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

If the caller already has a `DbContext` registered, they can use one of two EF paths:

- **`UseEntityFramework<TContext>()`** — extracts the connection string from the `DbContext` and introspects the live database via `INFORMATION_SCHEMA`. No EF model walking.
- **`UseSchemaFromEF<TContext>()`** — reads the schema from the EF model itself (`DbContext.Model`), giving the LLM richer context via `HasComment()` / `[Comment]` annotations. The right query executor (Postgres or SQL Server) is detected from `ProviderName`.

Neither path requires EF migrations or entity tracking — EF is only used as a source of connection string or model metadata.

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
// PostgreSQL
services.AddSqlChat()
    .UsePostgresConnectionString("Host=...;Database=...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "sk-ant-...");

// SQL Server
services.AddSqlChat()
    .UseSqlServerConnectionString("Server=...;Database=...")
    .UseAnthropic(opts =>
    {
        opts.AnthropicApiKey = "sk-ant-...";
        opts.Dialect = SqlDialect.SqlServer;
    });

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

- Streaming SQL generation is not planned
- Azure OpenAI is not planned — vanilla OpenAI (`OpenAiSqlGenerator`) is fully supported; Azure OpenAI is a distinct integration with different auth and deployment conventions

---

## Future Features

These are not part of the current MVP but are worth designing deliberately rather than bolting on later.

### Vector Database / RAG Schema Retrieval

**Problem it solves:** When a schema has many tables (20+), including the full schema in every prompt becomes expensive and hurts accuracy — the model pays attention to irrelevant tables and misses nuances in the relevant ones. RAG retrieval selects only the tables and columns that are semantically relevant to the user's question before building the prompt.

**How it works:**
1. At startup, embed each table/column description into a vector (using an embedding model)
2. At query time, embed the user's natural language question
3. Retrieve the top-k most semantically similar schema items using cosine similarity
4. Build the system prompt using only those items rather than the full schema

**Integration point:** A new `ISchemaRelevanceFilter` interface that wraps `ISchemaProvider` — the outer layer calls `GetSchemaAsync()` as normal and then filters the resulting `SchemaCatalog` down to the relevant subset before it reaches the prompt builder. No changes to `TextToSqlEngine` or `ISqlGenerator` required.

**Cloud backend options (user provides credentials):**

| Option | Notes |
|---|---|
| **pgvector** (Postgres extension) | Strongest fit — no new service needed if the user is already on Postgres. Enable the `pgvector` extension, store embeddings in a table, retrieve with `ORDER BY embedding <=> $query_embedding LIMIT k`. |
| **Pinecone** | Managed cloud vector DB with a solid .NET SDK. Good for teams who want a dedicated vector service. Requires `PINECONE_API_KEY` and index name. |
| **Azure AI Search** | Enterprise option with integrated vector + keyword hybrid search. Requires endpoint + API key. |
| **Weaviate Cloud** | Open-source core with a managed cloud tier. Requires endpoint + API key. |

**Proposed credential pattern:**
```csharp
// pgvector — reuses the existing Postgres connection string
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .UseVectorSchemaFilter(opts =>
    {
        opts.Provider          = VectorProvider.PgVector;
        opts.EmbeddingApiKey   = "...";  // e.g. VoyageAI or OpenAI for embeddings
        opts.TopK              = 10;     // tables/columns to include
        opts.SimilarityThreshold = 0.3;
    });

// Pinecone
services.AddSqlChat()
    .UsePostgresConnectionString("...")
    .UseAnthropic(opts => opts.AnthropicApiKey = "...")
    .UseVectorSchemaFilter(opts =>
    {
        opts.Provider        = VectorProvider.Pinecone;
        opts.PineconeApiKey  = "...";
        opts.PineconeIndex   = "sqlchat-schema";
        opts.EmbeddingApiKey = "...";
        opts.TopK            = 10;
    });
```

**When to use it:** Once the schema exceeds ~15-20 tables, or when prompt costs become a concern. Below that threshold the full-schema approach is simpler and more predictable.

---

### Chain-of-Thought Prompting

**Problem it solves:** For complex queries involving multiple joins, subqueries, or window functions, the model produces better SQL when it reasons through the approach before writing code — similar to how a developer would sketch out the logic first.

**How it works:** An option in `SqlChatOptions` that appends a reasoning instruction to the system prompt, asking the model to think through the join strategy, filter conditions, and aggregation before emitting SQL. The response is parsed to extract just the SQL portion.

**Proposed API:**
```csharp
services.AddSqlChat()
    .UseAnthropic(opts =>
    {
        opts.AnthropicApiKey      = "...";
        opts.EnableChainOfThought = true;  // default: false
    });
```

**Trade-off:** More tokens and slightly higher latency per query, but measurably better accuracy on multi-table or analytically complex questions. Straightforward to implement as a prompt engineering change in `SchemaPromptBuilder` — no architectural changes needed.

---

### Schema Data Samples

**Problem it solves:** Columns with non-obvious values (coded statuses, internal IDs, date formats) are hard for the LLM to filter or join on correctly without seeing real examples. Including a few representative rows per table in the prompt closes that gap.

**How it works:** An optional enrichment added at schema introspection time — `PostgresSchemaProvider` queries `SELECT * FROM table LIMIT n` for each table and attaches the results to `TableDescriptor`. The prompt builder renders them as a compact grid.

**Proposed API:**
```csharp
services.AddSqlChat()
    .UsePostgresConnectionString("...", includeSampleRows: 3);
```

**Trade-off:** Larger prompts (proportional to number of tables × sample rows × columns) and a small extra DB round-trip at startup. High value for schemas with coded or non-intuitive data.
