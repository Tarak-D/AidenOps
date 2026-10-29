using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Knowledge;

namespace AIOps.Orchestration.Evaluation;

public static class Phase15KnowledgeCorpus
{
    public const string CorpusId = "phase-15-knowledge";
    public const string Version = "1.0.0";
    public const string Source = "phase-15-evaluation-corpus";

    private static readonly Guid NetworkDocumentId =
        Guid.Parse("15000000-0000-4000-8000-000000000001");
    private static readonly Guid IdentityDocumentId =
        Guid.Parse("15000000-0000-4000-8000-000000000002");
    private static readonly Guid DatabaseDocumentId =
        Guid.Parse("15000000-0000-4000-8000-000000000003");

    public static EvaluationKnowledgeCorpus Create()
    {
        return new EvaluationKnowledgeCorpus(
            CorpusId,
            Version,
            [
                CreateNetworkDocument(),
                CreateIdentityDocument(),
                CreateDatabaseDocument()
            ]);
    }

    public static IReadOnlyList<RetrievalEvaluationCase> CreateRetrievalCases()
    {
        return
        [
            new(
                "network-connectivity-triage",
                "How should a network connectivity incident be triaged?",
                [NetworkDocumentId]),
            new(
                "identity-access-triage",
                "What checks are appropriate for an account access incident?",
                [IdentityDocumentId]),
            new(
                "database-availability-triage",
                "What should be checked when a database is unavailable?",
                [DatabaseDocumentId])
        ];
    }

    private static EvaluationKnowledgeCorpusDocument CreateNetworkDocument()
    {
        const string firstChunk =
            "Network connectivity triage begins by confirming the affected users, locations, network segments, and start time. Compare wired, wireless, VPN, and application reachability to determine the scope before changing configuration.";
        const string secondChunk =
            "Use approved monitoring and diagnostic tools to check recent changes, link and tunnel status, DNS resolution, and relevant service health. Record evidence and escalate to the network owner when impact is broad or a change is required.";

        return CreateDocument(
            NetworkDocumentId,
            "Network connectivity incident triage",
            firstChunk,
            secondChunk,
            Guid.Parse("15000000-0000-4000-8000-000000001001"),
            Guid.Parse("15000000-0000-4000-8000-000000001002"));
    }

    private static EvaluationKnowledgeCorpusDocument CreateIdentityDocument()
    {
        const string firstChunk =
            "Identity and account-access triage should establish the affected account, reported symptom, scope, and time of impact. Verify the requester using the approved identity process; never request or record passwords or authentication secrets.";
        const string secondChunk =
            "Use approved identity tooling to inspect sign-in status and relevant audit events. Password resets, MFA changes, session revocation, and account unlocks must follow the organization's authorization and approval procedures.";

        return CreateDocument(
            IdentityDocumentId,
            "Identity and account-access incident triage",
            firstChunk,
            secondChunk,
            Guid.Parse("15000000-0000-4000-8000-000000002001"),
            Guid.Parse("15000000-0000-4000-8000-000000002002"));
    }

    private static EvaluationKnowledgeCorpusDocument CreateDatabaseDocument()
    {
        const string firstChunk =
            "Database availability triage should establish the affected service, environment, users, and start time. Distinguish connection failures from application errors and confirm whether the impact is isolated or shared across dependent services.";
        const string secondChunk =
            "Check approved health signals, dependency status, recent deployments or configuration changes, and database service alerts. Preserve logs, avoid direct data changes or destructive recovery actions, and escalate to the database owner under the incident procedure.";

        return CreateDocument(
            DatabaseDocumentId,
            "Database availability incident triage",
            firstChunk,
            secondChunk,
            Guid.Parse("15000000-0000-4000-8000-000000003001"),
            Guid.Parse("15000000-0000-4000-8000-000000003002"));
    }

    private static EvaluationKnowledgeCorpusDocument CreateDocument(
        Guid documentId,
        string title,
        string firstChunk,
        string secondChunk,
        Guid firstChunkId,
        Guid secondChunkId)
    {
        var document = new KnowledgeDocument(
            documentId,
            Source,
            title,
            $"{firstChunk}\n\n{secondChunk}");

        return new EvaluationKnowledgeCorpusDocument(
            document,
            [
                new KnowledgeChunk(
                    firstChunkId,
                    documentId,
                    0,
                    firstChunk),
                new KnowledgeChunk(
                    secondChunkId,
                    documentId,
                    1,
                    secondChunk)
            ]);
    }
}
