using System.Security.Claims;
using System.Text.Encodings.Web;
using AIOps.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AIOps.Host.Security;

public static class AuthPolicies
{
    public const string CanViewQueue = "CanViewQueue";
    public const string CanCreateTicket = "CanCreateTicket";
    public const string CanRetry = "CanRetry";
    public const string CanApprove = "CanApprove";
    public const string CanViewAudit = "CanViewAudit";
    public const string CanManageKnowledge = "CanManageKnowledge";
}

/// <summary>
/// DEV-ONLY authentication: select a seeded identity via the "X-Dev-User" header
/// (viewer | engineer | approver | admin), defaulting to viewer. Documented as a
/// development convenience; real OIDC (e.g. Microsoft Entra ID) is the documented
/// production extension point.
/// </summary>
public static class SecurityExtensions
{
    public static IServiceCollection AddAIOpsSecurity(this IServiceCollection services, IConfiguration config)
    {
        services.AddAuthentication(o =>
            {
                o.DefaultScheme = DevIdentityHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, DevIdentityHandler>(DevIdentityHandler.SchemeName, _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.CanViewQueue, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.CanCreateTicket, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.CanRetry, p => p.RequireRole(nameof(UserRole.Engineer), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanApprove, p => p.RequireRole(nameof(UserRole.Approver), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanViewAudit, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanManageKnowledge, p => p.RequireRole(nameof(UserRole.Engineer), nameof(UserRole.Admin)));

        return services;
    }

    public sealed class DevIdentityHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "DevIdentity";

        public DevIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var name = Request.Headers["X-Dev-User"].FirstOrDefault() ?? "viewer";
            var role = name.ToLowerInvariant() switch
            {
                "engineer" => UserRole.Engineer,
                "approver" => UserRole.Approver,
                "admin" => UserRole.Admin,
                _ => UserRole.Viewer
            };

            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, name),
                new Claim(ClaimTypes.Email, $"{name}@example.local"),
                new Claim(ClaimTypes.Role, role.ToString())
            ], SchemeName);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
