using AIOps.Abstractions.Knowledge;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class Phase15KnowledgeCorpusTests
{
    [Fact]
    public void Corpus_has_versioned_stable_documents_chunks_and_order()
    {
        var corpus = Phase15KnowledgeCorpus.Create();

        Assert.Equal(Phase15KnowledgeCorpus.CorpusId, corpus.CorpusId);
        Assert.Equal("1.0.0", corpus.Version);
        Assert.Equal(
            new[]
            {
                Guid.Parse("15000000-0000-4000-8000-000000000001"),
                Guid.Parse("15000000-0000-4000-8000-000000000002"),
                Guid.Parse("15000000-0000-4000-8000-000000000003")
            },
            corpus.Documents.Select(entry => entry.Document.Id));
        Assert.Equal(
            new[]
            {
                0, 1, 0, 1, 0, 1
            },
            corpus.Documents.SelectMany(entry => entry.Chunks)
                .Select(chunk => chunk.ChunkIndex));
        Assert.Equal(
            new[]
            {
                Guid.Parse("15000000-0000-4000-8000-000000001001"),
                Guid.Parse("15000000-0000-4000-8000-000000001002"),
                Guid.Parse("15000000-0000-4000-8000-000000002001"),
                Guid.Parse("15000000-0000-4000-8000-000000002002"),
                Guid.Parse("15000000-0000-4000-8000-000000003001"),
                Guid.Parse("15000000-0000-4000-8000-000000003002")
            },
            corpus.Documents.SelectMany(entry => entry.Chunks)
                .Select(chunk => chunk.Id));

        corpus.Validate();
    }

    [Fact]
    public void Corpus_hash_is_deterministic_and_changes_with_content_or_order()
    {
        var corpus = Phase15KnowledgeCorpus.Create();
        var repeatedCorpus = Phase15KnowledgeCorpus.Create();

        var hash = corpus.ComputeSha256();

        Assert.Equal(hash, repeatedCorpus.ComputeSha256());
        Assert.Equal(64, hash.Length);
        Assert.All(hash, character =>
            Assert.True(Uri.IsHexDigit(character)));

        var firstEntry = corpus.Documents[0];
        var changedContent = firstEntry with
        {
            Document = firstEntry.Document with
            {
                Content = $"{firstEntry.Document.Content} Additional reviewed note."
            }
        };
        var changedCorpus = corpus with
        {
            Documents = [changedContent, .. corpus.Documents.Skip(1)]
        };
        var reorderedCorpus = corpus with
        {
            Documents = corpus.Documents.Reverse().ToArray()
        };

        Assert.NotEqual(hash, changedCorpus.ComputeSha256());
        Assert.NotEqual(hash, reorderedCorpus.ComputeSha256());
        Assert.NotEqual(
            hash,
            (corpus with { Version = "1.0.1" }).ComputeSha256());
    }

    [Fact]
    public void Corpus_validation_rejects_missing_metadata_and_empty_content()
    {
        var corpus = Phase15KnowledgeCorpus.Create();
        var firstEntry = corpus.Documents[0];

        Assert.Throws<ArgumentException>(() =>
            (corpus with { CorpusId = " " }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with { Version = "" }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with { Documents = [] }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry with
                    {
                        Document = firstEntry.Document with { Title = " " }
                    },
                    .. corpus.Documents.Skip(1)
                ]
            }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry with
                    {
                        Chunks =
                        [
                            firstEntry.Chunks[0] with { Content = "" },
                            firstEntry.Chunks[1]
                        ]
                    },
                    .. corpus.Documents.Skip(1)
                ]
            }).Validate());
    }

    [Fact]
    public void Corpus_validation_rejects_duplicate_ids_and_invalid_chunk_order()
    {
        var corpus = Phase15KnowledgeCorpus.Create();
        var firstEntry = corpus.Documents[0];

        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry,
                    corpus.Documents[1] with
                    {
                        Document = firstEntry.Document
                    },
                    corpus.Documents[2]
                ]
            }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry with
                    {
                        Chunks =
                        [
                            firstEntry.Chunks[0],
                            firstEntry.Chunks[1] with
                            {
                                Id = firstEntry.Chunks[0].Id
                            }
                        ]
                    },
                    .. corpus.Documents.Skip(1)
                ]
            }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry with
                    {
                        Chunks = firstEntry.Chunks.Reverse().ToArray()
                    },
                    .. corpus.Documents.Skip(1)
                ]
            }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (corpus with
            {
                Documents =
                [
                    firstEntry with
                    {
                        Chunks =
                        [
                            firstEntry.Chunks[0] with
                            {
                                DocumentId = Guid.NewGuid()
                            },
                            firstEntry.Chunks[1]
                        ]
                    },
                    .. corpus.Documents.Skip(1)
                ]
            }).Validate());
    }
}
