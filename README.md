# SqlChat

A .NET library that converts natural language questions into executed, read-only SQL results. Ask a question in plain English — SqlChat generates a `SELECT` statement, validates it, runs it against your database, and returns typed results.

```csharp
SqlChatCollectionResult<Order> result = await sqlChat.QueryAsync<Order>("show me all orders placed this month");

Console.WriteLine(result.Sql);          // SELECT * FROM orders WHERE created_at >= ...
foreach (Order order in result.Data) { ... }
```

**SqlChat only ever generates and executes `SELECT` statements — it will never run a mutating query.** Every generated SQL is validated before execution; anything that isn't a `SELECT` or CTE is rejected and surfaced as a failure on the result object.

---

## Requirements

- .NET 10
- PostgreSQL database
- An [Anthropic API key](https://console.anthropic.com/)

---

## Quick Start

### Primary: connection string

Point SqlChat at your database with a connection string — it introspects the schema automatically:

```csharp
// Program.cs
services.AddSqlChat(opts =>
{
    opts.ConnectionString = "Host=localhost;Database=mydb;Username=...;Password=...";
    opts.Dialect          = SqlDialect.PostgreSql;
    opts.AnthropicApiKey  = builder.Configuration["Anthropic:ApiKey"];
});
```

Then inject `SqlChatClient` and query:

```csharp
public class ReportService(SqlChatClient sqlChat)
{
    public async Task RunAsync()
    {
        SqlChatCollectionResult<Order> result = await sqlChat.QueryAsync<Order>("all orders this month");

        if (result.IsFailed)
        {
            Console.WriteLine(result.Error);
            return;
        }

        foreach (Order order in result.Data)
            Console.WriteLine(order);
    }
}
```

### Convenience: EF Core

If you already have a `DbContext` registered, pass it as a type argument — SqlChat extracts the connection string from it. No EF model walking, no entity mapping.

```csharp
services.AddSqlChat<AppDbContext>(opts =>
{
    opts.AnthropicApiKey = builder.Configuration["Anthropic:ApiKey"];
});
```

---

## Querying

### Collection results

```csharp
SqlChatCollectionResult<T> result = await sqlChat.QueryAsync<T>("question");

result.IsSuccess   // bool
result.IsFailed    // bool
result.Sql         // the SELECT that was generated and executed
result.Data        // IEnumerable<T>
result.Error       // string?, populated on failure
```

### Single result

```csharp
SqlChatResult<T> result = await sqlChat.QuerySingleAsync<T>("question");

result.Data        // T?
```

Both variants inherit from the same `SqlChatResult` base — `Sql`, `IsSuccess`, `IsFailed`, and `Error` are always present. SqlChat never throws; check `IsFailed` and handle the outcome yourself.

---

## Schema Enrichment

SqlChat introspects your database automatically. For better SQL quality on complex or ambiguous schemas, layer in semantic descriptions on top:

```csharp
// In code
services.AddSqlChat(opts => { ... })
    .Enrich(schema => schema
        .ExcludeTable("audit_log")
        .Table("orders", t => t
            .Column("status_id", "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled")
            .Column("total",     "order total in USD, always positive"))
        .Table("customers", t => t
            .Description("All registered customers including inactive accounts")));

// From a JSON or YAML file
services.AddSqlChat(opts => { ... })
    .EnrichFromFile("sqlchat-schema.json");
```

Enrichment is additive — it merges on top of the introspected schema. Use `ExcludeTable` to hide tables you don't want the LLM to know about (security, noise reduction).

---

## Safety

SqlChat enforces a strict read-only contract that cannot be bypassed through the public API:

- Generated SQL must begin with `SELECT` or `WITH` (CTEs)
- `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, `DROP`, `CREATE`, `ALTER`, `GRANT`, `REVOKE`, and `EXEC` are all rejected
- `SELECT INTO` is rejected (it writes data)
- Multi-statement input is rejected if any statement is non-`SELECT`

Violations are surfaced as a failed result — `IsFailed = true`, `Error` contains the rejection reason. Nothing is executed.

---

## Retry Behaviour

SqlChat handles two categories of failure internally:

| Failure | Strategy |
|---|---|
| Transient DB errors (timeouts, connection resets) | Retried with exponential backoff via Polly |
| Bad SQL from the LLM (syntax errors at execution) | DB error fed back to LLM with context; generation retried |

Configure thresholds in options:

```csharp
services.AddSqlChat(opts =>
{
    opts.QueryRetryMax  = 3;  // default: 3 — transient DB retries
    opts.LlmFixRetryMax = 2;  // default: 2 — LLM fix-and-retry loops
});
```

---

## Configuration

| Property | Default | Description |
|---|---|---|
| `ConnectionString` | required | PostgreSQL connection string |
| `Dialect` | `PostgreSql` | SQL dialect hint sent to the LLM |
| `AnthropicApiKey` | `null` | Falls back to `ANTHROPIC_API_KEY` env var |
| `AnthropicModel` | `"claude-opus-4-7"` | Model used for SQL generation |
| `MaxOutputTokens` | `2048` | Max tokens in the generated SQL response |
| `QueryRetryMax` | `3` | Max retries for transient DB errors |
| `LlmFixRetryMax` | `2` | Max LLM retry loops for bad SQL |

---

## Project Structure

```
SqlChat.Domain   → Core abstractions and domain models (no infrastructure deps)
SqlChat.Data     → Schema introspection (PostgreSQL via Dapper) and query execution
SqlChat.Service  → Orchestration, retry logic, and SQL security validation
SqlChat.Ai       → Anthropic LLM-backed SQL generation
SqlChat.Client   → Public API facade and DI registration
```
