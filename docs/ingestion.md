# Knowledge ingestion and retrieval

Policy knowledge is stored in PostgreSQL/pgvector. Ingestion is a separate command, never part of chat. Retrieval filters access policies in the database before vector ranking and limit.

## Fixtures

`fixtures/policies/` contains five fictional policies (annual leave, benefits, remote work, expenses, and HR contacts). HR contacts use `access_policy: hr` and are visible only to holders of the `hr.data.read` permission; the others use `access_policy: employee`. All content is fictional.

Each Markdown file has a strict YAML front-matter block:

```markdown
---
id: leave-policy
title: Annual leave policy
owner: People Operations
approver: HR Director
version: "1.0"
effective_date: 2025-01-01
review_date: 2026-06-30
approval_state: approved   # draft | approved | revoked
access_policy: employee    # employee | hr
---

# Annual leave
```

`id`, `title`, `owner`, `version`, `effective_date`, `approval_state` and `access_policy` are required; `approver` and `review_date` are optional. Unknown keys are rejected. Only `approved` documents whose `effective_date` is not in the future are searchable.

## Run migrations and ingest

```bash
docker compose up --build -d --wait
docker compose exec -T web dotnet CompanyAssistant.Web.dll migrate
docker compose exec -T web dotnet CompanyAssistant.Web.dll ingest
```

`migrate` applies versioned, non-destructive SQL migrations (embedded in the assembly) inside a transaction and records them in `__schema_migrations`. It refuses any migration containing `DROP`, `TRUNCATE` or `DELETE FROM`, and self-checks the Npgsql/pgvector vector mapping. `ingest` reads only the configured read-only folder (`Ingestion:SourcePath`, mounted from `fixtures/policies` in Compose), parses Markdown, rejects path traversal and symlink escapes, bounds file and chunk sizes, and indexes incrementally. Re-running it is idempotent.

## Embeddings

`ingest` and `/api/search` require a configurable Foundry/Azure-OpenAI-compatible embeddings deployment:

- `Embeddings__Endpoint` (e.g. `https://your-resource.openai.azure.com`)
- `Embeddings__ApiKey`
- `Embeddings__Deployment` (e.g. `text-embedding-3-small`)
- `Embeddings__ApiVersion` (default `2024-02-01`)
- `Embeddings__Dimensions` (default `1536`, must match the schema)

The current schema stores `vector(1536)`; changing the dimension requires a new versioned migration. When embeddings are not configured, `ingest` fails closed and `/api/search` returns 503. No mock is ever enabled in production.

Compose forwards `EMBEDDINGS__ENDPOINT`, `EMBEDDINGS__API_KEY` and `EMBEDDINGS__DEPLOYMENT` from `.env` to `Embeddings__Endpoint`, `Embeddings__ApiKey` and `Embeddings__Deployment`. They are optional: leave them unset to run the anonymous shell, or set all three to enable the real client; a partial configuration is rejected at startup. `Embeddings__ApiVersion` and `Embeddings__Dimensions` keep their defaults and can be overridden directly in the deployment environment when required.

## Retrieval API

| Endpoint | Access |
| --- | --- |
| `POST /api/search` `{ "query": "..." }` | Requires an assigned user with `assistant.use` or `hr.data.read`. Returns chunks matching the caller's allowed access policies. |
| `GET /api/documents/{id}` | Same permission filtering as search. Returns metadata, a safe relative `sourcePath` and stored content; never a raw filesystem path. |

Only `approved`, currently effective and permitted documents are returned. Unapproved, future-effective, deleted and unauthorized documents cannot leak through titles, fragments or citations.

## Version and audit policy

`document_versions` is an append-only audit log of every successful ingestion (version, approval state, effective date, content hash, timestamp). `document_chunks` holds only the current version's chunks and embeddings. Re-ingesting a changed document inserts a new audit row and revokes the previous chunks in the same transaction; deletion soft-deletes the document and revokes its chunks. No duplicate chunks are produced because unchanged documents are skipped.

## Database privileges

The migration creates two least-privilege NOLOGIN roles: `assistant_ingest` (write) and `assistant_runtime` (read-only). Ingestion and retrieval stores run `SET LOCAL ROLE` inside each transaction so retrieval can never write, even when the underlying connection is privileged. Production should connect with dedicated least-privilege login roles and run migrations with a dedicated migrator role.
