# Architecture
A modular ASP.NET Core application serves compiled React assets and APIs from one origin.
PostgreSQL/pgvector is provisioned but not connected to the application yet.
Later introduce Application and Infrastructure projects when actual use cases require those boundaries; do not add empty layers.
Ingestion runs as a separate command, not during chat. A separate private MCP service exposes narrow read-only tools.
Entra authentication terminates in the backend through Microsoft.Identity.Web 4.16.1 using the authorization-code flow with PKCE. OIDC tokens are not persisted into the cookie or returned to React; the cookie authentication ticket contains identity claims rather than raw OIDC tokens. The session cookie is HTTP-only, Secure and SameSite=Lax. Logout is a POST protected by double-submit antiforgery validation. See [identity setup](identity.md).
Authorization uses validated Entra `tid`/`oid` claims and explicit app-role-to-permission mappings. `Employee` grants `assistant.use`, `HR` grants `hr.data.read`, and `TechnicalAdmin` grants `technical.administration`; roles do not inherit from one another. The default authenticated policy requires a recognized assignment, and sensitive endpoints add named permission policies. Authorization precedes retrieval and tool execution; a future MCP service must independently validate delegated identity and permissions.
Only approved documents are indexed. Personal HR records remain outside general retrieval.
OpenTelemetry exports metadata without prompts or personal tool results by default.
