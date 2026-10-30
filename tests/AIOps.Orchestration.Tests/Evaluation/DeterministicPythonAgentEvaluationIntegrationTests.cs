using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.Agents;
using AIOps.Orchestration.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class DeterministicPythonAgentEvaluationIntegrationTests
{
    [Fact]
    public async Task EvaluationRunner_persists_service_failures_without_counting_them_as_success()
    {
        using var httpClient = new HttpClient(new ServiceUnavailableHandler())
        {
            BaseAddress = new Uri("http://agent.test/")
        };
        var gateway = new PythonAgentGateway(
            httpClient,
            NullLogger<PythonAgentGateway>.Instance);
        var store = new RecordingEvaluationStore();
        var dataset = CreateDataset();
        var runner = new EvaluationRunner(gateway, store);

        var summary = await runner.RunAsync(
            new EvaluationDataset(
                dataset.Name,
                dataset.Version,
                [dataset.Cases[0]]),
            "phase15-python-service-failure",
            "llm",
            "test-agent",
            "test-prompt");

        var result = Assert.Single(summary.Cases);
        Assert.Equal(AgentRunOutcome.Failed, result.FinalOutcome);
        Assert.False(result.DecisionCorrect);
        Assert.False(result.Resolved);
        Assert.Equal(0, summary.Metrics.DecisionAccuracy);
        Assert.Equal(0, summary.Metrics.ResolutionRate);
        Assert.Equal(1, Assert.Single(store.Runs).SampleCount);
    }

    [Fact]
    public async Task EvaluationRunner_runs_deterministic_python_agent_over_http_and_persists_results()
    {
        var repositoryRoot = FindRepositoryRoot();
        await using var knowledgeServer = new KnowledgeSearchServer();
        await using var agentService = await PythonAgentService.StartAsync(
            repositoryRoot,
            knowledgeServer.BaseAddress);

        using var httpClient = new HttpClient
        {
            BaseAddress = agentService.BaseAddress,
            Timeout = TimeSpan.FromSeconds(30)
        };
        var gateway = new PythonAgentGateway(
            httpClient,
            NullLogger<PythonAgentGateway>.Instance);
        var store = new RecordingEvaluationStore();
        var runner = new EvaluationRunner(gateway, store);
        var dataset = CreateDataset();
        var configuration = new EvaluationRunConfiguration(
            "phase15-deterministic-python-http",
            new EvaluationModelMetadata(
                "llm",
                "deterministic/bootstrap",
                "deterministic",
                "local-v1",
                "v0-deterministic"),
            dataset.Name,
            dataset.Version,
            MaxCases: 2,
            Deterministic: true,
            ExecuteExternalTools: false,
            Notes: "Deterministic Python agent HTTP integration test.");

        var summary = await runner.RunAsync(dataset, configuration);

        Assert.Equal(2, summary.Cases.Count);
        Assert.Equal(2, summary.Metrics.SampleCount);
        Assert.Equal(1, summary.Metrics.TriageAccuracy);
        Assert.Equal(1, summary.Metrics.DecisionAccuracy);
        Assert.Equal(1, summary.Metrics.ToolSelectionAccuracy);
        Assert.Equal(1, summary.Metrics.ToolArgumentValidity);
        Assert.Null(summary.Metrics.ExecutionSuccessRate);
        Assert.Equal(0.5, summary.Metrics.ResolutionRate);

        var resolvedCase = Assert.Single(
            summary.Cases,
            result => result.CaseId == "vpn-resolved");
        Assert.Equal(AgentRunOutcome.Resolved, resolvedCase.FinalOutcome);
        Assert.Equal(TicketDomain.Network, resolvedCase.PredictedDomain);
        Assert.Equal(Severity.P1, resolvedCase.PredictedSeverity);
        Assert.NotEmpty(resolvedCase.Trace);
        Assert.Contains(
            resolvedCase.Trace,
            trace => trace.Agent == "TriageAgent" &&
                trace.StepName == "triage" &&
                trace.Model == "deterministic/bootstrap");
        Assert.Null(resolvedCase.ExecutionSucceeded);

        var proposalCase = Assert.Single(
            summary.Cases,
            result => result.CaseId == "vpn-proposal");
        Assert.Equal(AgentRunOutcome.ProposalCreated, proposalCase.FinalOutcome);
        Assert.Equal(
            "Network.RunVpnDiagnostics",
            proposalCase.PredictedToolName);
        Assert.True(proposalCase.ToolArgumentsValid);
        Assert.Null(proposalCase.ExecutionSucceeded);

        var storedRun = Assert.Single(store.Runs);
        Assert.Equal(summary.Run.Id, storedRun.Id);
        Assert.Equal(configuration.ExperimentId, storedRun.ExperimentId);
        Assert.Equal(dataset.Name, storedRun.DatasetName);
        Assert.Equal(dataset.Version, storedRun.DatasetVersion);
        Assert.Equal(2, storedRun.SampleCount);
        Assert.Equal("llm", storedRun.ModelType);
        Assert.Equal("deterministic/bootstrap", storedRun.ModelName);
        Assert.Equal("v0-deterministic", storedRun.PromptVersion);

        using var configurationJson = JsonDocument.Parse(storedRun.ConfigJson);
        var persistedConfiguration = configurationJson.RootElement;
        Assert.Equal(dataset.Name, persistedConfiguration.GetProperty("Name").GetString());
        Assert.Equal(dataset.Version, persistedConfiguration.GetProperty("Version").GetString());
        Assert.Equal(2, persistedConfiguration.GetProperty("SampleCount").GetInt32());
        Assert.Equal(
            "deterministic-agent-gateway",
            persistedConfiguration.GetProperty("ExecutionMode").GetString());
        Assert.Equal(
            "deterministic",
            persistedConfiguration.GetProperty("Model").GetProperty("Provider").GetString());
        Assert.False(persistedConfiguration.GetProperty("ExecuteExternalTools").GetBoolean());

        using var metricsJson = JsonDocument.Parse(storedRun.MetricsJson);
        Assert.Equal(
            2,
            metricsJson.RootElement.GetProperty("SampleCount").GetInt32());
        Assert.Equal(
            summary.Metrics.DecisionAccuracy,
            metricsJson.RootElement.GetProperty("DecisionAccuracy").GetDouble());

        var listedRun = Assert.Single(
            await store.ListRunsAsync(configuration.ExperimentId, dataset.Name));
        Assert.Equal(storedRun.Id, listedRun.Id);
        Assert.Equal(2, knowledgeServer.RequestCount);
        Assert.Contains("VPN", knowledgeServer.LastQuery, StringComparison.OrdinalIgnoreCase);
    }

    private static EvaluationDataset CreateDataset()
    {
        var diagnosticTool = new ToolManifestEntry(
            "Network.RunVpnDiagnostics",
            "Runs read-only VPN diagnostics.",
            RiskLevel.Safe,
            false,
            """{"type":"object","properties":{"userOrDeviceId":{"type":"string"}},"required":["userOrDeviceId"],"additionalProperties":false}""");

        EvaluationCase CreateCase(
            string id,
            IReadOnlyList<ToolManifestEntry> allowedTools,
            string? expectedToolName,
            AgentRunOutcome expectedOutcome) =>
            new(
                id,
                new AgentTicketContext(
                    Guid.NewGuid(),
                    $"EVAL-{id}",
                    "VPN failure",
                    "The corporate VPN is down.",
                    "evaluation@example.com",
                    TicketDomain.Unknown,
                    Severity.P3,
                    TicketStatus.New),
                allowedTools,
                TicketDomain.Network,
                Severity.P1,
                expectedOutcome,
                expectedToolName,
                "The VPN workflow should follow its deterministic result.",
                ExpectedApprovalRequired: false);

        return new EvaluationDataset(
            "phase15-deterministic-python-agent",
            "1.0.0",
            [
                CreateCase(
                    "vpn-resolved",
                    Array.Empty<ToolManifestEntry>(),
                    null,
                    AgentRunOutcome.Resolved),
                CreateCase(
                    "vpn-proposal",
                    [diagnosticTool],
                    diagnosticTool.Name,
                    AgentRunOutcome.ProposalCreated)
            ]);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(
                    Path.Combine(directory.FullName, "python", "agent_service")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository Python agent service from the test output directory.");
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RecordingEvaluationStore : IEvaluationStore
    {
        public List<ExperimentRunRecord> Runs { get; } = [];

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<ExperimentRunRecord> result = Runs
                .Where(run =>
                    experimentId is null || run.ExperimentId == experimentId)
                .Where(run =>
                    datasetName is null || run.DatasetName == datasetName)
                .ToArray();
            return Task.FromResult(result);
        }
    }

    private sealed class ServiceUnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private sealed class KnowledgeSearchServer : IAsyncDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _serveTask;
        private int _requestCount;
        private string? _lastQuery;

        public KnowledgeSearchServer()
        {
            var port = GetAvailablePort();
            BaseAddress = new Uri($"http://127.0.0.1:{port}/");
            _listener.Prefixes.Add(BaseAddress.AbsoluteUri);
            _listener.Start();
            _serveTask = ServeAsync();
        }

        public Uri BaseAddress { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);
        public string? LastQuery => Volatile.Read(ref _lastQuery);

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Interlocked.Increment(ref _requestCount);
                Volatile.Write(
                    ref _lastQuery,
                    context.Request.QueryString["q"]);
                var responseBody = Encoding.UTF8.GetBytes("[]");
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = responseBody.Length;
                await context.Response.OutputStream.WriteAsync(responseBody);
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            await _serveTask;
        }
    }

    private sealed class PythonAgentService : IAsyncDisposable
    {
        private const int StartupTimeoutSeconds = 30;
        private readonly Process _process;
        private readonly StringBuilder _output = new();
        private readonly object _outputLock = new();

        private PythonAgentService(Process process, Uri baseAddress)
        {
            _process = process;
            BaseAddress = baseAddress;
            _process.OutputDataReceived += CaptureOutput;
            _process.ErrorDataReceived += CaptureOutput;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public Uri BaseAddress { get; }

        public static async Task<PythonAgentService> StartAsync(
            string repositoryRoot,
            Uri controlPlaneAddress)
        {
            var pythonRoot = Path.Combine(repositoryRoot, "python");
            var port = GetAvailablePort();
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = pythonRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("uvicorn");
            startInfo.ArgumentList.Add("agent_service.main:app");
            startInfo.ArgumentList.Add("--host");
            startInfo.ArgumentList.Add("127.0.0.1");
            startInfo.ArgumentList.Add("--port");
            startInfo.ArgumentList.Add(port.ToString());
            startInfo.ArgumentList.Add("--log-level");
            startInfo.ArgumentList.Add("warning");

            ConfigureChildEnvironment(startInfo);
            startInfo.Environment["AGENT_LLM_PROVIDER"] = "deterministic";
            startInfo.Environment["AIOPS_CONTROL_PLANE_URL"] =
                controlPlaneAddress.GetLeftPart(UriPartial.Authority);

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException(
                    "Could not start the deterministic Python agent service.");
            }

            var service = new PythonAgentService(
                process,
                new Uri($"http://127.0.0.1:{port}/"));
            try
            {
                await service.WaitUntilHealthyAsync();
                return service;
            }
            catch
            {
                await service.DisposeAsync();
                throw;
            }
        }

        private static void ConfigureChildEnvironment(
            ProcessStartInfo startInfo)
        {
            var allowedVariables = new[]
            {
                "PATH",
                "SYSTEMROOT",
                "WINDIR",
                "TEMP",
                "TMP",
                "APPDATA",
                "LOCALAPPDATA"
            };
            foreach (var name in startInfo.Environment.Keys.ToArray())
            {
                startInfo.Environment.Remove(name);
            }

            foreach (var name in allowedVariables)
            {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        private async Task WaitUntilHealthyAsync()
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(2)
            };
            var deadline = DateTime.UtcNow.AddSeconds(StartupTimeoutSeconds);
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    throw StartupFailure();
                }

                try
                {
                    using var response = await client.GetAsync("health");
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (HttpRequestException)
                {
                }
                catch (TaskCanceledException)
                {
                }

                await Task.Delay(TimeSpan.FromMilliseconds(200));
            }

            throw StartupFailure();
        }

        private InvalidOperationException StartupFailure()
        {
            string output;
            lock (_outputLock)
            {
                output = _output.ToString();
            }

            return new InvalidOperationException(
                $"Deterministic Python agent service did not become healthy. {output}");
        }

        private void CaptureOutput(object sender, DataReceivedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.Data))
            {
                return;
            }

            lock (_outputLock)
            {
                if (_output.Length > 4000)
                {
                    _output.Remove(0, _output.Length - 4000);
                }

                _output.AppendLine(args.Data);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                    using var timeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(10));
                    await _process.WaitForExitAsync(timeout.Token);
                }
            }
            finally
            {
                _process.Dispose();
            }
        }
    }
}
