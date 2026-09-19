CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS knowledge_documents
(
    id uuid NOT NULL,
    source varchar(500) NOT NULL,
    title varchar(500) NOT NULL,
    content text NOT NULL,
    metadata_json text NULL,
    created_at timestamp with time zone NOT NULL,

    CONSTRAINT knowledge_documents_pkey PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_knowledge_documents_source
    ON knowledge_documents (source);

CREATE INDEX IF NOT EXISTS ix_knowledge_documents_title
    ON knowledge_documents (title);


CREATE TABLE IF NOT EXISTS knowledge_chunks
(
    id uuid NOT NULL,
    document_id uuid NOT NULL,
    chunk_index integer NOT NULL,
    content text NOT NULL,
    metadata_json text NULL,
    embedding vector(64) NOT NULL,
    created_at timestamp with time zone NOT NULL,

    CONSTRAINT knowledge_chunks_pkey PRIMARY KEY (id),

    CONSTRAINT fk_knowledge_chunks_document
        FOREIGN KEY (document_id)
        REFERENCES knowledge_documents (id)
        ON DELETE CASCADE,

    CONSTRAINT uq_knowledge_chunks_document_chunk
        UNIQUE (document_id, chunk_index)
);

CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_document_id
    ON knowledge_chunks (document_id);

CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_embedding
    ON knowledge_chunks
    USING hnsw (embedding vector_cosine_ops);