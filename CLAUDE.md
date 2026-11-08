# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

```bash
# Build entire solution
dotnet build SqlChat.sln

# Run all tests
dotnet test SqlChat.sln

# Run a single test class
dotnet test tests/SqlChat.Tests/ --filter "FullyQualifiedName~SqlSecurityValidatorTests"

# Run tests with coverage
dotnet test SqlChat.sln /p:CollectCoverage=true

# Build a single project
dotnet build src/SqlChat.Client/SqlChat.Client.csproj
```

## Architecture Overview

**Target framework:** .NET 10 across all projects (`Directory.Build.props` sets this globally).

SqlChat converts natural language questions into executed, read-only SQL results. It introspects a database from a connection string, generates a validated `SELECT` statement via an LLM, executes it, and returns a typed result object to the caller.

```
SqlChat.Domain   → Core abstractions (no infrastructure deps)
SqlChat.Data     → Schema extraction (PostgreSQL + SQL Server via Dapper; EF convenience shim)
SqlChat.Service  → Orchestration, SQL security validation
SqlChat.Ai       → LLM SQL generation (Anthropic + OpenAI)
SqlChat.Client   → Public API facade + DI registration
SqlChat.Tests    → xUnit tests, uses EF Core InMemory for schema tests
```

### Data Flow

```
User (natural language)
  → SqlChatClient.QueryAsync<T> / QuerySingleAsync<T>
    → TextToSqlEngine
        → ISchemaProvider       → SchemaCatalog (introspected + enrichment merged)
        → ISqlGenerator         → raw SQL string
        → ISqlSecurityValidator → rejects non-SELECT (surfaced as IsSuccess=false, not thrown)
        → IQueryExecutor        → executes SELECT via Dapper, maps to T
            → transient error   → Polly retry (QueryRetryMax)
            → syntax error      → feed DB error back to ISqlGenerator, retry (LlmFixRetryMax)
  → SqlChatCollectionResult<T> / SqlChatResult<T> returned to user
```

SqlChat never throws to the caller — all outcomes are expressed via `IsSuccess` / `Error` on the result.

### Pluggable Extension Points

**Schema Providers** (`ISchemaProvider`):
- `PostgresSchemaProvider` — introspects a live Postgres DB via `INFORMATION_SCHEMA`
- `SqlServerSchemaProvider` — introspects a live SQL Server DB via `INFORMATION_SCHEMA` (T-SQL dialect)
- `ManualSchemaProvider` — wraps a pre-built `SchemaCatalog`; used in unit tests
- `EfCoreSchemaProvider` — walks an EF Core `IModel`; used by `UseSchemaFromEF` / `UseSchemaFromEF<TContext>()`. Surfaces `HasComment()` / `[Comment]` annotations as `Description` on tables and columns

Both relational providers extend `RelationalSchemaProvider` (abstract base) — add new DB support by subclassing it and supplying the three `INFORMATION_SCHEMA` queries.

**SQL Generators** (`ISqlGenerator`):
- `AnthropicSqlGenerator` — uses Anthropic Claude; configured via `SqlChatOptions`
- `OpenAiSqlGenerator` — uses OpenAI ChatGPT; configured via `OpenAiSqlGeneratorOptions`

**Security Validator** (`ISqlSecurityValidator`):
- `SqlSecurityValidator` — heuristic-only (no AST); blocks DML/DDL, `SELECT INTO`, enforces `SELECT`/`WITH` prefix

### Generator is Required

`ISqlGenerator` is a required parameter on `TextToSqlEngine` and `SqlChatBuilder.Build()` — there is no default fallback. Always call `.UseGenerator(...)`, `.UseAnthropic(...)`, or `.UseOpenAi(...)`.

### Domain Models (immutable records)

Located in `SqlChat.Domain/Models/`:
- `SchemaCatalog(Tables)` — full DB schema
- `TableDescriptor(Name, Schema?, Columns, Description?)` — one table/view
- `ColumnDescriptor(Name, StoreType, Description?, IsNullable, IsPrimaryKey, ForeignKey?, SemanticTags)` — one column

`Description` on both `TableDescriptor` and `ColumnDescriptor` is the primary hook for LLM context. It is fed into the system prompt by `SchemaPromptBuilder`, which is shared across all generators. `SemanticTags` on columns are `IReadOnlySet<string>` (case-insensitive).

### Public API

Consumers program against `ISqlChatClient` — the interface exposes `QueryAsync<T>`, `QuerySingleAsync<T>`, `GenerateSqlAsync`, `PreviewSqlAsync`, `ValidateGeneratedSql`, and `ValidateGeneratedSqlOrThrow`. `SqlChatClient` (the concrete class) has an `internal` constructor; always resolve it via DI or `SqlChatBuilder.Build()`.

`PreviewSqlAsync(string naturalLanguage)` generates and validates SQL without executing it. Returns the base `SqlChatResult` — never throws, consistent with the rest of the API. Distinct from `GenerateSqlAsync` which returns a raw `string` and throws on failure.

**DI path — PostgreSQL:**
```csharp
services.AddSqlChat()
    .UsePostgresConnectionString("Host=...;Database=...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");
```

**DI path — SQL Server:**
```csharp
services.AddSqlChat()
    .UseSqlServerConnectionString("Server=...;Database=...")
    .UseAnthropic(opts =>
    {
        opts.AnthropicApiKey = "sk-ant-...";
        opts.Dialect = SqlDialect.SqlServer;
    });
```

**EF Core — connection string extraction only:**
```csharp
// Extracts connection string from DbContext; introspects live DB via INFORMATION_SCHEMA
services.AddSqlChat()
    .UseEntityFramework<AppDbContext>()
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");
```

**EF Core — use the model as schema:**
```csharp
// Uses EfCoreSchemaProvider(DbContext.Model); HasComment() annotations become descriptions
// Detects Postgres vs SQL Server from ProviderName and wires the right executor
// InMemory contexts skip executor setup (GenerateSqlAsync works; QueryAsync unavailable)
services.AddSqlChat()
    .UseSchemaFromEF<AppDbContext>()
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");
```

**Schema enrichment — optional, merged on top of introspection:**
```csharp
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .Enrich(schema => schema
        .ExcludeTable("audit_log")
        .Table("orders", t => t
            .Column("status_id", "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled")));

// Or from a JSON file (deserialises to same object as the fluent builder)
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .EnrichFromFile("sqlchat-schema.json");
```

**Querying:**
```csharp
// Collection
SqlChatCollectionResult<Order> result = await sqlChat.QueryAsync<Order>("all orders this month");
if (!result.IsSuccess) { /* result.Error */ }
foreach (Order o in result.Data) { ... }

// Single
SqlChatResult<Customer> result = await sqlChat.QuerySingleAsync<Customer>("customer with email foo@bar.com");

// Preview — generate + validate SQL without executing
SqlChatResult preview = await sqlChat.PreviewSqlAsync("all orders this month");
if (preview.IsSuccess) Console.WriteLine(preview.Sql);
```

**Response types:**
```csharp
// Base — on every response
class SqlChatResult          { string Sql; bool IsSuccess; string? Error; }

// Variants
class SqlChatResult<T>           : SqlChatResult { T? Data; }
class SqlChatCollectionResult<T> : SqlChatResult { IEnumerable<T> Data; }
```

### Anthropic Integration

`AnthropicSqlGenerator` in `SqlChat.Ai` uses `Anthropic.SDK` v5.x. Key API shape:

```csharp
// Construction — AnthropicClient(APIAuthentication, HttpClient)
_client = new AnthropicClient(new APIAuthentication(apiKey), _httpClient);

// Request — GetClaudeMessageAsync(MessageParameters, CancellationToken)
var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
{
    Model = _options.AnthropicModel,   // string, e.g. "claude-opus-4-7"
    MaxTokens = _options.MaxOutputTokens,
    System = [new SystemMessage(systemPrompt, new CacheControl { Type = CacheControlType.ephemeral })],
    Messages =
    [
        new Message { Role = RoleType.User, Content = [new TextContent { Text = naturalLanguage }] }
    ]
}, ct).ConfigureAwait(false);

// Response
var text = response.FirstMessage?.Text;
```

`AnthropicClient` implements `IDisposable` — dispose it (and its `HttpClient`) when done.

`SqlChatOptions` controls: `Dialect` (defaults to `PostgreSql`), `MaxOutputTokens`, `AnthropicApiKey`, `AnthropicModel` (defaults to `"claude-opus-4-7"`).

### OpenAI Integration

`OpenAiSqlGenerator` in `SqlChat.Ai` uses the official `OpenAI` SDK v2.x. Key API shape:

```csharp
// Construction — OpenAIClient(apiKey)
_client = new OpenAIClient(_options.ApiKey!);

// Request — ChatClient.CompleteChatAsync(messages, options, ct)
var chatClient = _client.GetChatClient(_options.Model);
ClientResult<ChatCompletion> response = await chatClient.CompleteChatAsync(
    [new SystemChatMessage(systemPrompt), new UserChatMessage(naturalLanguage)],
    new ChatCompletionOptions { MaxOutputTokenCount = _options.MaxOutputTokens, Temperature = 0f },
    ct).ConfigureAwait(false);

// Response
var text = response.Value.Content[0].Text;
```

`OpenAIClient` does not implement `IDisposable` in v2.x; `OpenAiSqlGenerator.Dispose()` is a no-op but kept so consumers can use `using` blocks consistently.

`OpenAiSqlGeneratorOptions` (in `SqlChat.Ai.Options`) controls: `Dialect` (defaults to `PostgreSql`), `MaxOutputTokens`, `ApiKey`, `Model` (defaults to `"gpt-4o-mini"`).

Both generators call `SchemaPromptBuilder.BuildSystemPrompt` and `SchemaPromptBuilder.ExtractSql` — these are shared and provider-agnostic.

### EF Core Integration

Two EF paths are available:

- **`UseEntityFramework<TContext>()`** — extracts the connection string via `context.Database.GetConnectionString()` and delegates to `PostgresSchemaProvider` for live `INFORMATION_SCHEMA` introspection. Does not walk the EF model.
- **`UseSchemaFromEF<TContext>()`** — builds the schema from `EfCoreSchemaProvider(context.Model)`. `HasComment()` / `[Comment]` annotations on entities and properties flow through as `Description` on `TableDescriptor` / `ColumnDescriptor`. The executor is auto-detected from `context.Database.ProviderName` (Postgres or SQL Server); InMemory contexts get no executor. Enrichment (`.Enrich()`) is supported via `_concreteSchemaProviderType = typeof(EfCoreSchemaProvider)`.

### Security Constraint — Read-Only Queries Only

**SqlChat only ever executes or returns `SELECT` statements.** This is a hard invariant enforced before any SQL leaves the library. No exceptions, no opt-outs.

`SqlSecurityValidator` runs on every generated SQL string via `SqlHeuristicValidation`:
- SQL must begin with `SELECT` or `WITH` (CTEs)
- Rejects all DML: `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`
- Rejects all DDL: `CREATE`, `ALTER`, `DROP`
- Rejects privilege commands: `GRANT`, `REVOKE`
- Rejects `EXEC` / stored procedure calls
- Rejects `SELECT INTO` (would write data)
- Handles multi-statement input — rejects if *any* statement is non-`SELECT`
- Comment-strips and string-literal-quotes before scanning (prevents bypass via `-- DROP` tricks)

Violations throw `SqlSecurityException`. This validator runs in `TextToSqlEngine.GenerateSqlAsync` and cannot be skipped through the public API.

`PostgresQueryExecutor` additionally runs every query inside a `SET TRANSACTION READ ONLY` transaction that is always rolled back (the Polly retry wraps the whole connect → begin → query unit). Do not remove it — it is the database-level backstop if the heuristic validator is bypassed. SQL Server has no per-transaction equivalent; the README directs users to a `db_datareader`-only login.

## Implementation Status

The table below tracks what exists today vs what needs to be built.

| Feature | Status | Notes |
|---|---|---|
| `PostgresSchemaProvider` — DB introspection | ✅ Done | Primary schema source |
| `SqlServerSchemaProvider` / `SqlServerQueryExecutor` | ✅ Done | Full SQL Server support via `Microsoft.Data.SqlClient`; `UseSqlServerConnectionString` on both builders |
| `ISqlChatClient` — public client interface | ✅ Done | `SqlChatClient` implements it; registered in DI; constructor is `internal` |
| `AnthropicSqlGenerator` — LLM SQL generation | ✅ Done | Uses `Anthropic.SDK` v5.x |
| `OpenAiSqlGenerator` — LLM SQL generation | ✅ Done | Uses `OpenAI` SDK v2.x; `OpenAiSqlGeneratorOptions` in `SqlChat.Ai.Options`; `UseOpenAi()` DI extension |
| `SqlSecurityValidator` — heuristic read-only enforcement | ✅ Done | Blocks all non-SELECT |
| `SchemaCatalogBuilder` — manual schema construction | ✅ Done | Used in tests; basis for enrichment layer |
| `QueryAsync<T>()` / `QuerySingleAsync<T>()` | ✅ Done | Fully wired with LLM retry loop |
| `SqlChatResult` / `SqlChatResult<T>` / `SqlChatCollectionResult<T>` | ✅ Done | Result types with `IsSuccess`/`Error` |
| `IQueryExecutor` / `PostgresQueryExecutor` — Dapper + Polly execution | ✅ Done | Retries transient `NpgsqlException` via Polly |
| LLM fix-and-retry loop (`LlmFixRetryMax`) | ✅ Done | In `TextToSqlEngine.RunWithLlmRetryAsync` |
| `.Enrich()` / `.EnrichFromFile()` API | ✅ Done | `SchemaEnrichmentBuilder`, `EnrichingSchemaProvider` decorator; works with both Postgres and SQL Server |
| `ExcludeTable` / `ExcludeColumn` / column descriptions | ✅ Done | Applied by `EnrichingSchemaProvider` at schema fetch time |
| EF convenience shim — connection string extraction | ✅ Done | `UseEntityFramework(DbContext)` / `UseEntityFramework<TContext>()` extract connection string only |
| `internal` access modifier + `InternalsVisibleTo` | ✅ Done | `TextToSqlEngine` and `DapperRetryExtensions` are `internal`; `SqlChatClient` constructor is `internal`; `InternalsVisibleTo` grants `SqlChat.Client` and `SqlChat.Tests` access |
| `UseSchemaFromEF` / `UseSchemaFromEF<TContext>()` — EF model as schema source | ✅ Done | Uses `EfCoreSchemaProvider`; auto-detects executor from `ProviderName`; supports enrichment |
| `PreviewSqlAsync` — generate + validate SQL without executing | ✅ Done | Returns base `SqlChatResult`; never throws; distinct from `GenerateSqlAsync` (raw string, throws) |

**When implementing**: work top-to-bottom through the planned items. Each item that touches the public API surface should be reviewed against `docs/VISION.md` before coding.

## Coding Standards

### C# Language Conventions
- **File-scoped namespaces** (`namespace Foo;` not `namespace Foo { }`)
- **Immutable records** for all domain and value types (see `SqlChat.Domain/Models/`)
- **Sealed** on all concrete service/provider/generator classes unless designed for inheritance
- **Nullable reference types** enabled globally via `Directory.Build.props` — no `!` suppressions without a comment explaining why
- **Primary constructors** (C# 12) where they eliminate boilerplate; avoid when the constructor body has logic
- **Target-typed `new()`** preferred when the type is already stated on the left-hand side
- No `var` when the type is not immediately obvious from the right-hand side

### Naming Conventions

| Kind | Convention | Example |
|---|---|---|
| Types, methods, properties | PascalCase | `SqlChatClient`, `GenerateSqlAsync` |
| Private instance fields | `_camelCase` | `_generator`, `_schemaProvider` |
| Local variables, parameters | camelCase | `sql`, `cancellationToken` |
| Constants | PascalCase | `DefaultMaxTokens` |
| Interfaces | `I` prefix | `ISqlGenerator` |
| Async methods | `Async` suffix | `GetSchemaAsync` |

### Async Patterns
- `ConfigureAwait(false)` on **every** await — no exceptions
- `CancellationToken cancellationToken = default` as the **last parameter** on all public async methods
- No `async void` — always `async Task`

### Error Handling
- Validate at **public API boundaries only** (`SqlChatClient`, `SqlChatBuilder`) — not between internal layers
- Use `ArgumentNullException.ThrowIfNull(arg)` for null guards at public entry points
- Domain violations throw typed exceptions (`SqlSecurityException`) — never raw `Exception`
- Only swallow exceptions when the side-effect is optional and documented (the EF metadata `GetComment()` try/catch is the approved precedent)

### Testing
- **xUnit** only; no mocking framework — use hand-rolled fakes/stubs that implement the domain interfaces
- Test method naming: `MethodOrScenario_Condition_ExpectedBehavior`
  - Example: `ValidateSql_SelectIntoStatement_ReturnsFalse`
- Arrange / Act / Assert with a blank line between each section
- One logical concept per test; multiple assertions are fine when they verify the same outcome
- Tests live in `tests/SqlChat.Tests/` under a subfolder matching the source layer (`Security/`, `Data/`, `Schema/`, `Engine/`)

**Integration test gating** — tests skip rather than fail when their prerequisite is absent:

| Attribute | Skips unless |
|---|---|
| `[IntegrationFact]` | `SQLCHAT_RUN_INTEGRATION_TESTS=true` |
| `[AnthropicIntegrationFact]` | above + `ANTHROPIC_API_KEY` set |
| `[SqlServerIntegrationFact]` | above + `ANTHROPIC_API_KEY` + `SQLCHAT_SQLSERVER_CONNECTION_STRING` set |
| `[OpenAiIntegrationFact]` | `SQLCHAT_RUN_INTEGRATION_TESTS=true` + `OPENAI_API_KEY` set |

Use `[IntegrationFact]` only for pure infrastructure tests (DB connectivity ping) that don't call the LLM.

### Architecture Rules
- `SqlChat.Domain` has **zero** infrastructure or framework references — no EF, no Dapper, no HttpClient
- No domain logic in `SqlChat.Data` or `SqlChat.Ai` — those layers translate external representations into domain models
- One type per file; filename matches the type name exactly
- All async I/O goes behind an interface — orchestrators never call `HttpClient`, `DbConnection`, etc. directly

## Environment & Local Setup

### API Key & Environment

The integration test environment file lives in `tests/SqlChat.Tests/`. Copy the example and fill in your values:

```bash
cp tests/SqlChat.Tests/.env.example tests/SqlChat.Tests/.env
# edit .env — set SQLCHAT_RUN_INTEGRATION_TESTS=true and whichever API keys / connection strings you have
# tests skip rather than fail for any provider whose key is absent
```

`ANTHROPIC_API_KEY` is read at construction time by `AnthropicSqlGenerator` via `options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")`. `.env` is gitignored; never commit it. See `.env.example` for all available variables and their defaults.

**Important:** `IntegrationConfig` loads `.env` with `clobberExistingVars: true` — the file always wins over whatever is set in the shell. This prevents stale shell exports (e.g. an empty `ANTHROPIC_API_KEY`) from silently breaking tests.

### Docker — Local Dev Databases

A `docker-compose.yml` at the repo root defines both databases. Start everything with:

```bash
docker compose up -d       # start SQL Server + Postgres in the background
docker compose down        # stop and remove containers
docker compose logs -f     # stream logs from all containers
```

Both services include health checks — containers report `healthy` once the DB is accepting connections. The 2022 SQL Server image supports Apple Silicon natively.

Once running, add to your `.env`:

```
SQLCHAT_SQLSERVER_CONNECTION_STRING=Server=localhost,1433;Database=sqlchat_test;User Id=sa;Password=SqlChat_Strong@Pass1;TrustServerCertificate=true;
```

For Postgres via Docker (instead of a local install), use:
```
SQLCHAT_POSTGRES_CONNECTION_STRING=Host=localhost;Port=5432;Database=sqlchat_test;Username=postgres;Password=postgres;SSL Mode=Disable
```

The `SqlServerDatabaseFixture` creates and tears down the `sqlchat_test` database automatically on each test run.

### Running an End-to-End Smoke Test

**Against a live Postgres DB** (full pipeline — generate + execute):
```csharp
var client = new SqlChatBuilder()
    .UseConnectionString("Host=localhost;Database=mydb;Username=...;Password=...")
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
    {
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    }))
    .Build();

SqlChatCollectionResult<dynamic> result = await client.QueryAsync<dynamic>("show me all users");
Console.WriteLine(result.Sql);
```

**Against a live SQL Server DB:**
```csharp
var client = new SqlChatBuilder()
    .UseSqlServerConnectionString("Server=localhost,1433;Database=mydb;User Id=sa;Password=...;TrustServerCertificate=true")
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
    {
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"),
        Dialect = SqlDialect.SqlServer
    }))
    .Build();

SqlChatCollectionResult<dynamic> result = await client.QueryAsync<dynamic>("show me all users");
Console.WriteLine(result.Sql);
```

**SQL generation only** (no DB needed — useful for testing the LLM output):
```csharp
var schema = new SchemaCatalogBuilder()
    .AddTable("users", t => t
        .AddColumn("id", "int", primaryKey: true)
        .AddColumn("email", "varchar"))
    .Build();

var client = new SqlChatBuilder()
    .UseSchema(schema)
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
    {
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    }))
    .Build();

string sql = await client.GenerateSqlAsync("find all users");
Console.WriteLine(sql);
```

### Database Introspection

Both `PostgresSchemaProvider` and `SqlServerSchemaProvider` need a live connection. Use `SqlChatBuilder.UseConnectionString(cs)` / `UseSqlServerConnectionString(cs)` or the DI equivalents. No migrations needed — both providers read `INFORMATION_SCHEMA` directly.

### Project Dependency Graph

```
SqlChat.Domain      (no deps)
    ↑
SqlChat.Service     (Domain only) [InternalsVisibleTo: SqlChat.Client, SqlChat.Tests]
    ↑
SqlChat.Data        (Domain + EF Core + Dapper + Npgsql + Microsoft.Data.SqlClient + Polly)
    ↑
SqlChat.Client      (Domain + Data + Service + EF Core + DI Abstractions)
    ↑
SqlChat.Ai          (Domain + Client + Anthropic.SDK + OpenAI SDK + DI Abstractions) [InternalsVisibleTo: SqlChat.Tests]

SqlChat.Tests       (Ai + Client + Data + xUnit + EF InMemory + DotNetEnv)
```

Note: `SqlChat.Ai` references `SqlChat.Client` (not just Domain) because `SqlChatAiDiExtensions` registers `ISqlGenerator` into `SqlChatDiBuilder`. Keep this in mind — Ai-layer code must not pull in Data or Service logic directly.

## Known Limitations & Deferred Design

These are **intentional design choices**, not bugs. Don't "fix" them without a deliberate plan.

### Azure OpenAI — Not Implemented
There is no Azure OpenAI generator or options class. Vanilla OpenAI ChatGPT (`OpenAiSqlGenerator`) is fully implemented. Azure OpenAI is a separate integration (different endpoint, auth model, and deployment-name conventions) — if it is ever added, it needs a full implementation (generator + DI extension + options) designed together, following the same pattern.

### SemanticTags — Not Wired Up
`ColumnDescriptor.SemanticTags` (`IReadOnlySet<string>`, case-insensitive) exists on the domain model and can be set via `SchemaCatalogBuilder`, but **no schema provider populates it** and **`SchemaPromptBuilder` does not include it** in the system prompt. The hook is there for future enrichment; don't treat its absence as a bug.

### Composite Foreign Keys — Filtered Out
`EfCoreSchemaProvider` only maps **single-column FKs** to `ForeignKeyReference`. Multi-column FKs are silently skipped. This is a known limitation of the `UseSchemaFromEF` path; `PostgresSchemaProvider` and `SqlServerSchemaProvider` read FK relationships directly from `INFORMATION_SCHEMA` and are not affected.

### No Schema Caching
Every `GetSchemaAsync()` call rebuilds the `SchemaCatalog` from scratch. For `EfCoreSchemaProvider` this is cheap (in-memory EF model walk). For `PostgresSchemaProvider` this is a live DB query on every call. A caching decorator could wrap `ISchemaProvider` if needed — no changes to the core pipeline required.

### Read-Only Execution Only
SqlChat only ever generates and executes `SELECT` statements — it will never run a mutating query. The security validator enforces this before any SQL reaches the database. Violations are surfaced as `IsSuccess = false` on the result; nothing is executed.

## What NOT To Do

### Never Generate or Execute Mutating SQL

SqlChat is **read-only by design, at every layer.** It will only ever execute `SELECT` statements — never anything that writes, modifies, or deletes data.

`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, DDL (`CREATE`, `ALTER`, `DROP`), and privilege commands (`GRANT`, `REVOKE`) are permanently out of scope — not a future feature, not an opt-in. This is enforced by `SqlSecurityValidator` before any SQL reaches the database. Violations surface as `IsSuccess = false` on the result object.

### Never Throw to the Caller
SqlChat catches all exceptions internally. Transient DB errors go through Polly retry. Syntax errors trigger an LLM retry loop. If all retries are exhausted, the outcome is expressed via `IsSuccess = false` and `Error` on the result — not a thrown exception. Do not add `throw` statements on any code path reachable from `QueryAsync` or `QuerySingleAsync`.

### Never Bypass the Security Validator
`SqlSecurityValidator` runs inside `TextToSqlEngine.GenerateSqlAsync` and cannot be skipped via the public API. Do not add opt-outs, feature flags, or overloads that skip validation. If a query legitimately fails validation, the fix is in the prompt or schema context — not the validator.

### Never Add Infrastructure to SqlChat.Domain
`SqlChat.Domain` must have **zero** references to EF Core, Dapper, HttpClient, or any NuGet that touches I/O. Check before adding any `using` statement — if it requires a new `<PackageReference>`, it almost certainly doesn't belong in Domain.

### Never Mock in Tests
Hand-roll fakes that implement the domain interfaces (`ISchemaProvider`, `ISqlGenerator`, `ISqlSecurityValidator`). Do not add Moq, NSubstitute, or any mocking framework. The interfaces are small enough that fakes are 5–10 lines.

### Don't Implement Azure OpenAI Incrementally
There are no Azure OpenAI stubs to extend. If Azure OpenAI support is ever added, implement it fully in one pass (generator + DI extension + options) — do not layer it onto `OpenAiSqlGenerator` or add partial placeholders.

### Key Files

| File | Purpose |
|------|---------|
| `src/SqlChat.Domain/Abstractions/` | `ISchemaProvider`, `ISqlGenerator`, `ISqlSecurityValidator` |
| `src/SqlChat.Domain/Models/` | Immutable domain records |
| `src/SqlChat.Domain/Builders/SchemaCatalogBuilder.cs` | Fluent schema construction (used in tests) |
| `src/SqlChat.Data/Providers/RelationalSchemaProvider.cs` | Abstract base for INFORMATION_SCHEMA providers |
| `src/SqlChat.Data/Providers/PostgresSchemaProvider.cs` | Live Postgres introspection |
| `src/SqlChat.Data/Providers/SqlServerSchemaProvider.cs` | Live SQL Server introspection (T-SQL) |
| `src/SqlChat.Data/Execution/PostgresQueryExecutor.cs` | Postgres query execution via Dapper + Polly |
| `src/SqlChat.Data/Execution/SqlServerQueryExecutor.cs` | SQL Server query execution via Dapper + Polly |
| `src/SqlChat.Data/Providers/EfCoreSchemaProvider.cs` | EF Core `IModel` → `SchemaCatalog`; used by `UseSchemaFromEF` |
| `src/SqlChat.Service/Engine/TextToSqlEngine.cs` | Main orchestrator (internal) |
| `src/SqlChat.Service/Security/SqlHeuristicValidation.cs` | Security heuristics (internal) |
| `src/SqlChat.Ai/Generators/AnthropicSqlGenerator.cs` | Anthropic LLM SQL generation |
| `src/SqlChat.Ai/Generators/OpenAiSqlGenerator.cs` | OpenAI LLM SQL generation |
| `src/SqlChat.Ai/Options/OpenAiSqlGeneratorOptions.cs` | Options for `OpenAiSqlGenerator` |
| `src/SqlChat.Ai/Prompts/SchemaPromptBuilder.cs` | Schema → system prompt + SQL extraction; shared by all generators (internal) |
| `src/SqlChat.Client/ISqlChatClient.cs` | Public client interface |
| `src/SqlChat.Client/SqlChatClient.cs` | Implements `ISqlChatClient`; internal constructor |
| `src/SqlChat.Client/SqlChatBuilder.cs` | Non-DI public API |
| `src/SqlChat.Client/SqlChatDiBuilder.cs` | DI public API |
