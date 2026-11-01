using System.Net.Http.Json;
using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using Microsoft.Extensions.Logging;

namespace AIOps.Infrastructure.Evaluation;

public sealed class PythonClassicalMlEvaluationGateway(
    HttpClient httpClient,
    ILogger<PythonClassicalMlEvaluationGateway> logger)
    : IClassicalMlEvaluationGateway
{
    private const string EvaluationPath =
        "api/v1/evaluation/classical-ml";

    public async Task<ClassicalMlEvaluationResponse> EvaluateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.GetAsync(
                EvaluationPath,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Python classical ML evaluation request failed with HTTP {StatusCode}.",
                    (int)response.StatusCode);

                throw new HttpRequestException(
                    $"Python classical ML evaluation request failed with HTTP {(int)response.StatusCode}.",
                    inner: null,
                    response.StatusCode);
            }

            var result =
                await response.Content.ReadFromJsonAsync<ClassicalMlEvaluationResponse>(
                    cancellationToken: cancellationToken);

            return result
                ?? throw new InvalidOperationException(
                    "Python classical ML evaluation returned an empty response.");
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
        catch (JsonException exception)
        {
            logger.LogWarning(
                "Python classical ML evaluation returned an invalid response payload of type {ErrorType}.",
                exception.GetType().Name);

            throw new InvalidOperationException(
                "Python classical ML evaluation returned an invalid response.",
                exception);
        }
        catch (Exception exception)
        {
            logger.LogError(
                "Python classical ML evaluation gateway request failed with error type {ErrorType}.",
                exception.GetType().Name);

            throw new InvalidOperationException(
                "Python classical ML evaluation gateway request failed.",
                exception);
        }
    }
}
