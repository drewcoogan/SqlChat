## File organization policy

This repo is organized as a small set of focused libraries. The goal is:

- Keep **`SqlChat.Domain`** free of infrastructure dependencies (EF, HTTP, LLM SDKs, etc.)
- Keep **`SqlChat.Client`** as the public “entry point” (builder/facade) for consumers
- Keep **`SqlChat.Service`** as orchestration/business logic (generation + validation)
- Keep **`SqlChat.Data`** as persistence/infrastructure adapters: schema introspection (`PostgresSchemaProvider`, `SqlServerSchemaProvider`, `EfCoreSchemaProvider`), query execution (`PostgresQueryExecutor`, `SqlServerQueryExecutor`), and schema enrichment (`EnrichingSchemaProvider`)

### Folder layout (per project)

Within each project, group files by “what it is”, not by feature name:

- **`Abstractions/`**: interfaces / contracts
- **`Models/`**: records and value objects
- **`Builders/`**: fluent builders
- **`Providers/`**: provider implementations (EF, manual, etc.)
- **`Security/`**: SQL validation / safety rules
- **`Generation/`**: NL→SQL generation pieces

### Namespace rule

- Namespace should mirror the folder, e.g. `SqlChat.Service.Security`, `SqlChat.Domain.Models`.
- Public entry points should remain short and stable:
  - `SqlChat.Client.SqlChatClient`
  - `SqlChat.Client.SqlChatBuilder`

