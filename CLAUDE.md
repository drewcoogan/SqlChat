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

The solution is a layered text-to-SQL library:

```
SqlChat.Domain   → Core abstractions (no infrastructure deps)
SqlChat.Data     → Schema extraction (EF Core, PostgreSQL via Dapper)
SqlChat.Service  → Orchestration, SQL security validation
SqlChat.Ai       → Anthropic LLM-backed SQL generation
SqlChat.Client   → Public API facade + DI registration
SqlChat.Tests    → xUnit tests, uses EF Core InMemory for schema tests
```

### Data Flow

```
User (natural language)
  → SqlChatClient
    → TextToSqlEngine
        → ISchemaProvider   → SchemaCatalog
        → ISqlGenerator     → raw SQL string
        → ISqlSecurityValidator → SqlValidationResult (throws SqlSecurityException on failure)
  → validated SQL returned to user
```

### Pluggable Extension Points

**Schema Providers** (`ISchemaProvider`):
- `EfCoreSchemaProvider` — reads an EF Core `IModel`; the primary MVP path
- `PostgresSchemaProvider` — introspects a live Postgres DB via `INFORMATION_SCHEMA` (useful for testing with seeded data)
- `ManualSchemaProvider` — wraps a pre-built `SchemaCatalog` (useful in unit tests)

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

**Without DI (fluent builder):**
```csharp
var client = new SqlChatBuilder()
    .UseEntityFramework(dbContext.Model)
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatAiOptions { AnthropicApiKey = "..." }))
    .Build();

var sql = await client.GenerateSqlAsync("Show me all active users");
```

**With DI:**
```csharp
services.AddSqlChat()
    .UseEntityFramework<AppDbContext>()
    .UseAnthropic(opts => { opts.AnthropicApiKey = "..."; });
```

### Anthropic Integration

`AnthropicSqlGenerator` in `SqlChat.Ai` uses the community `Anthropic` NuGet package v1.0.0 (OpenAPI-generated SDK, **not** the official Anthropic SDK). Key API shape:

```csharp
var api = new AnthropicApi(httpClient, new Uri("https://api.anthropic.com"));
api.AuthorizeUsingApiKey(apiKey);
var response = await api.CreateMessageAsync(new CreateMessageRequest { ... });
// response.Content is OneOf<string, IList<Block>>
// Block.IsText / Block.Text.Text for text extraction
```

`SchemaPromptBuilder` formats `SchemaCatalog` into a markdown system prompt and strips markdown code fences from the response. `SqlChatAiOptions` controls: `Dialect` (defaults to `PostgreSql`), `MaxOutputTokens`, `AnthropicApiKey`, `AnthropicModel`.

### EF Core Schema Extraction

`EfCoreSchemaProvider` takes `IModel` (from `DbContext.Model`), iterates entity types, and maps EF properties → `ColumnDescriptor`. It reads:
- Column names, store types, nullability, PK membership
- FK navigations → `ForeignKeyReference`
- EF `HasComment()` → `Description` (guarded with try/catch — EF's runtime read-optimized model strips this metadata; real DB providers retain it)
- Filters owned entity types (they embed into the parent table)

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
