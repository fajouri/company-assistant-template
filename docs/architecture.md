# Architecture
A modular ASP.NET Core application serves compiled React assets and APIs from one origin.
PostgreSQL/pgvector is provisioned but not connected to the application yet.
Later introduce Application and Infrastructure projects when actual use cases require those boundaries; do not add empty layers.
Ingestion runs as a separate command, not during chat. A separate private MCP service exposes narrow read-only tools.
Entra authentication terminates in the backend; secure cookies and CSRF protection accompany authenticated state-changing endpoints. No browser token storage.
Authorization precedes retrieval and tool execution; MCP independently validates delegated identity and permissions.
Only approved documents are indexed. Personal HR records remain outside general retrieval.
OpenTelemetry exports metadata without prompts or personal tool results by default.
