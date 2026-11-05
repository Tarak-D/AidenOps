using AIOps.Abstractions;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Knowledge;
using AIOps.Abstractions.Persistence;
using AIOps.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;

namespace AIOps.Api.Tests;

public sealed class ApiTestWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting(
            "AIOps:Testing:UseInMemoryOrleansStorage",
            "true");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgresConnection"] = string.Empty,
                    ["AIOps:Testing:UseInMemoryOrleansStorage"] = "true"
                }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEvaluationStore>();
            services.AddSingleton<IEvaluationStore, InMemoryEvaluationStore>();
            services.RemoveAll<IEvaluationAcceptanceStore>();
            services.AddSingleton<
                IEvaluationAcceptanceStore,
                InMemoryEvaluationAcceptanceStore>();
            services.RemoveAll<IEvaluationDatasetStore>();
            services.AddSingleton<
                IEvaluationDatasetStore,
                InMemoryEvaluationDatasetStore>();
            services.RemoveAll<IItsmProviderSelectionStore>();
            services.AddSingleton<
                IItsmProviderSelectionStore,
                InMemoryItsmProviderSelectionStore>();
            services.RemoveAll<INetworkDiagnosticProviderSelectionStore>();
            services.AddSingleton<
                INetworkDiagnosticProviderSelectionStore,
                InMemoryNetworkDiagnosticProviderSelectionStore>();
            services.AddSingleton<IKnowledgeStore, InMemoryKnowledgeStore>();

            var actionApprovalStore = new InMemoryActionApprovalStore();
            services.RemoveAll<IActionExecutionStore>();
            services.AddSingleton<IActionExecutionStore>(actionApprovalStore);
            services.RemoveAll<IApprovalStore>();
            services.AddSingleton<IApprovalStore>(actionApprovalStore);
            services.RemoveAll<IApprovalValidator>();
            services.AddSingleton<IApprovalValidator>(actionApprovalStore);
        });
    }

    private sealed class InMemoryEvaluationStore : IEvaluationStore
    {
        private readonly ConcurrentQueue<ExperimentRunRecord> _runs = new();

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            _runs.Enqueue(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                _runs.Where(run =>
                        (experimentId is null ||
                         run.ExperimentId == experimentId) &&
                        (datasetName is null ||
                         run.DatasetName == datasetName))
                    .ToArray());
    }

    private sealed class InMemoryEvaluationAcceptanceStore
        : IEvaluationAcceptanceStore
    {
        private readonly ConcurrentDictionary<Guid, EvaluationAcceptanceRecord>
            _records = new();

        public Task RecordAsync(
            EvaluationAcceptanceRecord result,
            CancellationToken cancellationToken = default)
        {
            _records[result.Id] = result;
            return Task.CompletedTask;
        }

        public Task<EvaluationAcceptanceRecord?> GetAsync(
            Guid resultId,
            CancellationToken cancellationToken = default)
        {
            _records.TryGetValue(resultId, out var record);
            return Task.FromResult(record);
        }
    }

    private sealed class InMemoryEvaluationDatasetStore
        : IEvaluationDatasetStore
    {
        private readonly ConcurrentDictionary<
            (string DatasetId, string Version),
            EvaluationDatasetRecord> _datasets = new();

        public Task SaveAsync(
            EvaluationDatasetMetadata metadata,
            EvaluationDataset dataset,
            CancellationToken cancellationToken = default)
        {
            _datasets[(metadata.DatasetId, metadata.Version)] =
                new EvaluationDatasetRecord(metadata, dataset);
            return Task.CompletedTask;
        }

        public Task<EvaluationDatasetRecord?> GetAsync(
            string datasetId,
            string version,
            CancellationToken cancellationToken = default)
        {
            _datasets.TryGetValue((datasetId, version), out var record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<EvaluationDatasetRecord>> ListAsync(
            string? datasetId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvaluationDatasetRecord>>(
                _datasets
                    .Where(entry =>
                        datasetId is null ||
                        entry.Key.DatasetId == datasetId)
                    .Select(entry => entry.Value)
                    .ToArray());
    }

    private sealed class InMemoryItsmProviderSelectionStore
        : IItsmProviderSelectionStore
    {
        private string? _provider;

        public Task<string?> GetProviderAsync(CancellationToken ct = default) =>
            Task.FromResult(_provider);

        public Task SetProviderAsync(
            string provider,
            string changedBy,
            CancellationToken ct = default)
        {
            _provider = provider;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryNetworkDiagnosticProviderSelectionStore
        : INetworkDiagnosticProviderSelectionStore
    {
        private NetworkDiagnosticProviderSelection? _selection;

        public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(
            CancellationToken ct = default) =>
            Task.FromResult(_selection);

        public Task SetSelectionAsync(
            NetworkDiagnosticProviderSelection selection,
            string changedBy,
            CancellationToken ct = default)
        {
            _selection = selection;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryKnowledgeStore : IKnowledgeStore
    {
        public Task SaveDocumentAsync(
            KnowledgeDocument document,
            IReadOnlyList<KnowledgeChunk> chunks,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
            string query,
            int limit = 5,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<KnowledgeSearchResult>>([]);
    }

    private sealed class InMemoryActionApprovalStore :
        IActionExecutionStore,
        IApprovalStore,
        IApprovalValidator
    {
        public Task AddAsync(
            ActionExecution actionExecution,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        Task<ActionExecution?> IActionExecutionStore.GetAsync(
            Guid actionExecutionId,
            CancellationToken ct) =>
            Task.FromResult<ActionExecution?>(null);

        public Task UpdateAsync(
            ActionExecution actionExecution,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<bool> TryBeginExecutionAsync(
            ActionExecution actionExecution,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task AddAsync(
            ApprovalRequest approval,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        Task<ApprovalRequest?> IApprovalStore.GetAsync(
            Guid approvalId,
            CancellationToken ct) =>
            Task.FromResult<ApprovalRequest?>(null);

        public Task<ApprovalRequest?> GetForActionExecutionAsync(
            Guid actionExecutionId,
            CancellationToken ct = default) =>
            Task.FromResult<ApprovalRequest?>(null);

        public Task UpdateAsync(
            ApprovalRequest approval,
            CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>([]);

        public Task<ApprovalRequest?> GetApprovalForActionExecutionAsync(
            Guid actionExecutionId,
            CancellationToken ct = default) =>
            Task.FromResult<ApprovalRequest?>(null);
    }
}
