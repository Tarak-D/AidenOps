using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using AIOps.Abstractions.Knowledge;

namespace AIOps.Orchestration.Evaluation;

public sealed record EvaluationKnowledgeCorpusDocument(
    KnowledgeDocument Document,
    IReadOnlyList<KnowledgeChunk> Chunks);

public sealed record EvaluationKnowledgeCorpus(
    string CorpusId,
    string Version,
    IReadOnlyList<EvaluationKnowledgeCorpusDocument> Documents)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CorpusId))
        {
            throw new ArgumentException(
                "Knowledge corpus id is required.",
                nameof(CorpusId));
        }

        if (string.IsNullOrWhiteSpace(Version))
        {
            throw new ArgumentException(
                "Knowledge corpus version is required.",
                nameof(Version));
        }

        ArgumentNullException.ThrowIfNull(Documents);

        if (Documents.Count == 0)
        {
            throw new ArgumentException(
                "Knowledge corpus must contain at least one document.",
                nameof(Documents));
        }

        var documentIds = new HashSet<Guid>();
        var chunkIds = new HashSet<Guid>();

        foreach (var corpusDocument in Documents)
        {
            if (corpusDocument is null)
            {
                throw new ArgumentException(
                    "Knowledge corpus documents cannot be null.",
                    nameof(Documents));
            }

            var document = corpusDocument.Document;

            if (document is null)
            {
                throw new ArgumentException(
                    "Every knowledge corpus entry must have a document.",
                    nameof(Documents));
            }

            if (document.Id == Guid.Empty)
            {
                throw new ArgumentException(
                    "Knowledge document id is required.",
                    nameof(Documents));
            }

            if (!documentIds.Add(document.Id))
            {
                throw new ArgumentException(
                    "Knowledge document ids must be unique.",
                    nameof(Documents));
            }

            if (string.IsNullOrWhiteSpace(document.Source) ||
                string.IsNullOrWhiteSpace(document.Title) ||
                string.IsNullOrWhiteSpace(document.Content))
            {
                throw new ArgumentException(
                    "Knowledge document source, title, and content are required.",
                    nameof(Documents));
            }

            if (corpusDocument.Chunks is null ||
                corpusDocument.Chunks.Count == 0)
            {
                throw new ArgumentException(
                    "Every knowledge document must contain at least one chunk.",
                    nameof(Documents));
            }

            for (var index = 0; index < corpusDocument.Chunks.Count; index++)
            {
                var chunk = corpusDocument.Chunks[index];

                if (chunk is null)
                {
                    throw new ArgumentException(
                        "Knowledge corpus chunks cannot be null.",
                        nameof(Documents));
                }

                if (chunk.Id == Guid.Empty)
                {
                    throw new ArgumentException(
                        "Knowledge chunk id is required.",
                        nameof(Documents));
                }

                if (!chunkIds.Add(chunk.Id))
                {
                    throw new ArgumentException(
                        "Knowledge chunk ids must be unique.",
                        nameof(Documents));
                }

                if (chunk.DocumentId != document.Id)
                {
                    throw new ArgumentException(
                        "Every knowledge chunk must reference its corpus document.",
                        nameof(Documents));
                }

                if (chunk.ChunkIndex != index)
                {
                    throw new ArgumentException(
                        "Knowledge chunks must be ordered with contiguous indexes starting at zero.",
                        nameof(Documents));
                }

                if (string.IsNullOrWhiteSpace(chunk.Content))
                {
                    throw new ArgumentException(
                        "Knowledge chunk content is required.",
                        nameof(Documents));
                }
            }
        }
    }

    public string ComputeSha256()
    {
        Validate();

        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", 1);
            writer.WriteString("corpusId", CorpusId);
            writer.WriteString("version", Version);
            writer.WriteStartArray("documents");

            foreach (var corpusDocument in Documents)
            {
                var document = corpusDocument.Document;

                writer.WriteStartObject();
                writer.WriteString("id", document.Id.ToString("D"));
                writer.WriteString("source", document.Source);
                writer.WriteString("title", document.Title);
                writer.WriteString("content", document.Content);
                writer.WriteString("metadataJson", document.MetadataJson);
                writer.WriteStartArray("chunks");

                foreach (var chunk in corpusDocument.Chunks)
                {
                    writer.WriteStartObject();
                    writer.WriteString("id", chunk.Id.ToString("D"));
                    writer.WriteString("documentId", chunk.DocumentId.ToString("D"));
                    writer.WriteNumber("chunkIndex", chunk.ChunkIndex);
                    writer.WriteString("content", chunk.Content);
                    writer.WriteString("metadataJson", chunk.MetadataJson);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        return Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan));
    }
}
