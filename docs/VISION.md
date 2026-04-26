# SqlChat — Project Vision & Goals

## What It Is

SqlChat is a .NET library that converts natural language questions into executed, read-only SQL results. The caller asks a question in plain English; SqlChat generates a `SELECT` statement, validates it, executes it against the database, and returns a typed result object — including the SQL that was run, the results, and any error details.

The core value proposition: give SqlChat a connection string and an Anthropic API key, ask a question, get back typed results with strong safety guarantees and no risk of data mutation.

## What It Is Not

- **Not a query builder.** The caller writes natural language, not SQL.
- **Not an ORM.** SqlChat has no concept of tracked entities, change sets, or migrations.
- **Not a reporting engine.** It executes queries; it does not render charts, paginate results, or cache query outputs.
- **Not write-capable, ever.** `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, DDL, and privilege commands are permanently out of scope.

## Core Design Decisions

### 1. Connection string is the primary entry point

SqlChat introspects the database schema directly from a connection string via `INFORMATION_SCHEMA`. The caller does not need to define or maintain a separate schema object — pointing at the database is enough to get started.

### 2. EF Core is a convenience shim, not a dependency

If the caller already has a `DbContext` registered, they can pass it as a type argument and SqlChat will extract the connection string from it. **EF is only used for its connection string — not for entity metadata, model walking, or anything else.**

### 3. Enrichment is the quality lever

Introspection gives SqlChat the structure of the database (tables, columns, types, PKs, FKs). It cannot give it semantics — what `status_id = 3` means, which tables are soft-deleted, which columns contain PII. Callers layer this context on top via the enrichment API, which merges descriptions into the introspected schema before it reaches the LLM prompt.

Enrichment can be defined in code or loaded from a JSON file. Both produce the same underlying `SchemaEnrichment` object, so the enrichment source is interchangeable.

Enrichment also controls **table and column visibility** — callers can exclude tables or columns they don't want the LLM to know about, for both quality and security reasons.

### 4. SqlChat owns execution

SqlChat generates the SQL, validates it, and executes it. Results are returned in a structured response object. The caller never touches a `DbConnection` or handles raw query results — they get back a `SqlChatResult` and decide what to do with it.

### 5. Errors are returned, not thrown

SqlChat does not throw exceptions to the caller. All outcomes — success, transient DB failure, LLM error, security rejection — are expressed through the result object. The caller inspects `IsFailed` and `Error` and handles the outcome themselves.

### 6. Retry is handled internally

Two categories of retry, both configurable via options at init:

- **Transient DB errors** (timeouts, connection resets) — retried automatically via Polly with exponential backoff, up to `QueryRetryMax`
- **Bad SQL from the LLM** (syntax errors caught at execution) — the DB error is fed back to the LLM with the original question and context, and generation is retried up to `LlmFixRetryMax`

### 7. Read-only by hard constraint

The SQL security validator runs on every generated SQL string before execution. Anything that is not a `SELECT` or CTE is rejected. This cannot be bypassed through the public API, and no opt-out will ever be added.

## Response Types

```csharp
// Base — shared fields on every response
class SqlChatResult
{
    string  Sql        // the SQL that was generated and executed
    bool    IsSuccess
    bool    IsFailed
    string? Error      // populated on failure
}

// Single result
class SqlChatResult<T> : SqlChatResult
{
    T? Data
}

// Collection result
class SqlChatCollectionResult<T> : SqlChatResult
{
    IEnumerable<T> Data
}
```

## Target API

### DI registration

```csharp
// Primary: connection string
services.AddSqlChat()
    .UsePostgresConnectionString("Host=...;Database=...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");

// EF convenience — extracts connection string from DbContext only
services.AddSqlChat()
    .UseEntityFramework<AppDbContext>()
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");

// Retry thresholds
services.AddSqlChat()
    .UsePostgresConnectionString("...", queryRetryMax: 3)
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .WithOptions(opts =>
    {
        opts.LlmFixRetryMax = 2;
    });
```

### Schema enrichment (optional)

```csharp
// In code — fluent builder
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .Enrich(schema => schema
        .ExcludeTable("audit_log")
        .Table("orders", t => t
            .Column("status_id", "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled"))
        .Table("customers", t => t
            .Description("All registered customers including inactive accounts")));

// From a JSON file — deserialises to the same object as the fluent builder
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .EnrichFromFile("sqlchat-schema.json");
```

### Querying

```csharp
// Collection — returns SqlChatCollectionResult<T>
SqlChatCollectionResult<Order> result = await sqlChat.QueryAsync<Order>("show me all orders this month");

if (result.IsFailed)
{
    logger.LogError("Query failed: {Error}", result.Error);
    return;
}

foreach (Order order in result.Data) { ... }
Console.WriteLine(result.Sql); // "SELECT * FROM orders WHERE ..."

// Single — returns SqlChatResult<T>
SqlChatResult<Customer> result = await sqlChat.QuerySingleAsync<Customer>("find customer with email foo@bar.com");
```

## Package

- **NuGet ID:** `SqlChat`
- **Primary namespace:** `SqlChat`
- Implementation classes are `internal`; only the public API surface is exposed
- `InternalsVisibleTo` grants test project access to internals

## What Good Looks Like

A caller drops SqlChat into an existing ASP.NET Core app, points it at their Postgres database, provides an API key, and asks natural language questions. They get back typed results with the executed SQL attached — no schema definitions required to get started. When query quality needs to improve, they layer in enrichment incrementally, either in code or via a JSON file a non-developer can maintain.

## Non-Goals

- SQL Server support is not a current priority (Postgres is the target dialect)
- Streaming SQL generation is not planned
- Multi-LLM support (Azure OpenAI, etc.) is deferred until Anthropic integration is solid
