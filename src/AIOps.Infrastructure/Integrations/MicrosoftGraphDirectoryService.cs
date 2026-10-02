using Azure.Identity;
using AIOps.Abstractions.Diagnostics;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace AIOps.Infrastructure.Integrations;

/// <summary>Microsoft Graph implementation of the existing directory seam.</summary>
public sealed class MicrosoftGraphDirectoryService : AIOps.Abstractions.Integrations.IDirectoryService
{
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
    private readonly GraphServiceClient _graph;

    public MicrosoftGraphDirectoryService(GraphServiceClient graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    public Task<string> ResetPasswordAsync(
        string userPrincipalName,
        CancellationToken ct = default) =>
        AIOpsDiagnostics.TrackAsync(
            "directory", "MicrosoftGraph", "reset_password",
            () => ResetPasswordCoreAsync(userPrincipalName, ct),
            ct);

    private Task<string> ResetPasswordCoreAsync(
        string userPrincipalName,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        ct.ThrowIfCancellationRequested();

        throw new DirectoryProviderException(
            "DirectoryPasswordResetNotConfigured",
            "Production password reset is unsupported until a secure temporary-password delivery flow is configured.");
    }

    public Task<string> GrantGroupAccessAsync(
        string userPrincipalName,
        string groupId,
        CancellationToken ct = default) =>
        AIOpsDiagnostics.TrackAsync(
            "directory", "MicrosoftGraph", "grant_group_access",
            () => GrantGroupAccessCoreAsync(userPrincipalName, groupId, ct),
            ct);

    private async Task<string> GrantGroupAccessCoreAsync(
        string userPrincipalName,
        string groupId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrincipalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);

        try
        {
            User? user;
            try
            {
                user = await _graph.Users[userPrincipalName]
                    .GetAsync(request => request.QueryParameters.Select = ["id"], ct);
            }
            catch (ODataError error) when (error.ResponseStatusCode == 404)
            {
                throw Failure(
                    "DirectoryUserNotFound",
                    "The requested directory user was not found.");
            }

            if (string.IsNullOrWhiteSpace(user?.Id))
            {
                throw Failure(
                    "DirectoryUserNotFound",
                    "The requested directory user was not found.");
            }

            Group? group;
            try
            {
                group = await _graph.Groups[groupId]
                    .GetAsync(request => request.QueryParameters.Select = ["id", "isAssignableToRole"], ct);
            }
            catch (ODataError error) when (error.ResponseStatusCode == 404)
            {
                throw Failure(
                    "DirectoryGroupNotFound",
                    "The requested directory group was not found.");
            }

            if (group?.IsAssignableToRole is null)
            {
                throw Failure(
                    "DirectoryGroupMetadataUnavailable",
                    "The target directory group's role assignment status could not be verified.");
            }

            if (group.IsAssignableToRole.Value)
            {
                throw Failure(
                    "DirectoryRoleAssignableGroupNotSupported",
                    "Role-assignable groups are not supported by this directory operation.");
            }

            await _graph.Groups[groupId].Members.Ref.PostAsync(
                new ReferenceCreate
                {
                    OdataId = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(user.Id)}"
                },
                requestConfiguration: null,
                ct);

            return "{\"status\":\"access-granted\",\"provider\":\"microsoft-graph\"}";
        }
        catch (DirectoryProviderException)
        {
            throw;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (ODataError error)
        {
            throw Translate(error);
        }
        catch (AuthenticationFailedException)
        {
            throw Failure(
                "DirectoryAuthenticationFailed",
                "Microsoft Graph authentication failed.");
        }
        catch (OperationCanceledException)
        {
            throw Failure(
                "DirectoryTimeout",
                "The Microsoft Graph directory request timed out.");
        }
        catch (HttpRequestException)
        {
            throw Failure(
                "DirectoryOperationFailed",
                "The Microsoft Graph directory operation failed.");
        }
        catch (Exception)
        {
            // Never forward SDK exception messages, response bodies, or credential details.
            throw Failure(
                "DirectoryOperationFailed",
                "The Microsoft Graph directory operation failed.");
        }
    }

    private static DirectoryProviderException Translate(ODataError error)
    {
        var status = error.ResponseStatusCode;
        if (status == 401)
        {
            return Failure(
                "DirectoryAuthenticationFailed",
                "Microsoft Graph authentication failed.");
        }

        if (status == 403)
        {
            return Failure(
                "DirectoryPermissionDenied",
                "The Microsoft Graph application lacks permission for this operation.");
        }

        if (status == 429)
        {
            return Failure(
                "DirectoryThrottled",
                "Microsoft Graph is throttling directory requests.");
        }

        if (status == 400 &&
            error.Error?.Message?.Contains(
                "already exist",
                StringComparison.OrdinalIgnoreCase) == true)
        {
            return Failure(
                "DirectoryAlreadyMember",
                "The user is already a member of the requested group.");
        }

        return Failure(
            "DirectoryOperationFailed",
            "The Microsoft Graph directory operation failed.");
    }

    private static DirectoryProviderException Failure(
        string code,
        string message) => new(code, message);
}
