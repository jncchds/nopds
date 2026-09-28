using System.Security.Claims;
using Nopds.Infrastructure.Identity;

namespace Nopds.Web.Auth;

public static class NopdsClaims
{
    public const string UserId = "sub";
    public const string Name = "name";
    public const string Admin = "admin";
    public const string Libraries = "libs";
    public const string HideDuplicates = "hidedup";
    public const string Language = "lang";

    public static IEnumerable<Claim> For(AppUser user)
    {
        yield return new Claim(UserId, user.Id.ToString());
        yield return new Claim(Name, user.UserName ?? string.Empty);
        if (user.IsAdmin)
        {
            yield return new Claim(Admin, "true");
        }

        if (user.AllowedLibraryIds is { } libs)
        {
            yield return new Claim(Libraries, string.Join(',', libs));
        }

        yield return new Claim(HideDuplicates, user.HideDuplicates ? "true" : "false");
        if (user.UiLanguage is { } lang)
        {
            yield return new Claim(Language, lang);
        }
    }
}

/// <summary>Convenience view over the authenticated principal.</summary>
public sealed class CurrentUser(IHttpContextAccessor accessor)
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid? Id => Guid.TryParse(Principal?.FindFirstValue(NopdsClaims.UserId), out var id) ? id : null;

    public string? Name => Principal?.FindFirstValue(NopdsClaims.Name);

    public bool IsAdmin => Principal?.HasClaim(NopdsClaims.Admin, "true") == true;

    /// <summary>Library ids the user may access; null means all.</summary>
    public int[]? AllowedLibraries
    {
        get
        {
            var v = Principal?.FindFirstValue(NopdsClaims.Libraries);
            return v is null ? null : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
        }
    }

    public bool? HideDuplicates => Principal?.FindFirstValue(NopdsClaims.HideDuplicates) is { } v ? v == "true" : null;

    public string? Language => Principal?.FindFirstValue(NopdsClaims.Language);
}
