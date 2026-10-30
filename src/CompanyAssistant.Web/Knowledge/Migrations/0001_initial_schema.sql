-- 0001_initial_schema.sql
-- Non-destructive knowledge schema. Requires the pgvector extension (deploy/init.sql).

DO $roles$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'assistant_ingest') THEN
        EXECUTE 'CREATE ROLE assistant_ingest NOLOGIN';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'assistant_runtime') THEN
        EXECUTE 'CREATE ROLE assistant_runtime NOLOGIN';
    END IF;
END
$roles$;

CREATE TABLE IF NOT EXISTS documents (
    document_id     text PRIMARY KEY,
    title           text NOT NULL,
    owner           text NOT NULL,
    approver        text NULL,
    version         text NOT NULL,
    effective_date  date NOT NULL,
    review_date     date NULL,
    approval_state  text NOT NULL,
    access_policy   text NOT NULL,
    source_path     text NOT NULL,
    content         text NOT NULL,
    content_sha256  text NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    updated_at      timestamptz NOT NULL DEFAULT now(),
    deleted_at      timestamptz NULL,
    CONSTRAINT documents_approval_state_check CHECK (approval_state IN ('draft', 'approved', 'revoked')),
    CONSTRAINT documents_access_policy_check CHECK (access_policy IN ('employee', 'hr'))
);

CREATE TABLE IF NOT EXISTS document_versions (
    version_id      bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id     text NOT NULL REFERENCES documents (document_id) ON DELETE CASCADE,
    version         text NOT NULL,
    approval_state  text NOT NULL,
    effective_date  date NOT NULL,
    content_sha256  text NOT NULL,
    ingested_at     timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS document_chunks (
    chunk_id        bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    document_id     text NOT NULL REFERENCES documents (document_id) ON DELETE CASCADE,
    version_id      bigint NOT NULL REFERENCES document_versions (version_id) ON DELETE CASCADE,
    chunk_index     integer NOT NULL,
    section_heading text NULL,
    content         text NOT NULL,
    content_sha256  text NOT NULL,
    embedding       vector(1536) NOT NULL,
    CONSTRAINT document_chunks_document_chunk_unique UNIQUE (document_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS document_chunks_document_id_idx ON document_chunks (document_id);

GRANT USAGE ON SCHEMA public TO assistant_ingest, assistant_runtime;
GRANT SELECT, INSERT, UPDATE, DELETE ON documents, document_versions, document_chunks TO assistant_ingest;
GRANT USAGE, SELECT ON SEQUENCE document_versions_version_id_seq, document_chunks_chunk_id_seq TO assistant_ingest;
GRANT SELECT ON documents, document_versions, document_chunks TO assistant_runtime;
