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
SqlChat.Ai       → Anthropic LLM-backed SQL generation
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
        → ISqlSecurityValidator → rejects non-SELECT (surfaced as IsFailed, not thrown)
        → IQueryExecutor        → executes SELECT via Dapper, maps to T
            → transient error   → Polly retry (QueryRetryMax)
            → syntax error      → feed DB error back to ISqlGenerator, retry (LlmFixRetryMax)
  → SqlChatCollectionResult<T> / SqlChatResult<T> returned to user
```

SqlChat never throws to the caller — all outcomes are expressed via `IsFailed` / `Error` on the result.

### Pluggable Extension Points

**Schema Providers** (`ISchemaProvider`):
- `PostgresSchemaProvider` — introspects a live Postgres DB via `INFORMATION_SCHEMA`
- `SqlServerSchemaProvider` — introspects a live SQL Server DB via `INFORMATION_SCHEMA` (T-SQL dialect)
- `ManualSchemaProvider` — wraps a pre-built `SchemaCatalog`; used in unit tests
- `EfCoreSchemaProvider` — legacy; walks an EF Core `IModel`. Being replaced: the EF convenience path now only extracts the connection string from the `DbContext` and delegates to `PostgresSchemaProvider`

Both relational providers extend `RelationalSchemaProvider` (abstract base) — add new DB support by subclassing it and supplying the three `INFORMATION_SCHEMA` queries.

**SQL Generators** (`ISqlGenerator`):
- `AnthropicSqlGenerator` — the only generator; requires an API key

**Security Validator** (`ISqlSecurityValidator`):
- `SqlSecurityValidator` — heuristic-only (no AST); blocks DML/DDL, `SELECT INTO`, enforces `SELECT`/`WITH` prefix

### Generator is Required

`ISqlGenerator` is a required parameter on `TextToSqlEngine` and `SqlChatBuilder.Build()` — there is no default fallback. Always call `.UseGenerator(...)` or `.UseAnthropic(...)`.

### Domain Models (immutable records)

Located in `SqlChat.Domain/Models/`:
- `SchemaCatalog(Tables)` — full DB schema
- `TableDescriptor(Name, Schema?, Columns, Description?)` — one table/view
- `ColumnDescriptor(Name, StoreType, Description?, IsNullable, IsPrimaryKey, ForeignKey?, SemanticTags)` — one column

`Description` on both `TableDescriptor` and `ColumnDescriptor` is the primary hook for LLM context. It is fed directly into the Anthropic system prompt by `SchemaPromptBuilder`. `SemanticTags` on columns are `IReadOnlySet<string>` (case-insensitive).

### Public API

Consumers program against `ISqlChatClient` — the interface exposes `QueryAsync<T>`, `QuerySingleAsync<T>`, `GenerateSqlAsync`, `ValidateGeneratedSql`, and `ValidateGeneratedSqlOrThrow`. `SqlChatClient` (the concrete class) has an `internal` constructor; always resolve it via DI or `SqlChatBuilder.Build()`.

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

**EF Core convenience — connection string extraction only:**
```csharp
// Extracts connection string from DbContext; does NOT walk the EF model
services.AddSqlChat()
    .UseEntityFramework<AppDbContext>()
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
if (result.IsFailed) { /* result.Error */ }
foreach (Order o in result.Data) { ... }

// Single
SqlChatResult<Customer> result = await sqlChat.QuerySingleAsync<Customer>("customer with email foo@bar.com");
```

**Response types:**
```csharp
// Base — on every response
class SqlChatResult          { string Sql; bool IsSuccess; bool IsFailed; string? Error; }

// Variants
class SqlChatResult<T>           : SqlChatResult { T? Data; }
class SqlChatCollectionResult<T> : SqlChatResult { IEnumerable<T> Data; }
```

### Anthropic Integration

`AnthropicSqlGenerator` in `SqlChat.Ai` uses `Anthropic.SDK` v3.3.0 (NuGet, by tghamm). Key API shape:

```csharp
// Construction — AnthropicClient(APIAuthentication, HttpClient)
_client = new AnthropicClient(new APIAuthentication(apiKey), _httpClient);

// Request — GetClaudeMessageAsync(MessageParameters, IList<Tool>?, CancellationToken)
var response = await _client.Messages.GetClaudeMessageAsync(new MessageParameters
{
    Model = _options.AnthropicModel,   // string, e.g. "claude-opus-4-7"
    MaxTokens = _options.MaxOutputTokens,
    SystemMessage = systemPrompt,      // string, not a list
    Messages =
    [
        new Message
        {
            Role = RoleType.User,
            Content = [new TextContent { Text = naturalLanguage }]  // Content is List<ContentBase>
        }
    ]
}, null, cancellationToken).ConfigureAwait(false);

// Response — FirstMessage is a TextContent helper property
var text = response.FirstMessage?.Text
    ?? throw new InvalidOperationException("Anthropic returned no text content.");
```

`AnthropicClient` implements `IDisposable` — dispose it (and its `HttpClient`) when done.

`SchemaPromptBuilder` formats `SchemaCatalog` into a markdown system prompt and strips markdown code fences from the response. `SqlChatAiOptions` controls: `Dialect` (defaults to `PostgreSql`), `MaxOutputTokens`, `AnthropicApiKey`, `AnthropicModel` (defaults to `"claude-opus-4-7"`).

### EF Core Integration (Connection String Only)

The EF convenience path (`UseEntityFramework<TContext>()`) extracts the connection string from the registered `DbContext` via `context.Database.GetConnectionString()` and passes it to `PostgresSchemaProvider`. **It does not walk the EF model.** `EfCoreSchemaProvider` (which maps `IModel` → `SchemaCatalog`) is legacy and will be removed.

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

## Implementation Status

The table below tracks what exists today vs what needs to be built.

| Feature | Status | Notes |
|---|---|---|
| `PostgresSchemaProvider` — DB introspection | ✅ Done | Primary schema source |
| `SqlServerSchemaProvider` / `SqlServerQueryExecutor` | ✅ Done | Full SQL Server support via `Microsoft.Data.SqlClient`; `UseSqlServerConnectionString` on both builders |
| `ISqlChatClient` — public client interface | ✅ Done | `SqlChatClient` implements it; registered in DI; constructor is `internal` |
| `AnthropicSqlGenerator` — LLM SQL generation | ✅ Done | Uses `Anthropic.SDK` v3.3.0 |
| `SqlSecurityValidator` — heuristic read-only enforcement | ✅ Done | Blocks all non-SELECT |
| `SchemaCatalogBuilder` — manual schema construction | ✅ Done | Used in tests; basis for enrichment layer |
| `QueryAsync<T>()` / `QuerySingleAsync<T>()` | ✅ Done | Fully wired with LLM retry loop |
| `SqlChatResult` / `SqlChatResult<T>` / `SqlChatCollectionResult<T>` | ✅ Done | Result types with `IsSuccess`/`IsFailed`/`Error` |
| `IQueryExecutor` / `PostgresQueryExecutor` — Dapper + Polly execution | ✅ Done | Retries transient `NpgsqlException` via Polly |
| LLM fix-and-retry loop (`LlmFixRetryMax`) | ✅ Done | In `TextToSqlEngine.RunWithLlmRetryAsync` |
| `.Enrich()` / `.EnrichFromFile()` API | ✅ Done | `SchemaEnrichmentBuilder`, `EnrichingSchemaProvider` decorator; works with both Postgres and SQL Server |
| `ExcludeTable` / `ExcludeColumn` / column descriptions | ✅ Done | Applied by `EnrichingSchemaProvider` at schema fetch time |
| EF convenience shim — connection string extraction | ✅ Done | `UseEntityFramework(DbContext)` / `UseEntityFramework<TContext>()` extract connection string only |
| `internal` access modifier + `InternalsVisibleTo` | ✅ Done | `TextToSqlEngine` and `DapperRetryExtensions` are `internal`; `SqlChatClient` constructor is `internal`; `InternalsVisibleTo` grants `SqlChat.Client` and `SqlChat.Tests` access |
| `EfCoreSchemaProvider` — EF model walking | ⚠️ Legacy | Still exists; not used by primary paths; pending removal |
| `AddSqlChat(opts => { ConnectionString, ... })` | 🔲 Planned | Unified DI init with single options object |

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
# edit .env — set ANTHROPIC_API_KEY, SQLCHAT_CONNECTION_STRING, and SQLCHAT_RUN_INTEGRATION_TESTS=true
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
SQLCHAT_CONNECTION_STRING=Host=localhost;Port=5432;Database=sqlchat_test;Username=postgres;Password=postgres;SSL Mode=Disable
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
        .AddColumn("id", "int", pk: true)
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
SqlChat.Ai          (Domain + Client + Anthropic.SDK + DI Abstractions) [InternalsVisibleTo: SqlChat.Tests]

SqlChat.Tests       (Ai + Client + Data + xUnit + EF InMemory + DotNetEnv)
```

Note: `SqlChat.Ai` references `SqlChat.Client` (not just Domain) because `SqlChatAiDiExtensions` registers `ISqlGenerator` into `SqlChatDiBuilder`. Keep this in mind — Ai-layer code must not pull in Data or Service logic directly.

## Known Limitations & Deferred Design

These are **intentional design choices**, not bugs. Don't "fix" them without a deliberate plan.

### Azure OpenAI — Placeholder Only
There are no Azure OpenAI generator or options classes. Do not implement it piecemeal — if Azure OpenAI support is added it needs a full implementation (generator + DI extension + options) designed together.

### SemanticTags — Not Wired Up
`ColumnDescriptor.SemanticTags` (`IReadOnlySet<string>`, case-insensitive) exists on the domain model and can be set via `SchemaCatalogBuilder`, but **no schema provider populates it** and **`SchemaPromptBuilder` does not include it** in the system prompt. The hook is there for future enrichment; don't treat its absence as a bug.

### EfCoreSchemaProvider — Legacy, Pending Removal
`EfCoreSchemaProvider` walks the EF Core `IModel` to build a `SchemaCatalog`. This is the old primary path and is being replaced. The EF convenience init (`AddSqlChat<TContext>()`) will extract the connection string from the `DbContext` and delegate to `PostgresSchemaProvider` instead. Do not invest further in `EfCoreSchemaProvider`.

### Composite Foreign Keys — Filtered Out
`EfCoreSchemaProvider` only maps **single-column FKs** to `ForeignKeyReference`. Multi-column FKs are silently skipped. This limitation is moot once `EfCoreSchemaProvider` is removed — `PostgresSchemaProvider` reads FK relationships directly from `INFORMATION_SCHEMA`.

### No Schema Caching
Every `GetSchemaAsync()` call rebuilds the `SchemaCatalog` from scratch. For `EfCoreSchemaProvider` this is cheap (in-memory EF model walk). For `PostgresSchemaProvider` this is a live DB query on every call. A caching decorator could wrap `ISchemaProvider` if needed — no changes to the core pipeline required.

### Read-Only Execution Only
SqlChat only ever generates and executes `SELECT` statements — it will never run a mutating query. The security validator enforces this before any SQL reaches the database. Violations are surfaced as `IsFailed = true` on the result; nothing is executed.

## What NOT To Do

### Never Generate or Execute Mutating SQL

SqlChat is **read-only by design, at every layer.** It will only ever execute `SELECT` statements — never anything that writes, modifies, or deletes data.

`INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, DDL (`CREATE`, `ALTER`, `DROP`), and privilege commands (`GRANT`, `REVOKE`) are permanently out of scope — not a future feature, not an opt-in. This is enforced by `SqlSecurityValidator` before any SQL reaches the database. Violations surface as `IsFailed = true` on the result object.

### Never Throw to the Caller
SqlChat catches all exceptions internally. Transient DB errors go through Polly retry. Syntax errors trigger an LLM retry loop. If all retries are exhausted, the outcome is expressed via `IsFailed = true` and `Error` on the result — not a thrown exception. Do not add `throw` statements on any code path reachable from `QueryAsync` or `QuerySingleAsync`.

### Never Bypass the Security Validator
`SqlSecurityValidator` runs inside `TextToSqlEngine.GenerateSqlAsync` and cannot be skipped via the public API. Do not add opt-outs, feature flags, or overloads that skip validation. If a query legitimately fails validation, the fix is in the prompt or schema context — not the validator.

### Never Add Infrastructure to SqlChat.Domain
`SqlChat.Domain` must have **zero** references to EF Core, Dapper, HttpClient, or any NuGet that touches I/O. Check before adding any `using` statement — if it requires a new `<PackageReference>`, it almost certainly doesn't belong in Domain.

### Never Mock in Tests
Hand-roll fakes that implement the domain interfaces (`ISchemaProvider`, `ISqlGenerator`, `ISqlSecurityValidator`). Do not add Moq, NSubstitute, or any mocking framework. The interfaces are small enough that fakes are 5–10 lines.

### Don't Touch Azure OpenAI Placeholders Incrementally
The Azure OpenAI properties in `SqlChatAiOptions` are stubs. Don't add partial implementations — either implement it fully (generator + DI + options) or leave it alone.

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
| `src/SqlChat.Data/Providers/EfCoreSchemaProvider.cs` | EF Core `IModel` → `SchemaCatalog` (legacy) |
| `src/SqlChat.Service/Engine/TextToSqlEngine.cs` | Main orchestrator (internal) |
| `src/SqlChat.Service/Security/SqlHeuristicValidation.cs` | Security heuristics (internal) |
| `src/SqlChat.Ai/Generators/AnthropicSqlGenerator.cs` | LLM SQL generation |
| `src/SqlChat.Ai/Prompts/SchemaPromptBuilder.cs` | Schema → system prompt + SQL extraction (internal) |
| `src/SqlChat.Client/ISqlChatClient.cs` | Public client interface |
| `src/SqlChat.Client/SqlChatClient.cs` | Implements `ISqlChatClient`; internal constructor |
| `src/SqlChat.Client/SqlChatBuilder.cs` | Non-DI public API |
| `src/SqlChat.Client/SqlChatDiBuilder.cs` | DI public API |
