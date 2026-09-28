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

    public static void Configure(AuthorizationOptions o, params string[] schemes)
    {
        o.AddPolicy(Reader, p => p.AddAuthenticationSchemes(schemes).AddRequirements(new ReaderRequirement()));
        o.AddPolicy(User, p => p.AddAuthenticationSchemes(schemes).RequireAuthenticatedUser());
        o.AddPolicy(Admin, p => p.AddAuthenticationSchemes(schemes).RequireClaim(NopdsClaims.Admin, "true"));
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
