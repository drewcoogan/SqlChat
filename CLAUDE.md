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
SqlChat.Data     → Schema extraction (PostgreSQL via Dapper; EF convenience shim)
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
- `PostgresSchemaProvider` — **primary path**; introspects a live Postgres DB via `INFORMATION_SCHEMA` using a connection string
- `ManualSchemaProvider` — wraps a pre-built `SchemaCatalog`; used in unit tests
- `EfCoreSchemaProvider` — legacy; walks an EF Core `IModel`. Being replaced: the EF convenience path now only extracts the connection string from the `DbContext` and delegates to `PostgresSchemaProvider`

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

### Public API (Target Design)

> The API shape below reflects the **intended architecture**. Some of this is not yet fully implemented — use it as the target when adding or refactoring code.

**Primary DI path — connection string:**
```csharp
services.AddSqlChat(opts =>
{
    opts.ConnectionString = "Host=...;Database=...";
    opts.Dialect          = SqlDialect.PostgreSql;
    opts.AnthropicApiKey  = "sk-ant-...";
    opts.QueryRetryMax    = 3;   // transient DB retries (Polly)
    opts.LlmFixRetryMax   = 2;   // bad SQL → LLM retry loops
});
```

**EF Core convenience — connection string extraction only:**
```csharp
// Extracts connection string from DbContext; does NOT walk the EF model
services.AddSqlChat<AppDbContext>(opts =>
{
    opts.AnthropicApiKey = "sk-ant-...";
});
```

**Schema enrichment — optional, merged on top of introspection:**
```csharp
services.AddSqlChat(opts => { ... })
    .Enrich(schema => schema
        .ExcludeTable("audit_log")
        .Table("orders", t => t
            .Column("status_id", "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled")));

// Or from a JSON/YAML file (deserialises to same object as fluent builder)
services.AddSqlChat(opts => { ... })
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

The EF convenience path (`AddSqlChat<TContext>()`) extracts the connection string from the registered `DbContext` via `context.Database.GetConnectionString()` and passes it to `PostgresSchemaProvider`. **It does not walk the EF model.** `EfCoreSchemaProvider` (which maps `IModel` → `SchemaCatalog`) is legacy and will be removed once the connection-string-primary architecture is fully implemented.

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

The codebase is in a **pre-refactor state**. The architecture documented above reflects the target design. The table below tracks what exists today vs what needs to be built.

| Feature | Status | Notes |
|---|---|---|
| `PostgresSchemaProvider` — DB introspection | ✅ Exists | Primary schema source going forward |
| `AnthropicSqlGenerator` — LLM SQL generation | ✅ Exists | Uses `Anthropic.SDK` v3.3.0 |
| `SqlSecurityValidator` — heuristic read-only enforcement | ✅ Exists | Blocks all non-SELECT |
| `SchemaCatalogBuilder` — manual schema construction | ✅ Exists | Used in tests; basis for enrichment layer |
| `EfCoreSchemaProvider` — EF model walking | ⚠️ Legacy | To be replaced; EF path should extract connection string only |
| `AddSqlChat(opts => { ConnectionString, ... })` | 🔲 Planned | Current API takes no options object; connection string is a separate call |
| `QueryAsync<T>()` / `QuerySingleAsync<T>()` | 🔲 Planned | Currently only `GenerateSqlAsync()` exists (returns raw string) |
| `SqlChatResult` / `SqlChatResult<T>` / `SqlChatCollectionResult<T>` | 🔲 Planned | No result types exist yet |
| `IQueryExecutor` — Dapper execution layer | 🔲 Planned | No execution layer exists; SqlChat currently returns SQL strings only |
| Polly retry on transient DB errors (`QueryRetryMax`) | 🔲 Planned | Polly exists in `DapperRetryExtensions` but is hard-coded and not wired to execution |
| LLM fix-and-retry loop (`LlmFixRetryMax`) | 🔲 Planned | No retry loop in `TextToSqlEngine` |
| `.Enrich()` / `.EnrichFromFile()` API | 🔲 Planned | No enrichment API exists |
| `ExcludeTable` / column-level visibility filtering | 🔲 Planned | No filtering mechanism exists |
| `internal` access modifier + `InternalsVisibleTo` | 🔲 Planned | All classes currently `public` |

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

### API Key

Copy `.env.example` to `.env` and fill in your key — the test runner loads it automatically via `DotNetEnv`:

```bash
cp .env.example .env
# edit .env and set ANTHROPIC_API_KEY=sk-ant-...
```

Alternatively, export it in your shell:

```bash
export ANTHROPIC_API_KEY=sk-ant-...
```

The key is read at construction time by `AnthropicSqlGenerator`: `options.AnthropicApiKey ?? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")`. `.env` is gitignored; never commit it.

### Running an End-to-End Smoke Test

> This uses the **current pre-refactor API** (`GenerateSqlAsync` returning a SQL string). Once `QueryAsync<T>` and the execution layer are implemented, update this example.

```csharp
var schema = new SchemaCatalogBuilder()
    .AddTable("users", t => t
        .AddColumn("id", "int", pk: true)
        .AddColumn("email", "varchar"))
    .Build();

var client = new SqlChatBuilder()
    .UseSchema(schema)
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatAiOptions
    {
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    }))
    .Build();

var sql = await client.GenerateSqlAsync("Find all users");
Console.WriteLine(sql);
```

### Postgres Introspection (optional)

`PostgresSchemaProvider` needs a live connection. Use `SqlChatBuilder.UsePostgresConnectionString(cs)` or the DI equivalent. No migrations needed — it reads `INFORMATION_SCHEMA` directly.

### Project Dependency Graph

```
SqlChat.Domain      (no deps)
    ↑
SqlChat.Service     (Domain only)
    ↑
SqlChat.Data        (Domain + EF Core + Dapper + Npgsql + Polly)
    ↑
SqlChat.Client      (Domain + Data + Service + EF Core + DI Abstractions)
    ↑
SqlChat.Ai          (Domain + Client + Anthropic.SDK + DI Abstractions)

SqlChat.Tests       (Ai + Client + Data + xUnit + EF InMemory + DotNetEnv)
```

Note: `SqlChat.Ai` references `SqlChat.Client` (not just Domain) because `SqlChatAiDiExtensions` registers `ISqlGenerator` into `SqlChatDiBuilder`. Keep this in mind — Ai-layer code must not pull in Data or Service logic directly.

## Known Limitations & Deferred Design

These are **intentional design choices**, not bugs. Don't "fix" them without a deliberate plan.

### Azure OpenAI — Placeholder Only
`SqlChatAiOptions` has `AzureOpenAiEndpoint`, `AzureOpenAiApiKey`, and `AzureOpenAiDeployment` properties, and `.env.example` documents Azure vars — but **no `AzureOpenAiSqlGenerator` exists**. These are stubs. The `SQLCHAT_PROVIDER` env var in `.env.example` is also not read by any code; provider selection happens at construction time. Do not implement Azure OpenAI piecemeal — remove the placeholder properties and design the generator together.

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
| `src/SqlChat.Data/Providers/EfCoreSchemaProvider.cs` | EF Core `IModel` → `SchemaCatalog` |
| `src/SqlChat.Data/Providers/PostgresSchemaProvider.cs` | Live Postgres introspection |
| `src/SqlChat.Service/Engine/TextToSqlEngine.cs` | Main orchestrator |
| `src/SqlChat.Service/Security/SqlHeuristicValidation.cs` | Security heuristics |
| `src/SqlChat.Ai/Generators/AnthropicSqlGenerator.cs` | LLM SQL generation |
| `src/SqlChat.Ai/Prompts/SchemaPromptBuilder.cs` | Schema → system prompt + SQL extraction |
| `src/SqlChat.Client/SqlChatBuilder.cs` | Non-DI public API |
| `src/SqlChat.Client/SqlChatDiBuilder.cs` | DI public API |
