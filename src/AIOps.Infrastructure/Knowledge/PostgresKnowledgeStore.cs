using AIOps.Abstractions.Knowledge;
using AIOps.Domain.Entities;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.Knowledge;

public sealed class PostgresKnowledgeStore : IKnowledgeStore
{
    private readonly AIOpsDbContext _dbContext;
    private readonly IEmbeddingGenerator _embeddingGenerator;

    public PostgresKnowledgeStore(
        AIOpsDbContext dbContext,
        IEmbeddingGenerator embeddingGenerator)
    {
        _dbContext = dbContext;
        _embeddingGenerator = embeddingGenerator;
    }

    public async Task SaveDocumentAsync(
        KnowledgeDocument document,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        var documentEntity = new KnowledgeDocumentEntity
        {
            Id = document.Id,
            Source = document.Source,
            Title = document.Title,
            Content = document.Content,
            MetadataJson = document.MetadataJson,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var chunk in chunks)
        {
            if (chunk.DocumentId != document.Id)
            {
                throw new ArgumentException(
                    "Every knowledge chunk must reference the document being saved.",
                    nameof(chunks));
            }

            var embedding = await _embeddingGenerator.GenerateAsync(
                chunk.Content,
                cancellationToken);

            if (embedding.Count != _embeddingGenerator.Dimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding dimension mismatch. Expected {_embeddingGenerator.Dimensions}, got {embedding.Count}.");
            }

            documentEntity.Chunks.Add(new KnowledgeChunkEntity
            {
                Id = chunk.Id,
                DocumentId = chunk.DocumentId,
                ChunkIndex = chunk.ChunkIndex,
                Content = chunk.Content,
                MetadataJson = chunk.MetadataJson,
                Embedding = new Vector(embedding.ToArray()),
                CreatedAt = DateTime.UtcNow
            });
        }

        _dbContext.KnowledgeDocuments.Add(documentEntity);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<KnowledgeSearchResult>();
        }

        if (limit <= 0)
        {
            return Array.Empty<KnowledgeSearchResult>();
        }

        var embedding = await _embeddingGenerator.GenerateAsync(
            query,
            cancellationToken);

        if (embedding.Count != _embeddingGenerator.Dimensions)
        {
            throw new InvalidOperationException(
                $"Embedding dimension mismatch. Expected {_embeddingGenerator.Dimensions}, got {embedding.Count}.");
        }

        var queryVector = new Vector(embedding.ToArray());

        var results = await _dbContext.KnowledgeChunks
            .AsNoTracking()
            .Where(x => x.Embedding != null)
            .Select(x => new
            {
                ChunkId = x.Id,
                DocumentId = x.DocumentId,
                Source = x.Document.Source,
                Title = x.Document.Title,
                ChunkIndex = x.ChunkIndex,
                Content = x.Content,
                MetadataJson = x.MetadataJson,
                Distance = x.Embedding!.CosineDistance(queryVector)
            })
            .OrderBy(x => x.Distance)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return results
            .Select(x => new KnowledgeSearchResult(
                x.ChunkId,
                x.DocumentId,
                x.Source,
                x.Title,
                x.ChunkIndex,
                x.Content,
                1.0 - x.Distance,
                x.MetadataJson))
            .ToArray();
    }
}