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
- PostgreSQL **or** SQL Server database
- An [Anthropic API key](https://console.anthropic.com/) **or** an [OpenAI API key](https://platform.openai.com/)

---

## Quick Start

### Primary: connection string

Point SqlChat at your database with a connection string — it introspects the schema automatically via `INFORMATION_SCHEMA`:

```csharp
// Program.cs
services.AddSqlChat()
    .UsePostgresConnectionString("Host=localhost;Database=mydb;Username=...;Password=...")
    .UseAnthropic(opts => opts.AnthropicApiKey = builder.Configuration["Anthropic:ApiKey"]);
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

### Primary: SQL Server

```csharp
services.AddSqlChat()
    .UseSqlServerConnectionString("Server=localhost,1433;Database=mydb;User Id=sa;Password=...;TrustServerCertificate=true")
    .UseAnthropic(opts =>
    {
        opts.AnthropicApiKey = builder.Configuration["Anthropic:ApiKey"];
        opts.Dialect = SqlDialect.SqlServer;
    });
```

### Convenience: EF Core

If you already have a `DbContext` registered, pass it as a type argument — SqlChat extracts the connection string from it. No EF model walking, no entity mapping.

```csharp
services.AddSqlChat()
    .UseEntityFramework<AppDbContext>()
    .UseAnthropic(opts => opts.AnthropicApiKey = builder.Configuration["Anthropic:ApiKey"]);
```

### Primary: OpenAI

```csharp
services.AddSqlChat()
    .UsePostgresConnectionString("Host=localhost;Database=mydb;Username=...;Password=...")
    .UseOpenAi(opts => opts.ApiKey = builder.Configuration["OpenAI:ApiKey"]);
```

For SQL Server:

```csharp
services.AddSqlChat()
    .UseSqlServerConnectionString("Server=localhost,1433;Database=mydb;User Id=sa;Password=...;TrustServerCertificate=true")
    .UseOpenAi(opts =>
    {
        opts.ApiKey  = builder.Configuration["OpenAI:ApiKey"];
        opts.Model   = "gpt-4o";                 // default is gpt-4o-mini
        opts.Dialect = SqlDialect.SqlServer;
    });
```

### From a YAML schema file (no live DB)

If your database lives in a separate repo or you don't want a live connection in your service, bootstrap the schema from a YAML file instead:

```csharp
// DI
services.AddSqlChat()
    .UseSchemaFromFile("sqlchat-schema.yaml")
    .UseOpenAi(opts => opts.ApiKey = builder.Configuration["OpenAI:ApiKey"]);

// Non-DI
var client = new SqlChatBuilder()
    .UseSchemaFromFile("sqlchat-schema.yaml")
    .UseGenerator(new OpenAiSqlGenerator(new OpenAiSqlGeneratorOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    }))
    .Build();
```

See [Schema from DDL Files](#schema-from-ddl-files) for how to generate the YAML.

### Without DI

```csharp
// Anthropic
var client = new SqlChatBuilder()
    .UseConnectionString("Host=localhost;Database=mydb;Username=...;Password=...")
    .UseGenerator(new AnthropicSqlGenerator(new SqlChatOptions
    {
        AnthropicApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
    }))
    .Build();

// OpenAI
var client = new SqlChatBuilder()
    .UseConnectionString("Host=localhost;Database=mydb;Username=...;Password=...")
    .UseGenerator(new OpenAiSqlGenerator(new OpenAiSqlGeneratorOptions
    {
        ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
    }))
    .Build();

SqlChatCollectionResult<Order> result = await client.QueryAsync<Order>("all orders this month");
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
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .Enrich(schema => schema
        .ExcludeTable("audit_log")
        .Table("orders", t => t
            .Column("status_id", "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled")
            .Column("total",     "order total in USD, always positive"))
        .Table("customers", t => t
            .Description("All registered customers including inactive accounts")));

// From a JSON file
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .EnrichFromFile("sqlchat-schema.json");
```

**JSON file format:**

```json
{
  "excludedTables": ["audit_log"],
  "tables": {
    "orders": {
      "columns": {
        "status_id": { "description": "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled" }
      }
    },
    "customers": {
      "description": "All registered customers including inactive accounts"
    }
  }
}
```

Enrichment is additive — it merges on top of the introspected schema. Use `ExcludeTable` / `ExcludeColumn` to hide tables or columns you don't want the LLM to see.

---

## Schema from DDL Files

When your database lives in a separate repository or you don't have a live connection available at runtime, SqlChat can build a `SchemaCatalog` directly from your SQL DDL files (standard `CREATE TABLE` statements).

### Step 1 — Scan DDL and save YAML (in your DB repo)

```csharp
using SqlChat.Data.Schema;
using SqlChat.Data.Serialization;

// Recursively scan a directory of .sql files
SchemaCatalog schema = DdlSchemaScanner.ScanDirectory("./migrations");

// Or scan a single file
SchemaCatalog schema = DdlSchemaScanner.ScanFile("./schema.sql");

// Persist as YAML — check this into your service repo
await SchemaCatalogSerializer.SaveYamlAsync(schema, "sqlchat-schema.yaml");
```

### Step 2 — Use the YAML in your service (no live DB required)

```csharp
services.AddSqlChat()
    .UseSchemaFromFile("sqlchat-schema.yaml")
    .UseOpenAi(opts => opts.ApiKey = builder.Configuration["OpenAI:ApiKey"]);
```

### What the scanner understands

- `CREATE TABLE` and `CREATE TABLE IF NOT EXISTS`
- Schema-qualified names (`public.orders`)
- Inline and table-level `PRIMARY KEY` constraints
- Inline and table-level `FOREIGN KEY ... REFERENCES` constraints
- Multi-word types (`DOUBLE PRECISION`, `CHARACTER VARYING`, `TIMESTAMP WITH TIME ZONE`)
- Types with precision (`DECIMAL(10,2)`, `VARCHAR(255)`)
- `--` line comments and `/* */` block comments

EF Core migration file parsing is not yet supported — use the raw DDL path for now.

### Exporting from a live database

You can also export a live database's introspected schema to YAML — useful for a one-time snapshot that you then annotate and check in:

```csharp
using SqlChat.Data.Providers;
using SqlChat.Data.Serialization;

// PostgreSQL
var provider = new PostgresSchemaProvider("Host=localhost;Database=mydb;...");
SchemaCatalog schema = await provider.GetSchemaAsync();
await SchemaCatalogSerializer.SaveYamlAsync(schema, "sqlchat-schema.yaml");

// SQL Server
var provider = new SqlServerSchemaProvider("Server=localhost,1433;Database=mydb;...");
SchemaCatalog schema = await provider.GetSchemaAsync();
await SchemaCatalogSerializer.SaveYamlAsync(schema, "sqlchat-schema.yaml");
```

### Annotating the YAML to help the LLM

The exported YAML supports optional `description` fields on both tables and columns. Filling these in is the most effective way to improve SQL quality on ambiguous or domain-specific schemas:

```yaml
tables:
  - name: orders
    schema: public
    description: "Customer purchase orders. Only includes completed checkouts."
    columns:
      - name: id
        storeType: int4
        isPrimaryKey: true
        isNullable: false
      - name: status_id
        storeType: int4
        isNullable: false
        description: "1 = pending, 2 = shipped, 3 = delivered, 4 = cancelled"
      - name: total
        storeType: numeric
        isNullable: true
        description: "Order total in USD cents, always positive"
      - name: customer_id
        storeType: int4
        isNullable: false
        foreignKey:
          principalTable: customers
          principalColumn: id
```

Edit the file, check it into your service repo, then load it with `UseSchemaFromFile`. No live DB connection is required at runtime.

### Working with the catalog directly in code

```csharp
// Build manually
var catalog = new SchemaCatalogBuilder()
    .AddTable("orders", t => t
        .AddColumn("id", "int", pk: true)
        .AddColumn("customer_id", "int")
        .AddColumn("total", "decimal(10,2)"))
    .Build();

// Serialize to YAML string
string yaml = SchemaCatalogSerializer.ToYaml(catalog);

// Deserialize from YAML string
SchemaCatalog catalog = SchemaCatalogSerializer.FromYaml(yaml);
```

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

Configure thresholds via generator options and `WithOptions`:

```csharp
services.AddSqlChat()
    .UsePostgresConnectionString("...", queryRetryMax: 3)
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .WithOptions(opts =>
    {
        opts.QueryRetryMax  = 3;  // transient DB retries
        opts.LlmFixRetryMax = 2;  // LLM fix-and-retry loops
    });
```

---

## Configuration

**Anthropic (`SqlChatOptions`):**

| Property | Default | Description |
|---|---|---|
| `AnthropicApiKey` | `null` | Falls back to `ANTHROPIC_API_KEY` env var |
| `AnthropicModel` | `"claude-opus-4-7"` | Model used for SQL generation |
| `MaxOutputTokens` | `2048` | Max tokens in the generated SQL response |
| `Dialect` | `PostgreSql` | SQL dialect for prompt and schema formatting |
| `QueryRetryMax` | `3` | Max retries for transient DB errors |
| `LlmFixRetryMax` | `2` | Max LLM retry loops for bad SQL |

**OpenAI (`OpenAiSqlGeneratorOptions`):**

| Property | Default | Description |
|---|---|---|
| `ApiKey` | `null` | Your OpenAI API key |
| `Model` | `"gpt-4o-mini"` | Model used for SQL generation |
| `MaxOutputTokens` | `2048` | Max tokens in the generated SQL response |
| `Dialect` | `PostgreSql` | SQL dialect for prompt and schema formatting |

---

## Running Tests

### Unit tests

```bash
dotnet test SqlChat.sln
```

Integration tests are skipped automatically unless explicitly enabled.

### Integration tests

Integration tests run against live databases and call the LLM APIs. Copy the example env file and fill in your values:

```bash
cp tests/SqlChat.Tests/.env.example tests/SqlChat.Tests/.env
```

Then set `SQLCHAT_RUN_INTEGRATION_TESTS=true` in `.env` and run:

```bash
dotnet test SqlChat.sln
```

**PostgreSQL variables:**

| Variable | Description |
|---|---|
| `ANTHROPIC_API_KEY` | Your Anthropic API key |
| `OPENAI_API_KEY` | Your OpenAI API key |
| `SQLCHAT_CONNECTION_STRING` | Postgres connection string |
| `SQLCHAT_RUN_INTEGRATION_TESTS` | Set to `true` to enable |

**SQL Server variables** (optional — SQL Server tests are skipped if absent):

| Variable | Description |
|---|---|
| `SQLCHAT_SQLSERVER_CONNECTION_STRING` | SQL Server connection string |

A `docker-compose.yml` at the repo root defines both SQL Server and Postgres. Start the full test environment with:

```bash
docker compose up -d
```

The SQL Server 2022 image supports Apple Silicon natively. The test fixture creates and tears down the test database automatically.

---

## Project Structure

```
SqlChat.Domain   → Core abstractions and domain models (no infrastructure deps)
SqlChat.Data     → Schema introspection (PostgreSQL + SQL Server), DDL scanning, YAML serialization, query execution (Dapper + Polly)
SqlChat.Service  → Orchestration, LLM retry loop, and SQL security validation
SqlChat.Ai       → LLM SQL generation (Anthropic + OpenAI)
SqlChat.Client   → Public API facade and DI registration
```
