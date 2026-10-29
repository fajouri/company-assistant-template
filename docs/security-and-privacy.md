# Security and privacy
## Current state
Local fictional shell only. No authentication, authorization, audit, retrieval or model integration exists yet.
Compose binds the UI to loopback. PostgreSQL has no host port. Its bootstrap account is a development superuser; replace it with least-privilege runtime, migration and audit roles before production.
Do not expose this delivery publicly or ingest real documents.
## Required before pilot
Entra tenant and audience validation; assigned app access; authorization per resource; CSRF; secure session lifecycle.
Approved document lifecycle and ACL filtering before retrieval, including source links, caches and conversation history.
MCP authentication, allowlisted tools, input/output bounds and timeouts.
Regional/EU processing decision covering inference, embeddings, telemetry and backups; review provider processing terms.
Data inventory, purpose/legal basis, retention, rights workflows, incident plan and DPIA screening with the responsible organization.
No raw conversation logging by default. Separate audit permissions and pseudonymous actor references.
Encrypted host storage/backups, tested restore and deletion reapplication.
Reviewed immutable image digests, dependency lockfiles, SBOM, scanning and signed releases before production.
## Threats to test
Cross-user access, privilege escalation, prompt injection, poisoned policies, source-link leaks, unsafe markdown, SSRF, secret leakage and telemetry disclosure.
GDPR compliance cannot be established by this codebase alone.
