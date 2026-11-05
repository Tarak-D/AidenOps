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
    public const string CanManageIntegrations = "CanManageIntegrations";
    public const string CanRunEvaluationAcceptance = "CanRunEvaluationAcceptance";
}

/// <summary>
/// Development-only authentication: select a local demo identity through the
/// "X-Dev-User" header. No production identity provider is configured.
/// </summary>
public static class SecurityExtensions
{
    private const string DevelopmentIdentityConfigurationSection =
        "Authentication:DevelopmentIdentity";

    public static IServiceCollection AddAIOpsSecurity(
        this IServiceCollection services,
        IConfiguration config,
        IWebHostEnvironment environment)
    {
        var developmentIdentityConfiguration = config.GetSection(
            DevelopmentIdentityConfigurationSection);
        if (!environment.IsDevelopment() &&
            developmentIdentityConfiguration.Exists())
        {
            throw new InvalidOperationException(
                "Development identity authentication is configured outside the Development environment.");
        }

        var useDevelopmentIdentity =
            environment.IsDevelopment() &&
            developmentIdentityConfiguration.GetValue<bool>("Enabled");

        var authentication = services.AddAuthentication(o =>
            {
                o.DefaultScheme = useDevelopmentIdentity
                    ? DevIdentityHandler.SchemeName
                    : UnconfiguredAuthenticationHandler.SchemeName;
            });

        if (useDevelopmentIdentity)
        {
            authentication.AddScheme<
                AuthenticationSchemeOptions,
                DevIdentityHandler>(
                DevIdentityHandler.SchemeName,
                _ => { });
        }
        else
        {
            authentication.AddScheme<
                AuthenticationSchemeOptions,
                UnconfiguredAuthenticationHandler>(
                UnconfiguredAuthenticationHandler.SchemeName,
                _ => { });
        }

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.CanViewQueue, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.CanCreateTicket, p => p.RequireAuthenticatedUser())
            .AddPolicy(AuthPolicies.CanRetry, p => p.RequireRole(nameof(UserRole.Engineer), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanApprove, p => p.RequireRole(nameof(UserRole.Approver), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanViewAudit, p => p.RequireRole(nameof(UserRole.Admin)))
            .AddPolicy(
                AuthPolicies.CanRunEvaluationAcceptance,
                p => p.RequireRole(
                    nameof(UserRole.Engineer),
                    nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanManageKnowledge, p => p.RequireRole(nameof(UserRole.Engineer), nameof(UserRole.Admin)))
            .AddPolicy(AuthPolicies.CanManageIntegrations, p => p.RequireAssertion(context =>
                context.User.IsInRole(nameof(UserRole.Admin)) ||
                (environment.IsDevelopment() && context.User.Identities.Any(identity =>
                    identity.IsAuthenticated &&
                    identity.AuthenticationType == DevIdentityHandler.SchemeName))));

        return services;
    }

    public sealed class UnconfiguredAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(
            options,
            logger,
            encoder)
    {
        public const string SchemeName = "Unconfigured";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }

    public sealed class DevIdentityHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "DevIdentity";

        public DevIdentityHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            IWebHostEnvironment environment)
            : base(options, logger, encoder)
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Development identity authentication is available only in the Development environment.");
            }
        }

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
