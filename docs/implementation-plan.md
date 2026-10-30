# Implementation plan
1. Foundation (merged): Docker UI shell, branding, es/en, health endpoint and CI. Validate actual CI before merge. Lock dependencies and pin reviewed action/image digests in hardening work.
2. Identity (merged): Entra single-tenant, secure session, explicit permissions, denied-access tests. Production rejects simulated identity and missing/invalid identity configuration. Real-tenant verification remains an explicit manual setup step.
3. Knowledge and chat (merged): approved Markdown documents, incremental ingestion, pgvector, ACL-aware retrieval, source/version citations, deletion and evaluation, plus Semantic Kernel grounded streaming chat with server-verified citations, conversation ownership, rate limits, timeouts and CSRF-protected streaming. Real Foundry embeddings/chat and real-data evaluation remain to be verified (see [chat](chat.md)).
4. MCP: authenticated private service, own leave balance and HR contact; deny cross-user access.
5. Product: role-aware UI, controlled language/branding, accessible streaming, feedback and reproducible fictional demo.
6. Operations: metadata-only audit/traces, retention/deletion, backup/restore, security tests and production deployment guidance.
7. Sales: guided scenario, measured quality/latency/cost and one-page pilot scope.
Each delivery must compile, pass relevant tests, state limitations and update these documents.
