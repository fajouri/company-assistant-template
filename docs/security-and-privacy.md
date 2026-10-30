# Security and privacy
## Current state
The fictional shell now has single-tenant Entra OIDC sign-in, secure server-side sessions, CSRF-protected logout, validated tenant/object identifiers, assigned-user checks and explicit role/permission policies. Permission-aware retrieval and approved-Markdown ingestion are implemented; chat, model calls and audit logging remain disabled. Real-tenant behavior requires the manual setup and verification in [identity](identity.md); deterministic tests do not use Entra credentials or metadata.
Compose binds the UI to loopback and uses temporary Data Protection keys in `/tmp`; its HTTP URL is not suitable for real sign-in. PostgreSQL has no host port. Its bootstrap account is a development superuser. Migrations create `assistant_ingest` (write) and `assistant_runtime` (read-only) NOLOGIN roles and the stores downgrade via `SET LOCAL ROLE`; production should still connect with dedicated least-privilege login roles and run migrations with a dedicated migrator role.
Do not expose this delivery publicly or load real company documents yet.
## Required before pilot
Complete real-tenant sign-in/logout and denied-access verification; production TLS, trusted-proxy and persistent protected Data Protection key configuration; authorization review for every future resource.
Approved document lifecycle and ACL filtering are now applied before retrieval on the search and source-document endpoints. Remaining before pilot: retrieval/ingestion audit logging, and applying the same ACL to any future source links, caches and conversation history.
MCP authentication, allowlisted tools, input/output bounds and timeouts.
Regional/EU processing decision covering inference, embeddings, telemetry and backups; review provider processing terms.
Data inventory, purpose/legal basis, retention, rights workflows, incident plan and DPIA screening with the responsible organization.
No raw conversation logging by default. Separate audit permissions and pseudonymous actor references.
Encrypted host storage/backups, tested restore and deletion reapplication.
Reviewed immutable image digests, dependency lockfiles, SBOM, scanning and signed releases before production.
## Threats to test
Cross-user access, privilege escalation, prompt injection, poisoned policies, source-link leaks, unsafe markdown, SSRF, secret leakage and telemetry disclosure.
GDPR compliance cannot be established by this codebase alone.
