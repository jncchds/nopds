using Microsoft.AspNetCore.Authorization;
using Nopds.Infrastructure.Settings;

namespace Nopds.Web.Auth;

public static class Policies
{
    /// <summary>Browse/download: authenticated users, or anyone when the library is public.</summary>
    public const string Reader = "reader";

    /// <summary>Signed-in users only (shelf, progress, profile).</summary>
    public const string User = "user";

    public const string Admin = "admin";

    /// <summary>Reader access for e-reader clients: Basic / feed-token auth with a Basic challenge.</summary>
    public const string OpdsReader = "opds-reader";

    /// <summary>Signed-in e-reader clients (KOReader sync, shelf).</summary>
    public const string OpdsUser = "opds-user";

    public static void Configure(AuthorizationOptions o, params string[] schemes)
    {
        o.AddPolicy(Reader, p => p.AddAuthenticationSchemes(schemes).AddRequirements(new ReaderRequirement()));
        o.AddPolicy(User, p => p.AddAuthenticationSchemes(schemes).RequireAuthenticatedUser());
        o.AddPolicy(Admin, p => p.AddAuthenticationSchemes(schemes).RequireClaim(NopdsClaims.Admin, "true"));
        o.AddPolicy(OpdsReader, p => p.AddAuthenticationSchemes(OpdsAuthenticationHandler.SchemeName).AddRequirements(new ReaderRequirement()));
        o.AddPolicy(OpdsUser, p => p.AddAuthenticationSchemes(OpdsAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
    }
}

public sealed class ReaderRequirement : IAuthorizationRequirement;

public sealed class ReaderRequirementHandler(SettingsStore settings) : AuthorizationHandler<ReaderRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ReaderRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true || settings.Current.Access == AccessMode.Public)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
