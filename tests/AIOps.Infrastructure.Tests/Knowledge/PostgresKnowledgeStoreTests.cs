using AIOps.Abstractions.Knowledge;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Knowledge;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Tests.Knowledge;

public sealed class PostgresKnowledgeStoreTests
{
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=aios;Username=aios;Password=aiospw";

    [Fact]
    public async Task Save_and_search_returns_matching_knowledge_chunk()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(
                ConnectionString,
                npgsqlOptions => npgsqlOptions.UseVector())
            .Options;

        var documentId = Guid.NewGuid();
        var chunkId = Guid.NewGuid();

        var document = new KnowledgeDocument(
            documentId,
            "phase7-test",
            "Phase 7 PostgreSQL Test",
            "This document verifies PostgreSQL pgvector knowledge retrieval.");

        var chunkContent =
            $"AidenOps Phase 7 integration test knowledge {Guid.NewGuid()}";

        var chunk = new KnowledgeChunk(
            chunkId,
            documentId,
            0,
            chunkContent,
            """{"test":"phase7"}""");

        await using var dbContext = new AIOpsDbContext(options);

        var embeddingGenerator = new DeterministicEmbeddingGenerator();

        var store = new PostgresKnowledgeStore(
            dbContext,
            embeddingGenerator);

        try
        {
            await store.SaveDocumentAsync(
                document,
                new[] { chunk });

            var results = await store.SearchAsync(
                chunkContent,
                limit: 5);

            var result = Assert.Single(
                results,
                x => x.ChunkId == chunkId);

            Assert.Equal(documentId, result.DocumentId);
            Assert.Equal("phase7-test", result.Source);
            Assert.Equal("Phase 7 PostgreSQL Test", result.Title);
            Assert.Equal(0, result.ChunkIndex);
            Assert.Equal(chunkContent, result.Content);
            Assert.Equal("""{"test":"phase7"}""", result.MetadataJson);

            Assert.InRange(
                result.Similarity,
                0.999,
                1.001);
        }
        finally
        {
            var savedDocument =
                await dbContext.KnowledgeDocuments
                    .SingleOrDefaultAsync(x => x.Id == documentId);

            if (savedDocument is not null)
            {
                dbContext.KnowledgeDocuments.Remove(savedDocument);
                await dbContext.SaveChangesAsync();
            }
        }
    }
}