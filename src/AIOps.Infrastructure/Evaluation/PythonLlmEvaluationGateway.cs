using System.Net.Http.Json;
using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using Microsoft.Extensions.Logging;

namespace AIOps.Infrastructure.Evaluation;

public sealed class PythonLlmEvaluationGateway(
    HttpClient httpClient,
    ILogger<PythonLlmEvaluationGateway> logger) : ILlmEvaluationGateway
{
    private const string EvaluationPath =
        "api/v1/evaluation/llm";

    public async Task<LlmEvaluationResponse> EvaluateAsync(
        LlmEvaluationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response =
                await httpClient.PostAsJsonAsync(
                    EvaluationPath,
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Python LLM evaluation request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);

                throw new HttpRequestException(
                    $"Python LLM evaluation request failed with HTTP {(int)response.StatusCode}.");
            }

            var result =
                await response.Content.ReadFromJsonAsync<LlmEvaluationResponse>(
                    cancellationToken: cancellationToken);

            if (result is null)
            {
                throw new InvalidOperationException(
                    "Python LLM evaluation returned an empty response.");
            }

            return result;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(
                "Python LLM evaluation returned an invalid response payload of type {ErrorType}.",
                ex.GetType().Name);

            throw new InvalidOperationException(
                "Python LLM evaluation returned an invalid response.",
                ex);
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Python LLM evaluation gateway request failed with error type {ErrorType}.",
                ex.GetType().Name);

            throw new InvalidOperationException(
                "Python LLM evaluation gateway request failed.",
                ex);
        }
    }
}