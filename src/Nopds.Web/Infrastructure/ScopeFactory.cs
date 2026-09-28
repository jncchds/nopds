using Nopds.Domain.Text;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Settings;
using Nopds.Web.Auth;

namespace Nopds.Web.Infrastructure;

/// <summary>Builds the catalog <see cref="Scope"/> for the current request (user access, language, duplicates).</summary>
public sealed class ScopeFactory(CurrentUser user, SettingsStore settings, IHttpContextAccessor http)
{
    public string Language
    {
        get
        {
            var ctx = http.HttpContext;
            var q = ctx?.Request.Query["lang"].ToString();
            return UiLanguages.Match(q) ?? UiLanguages.Match(user.Language)
                   ?? UiLanguages.FromAcceptLanguage(ctx?.Request.Headers.AcceptLanguage.ToString());
        }
    }

    public Scope Create(int? libraryId = null, bool? hideDuplicates = null) => new(
        user.IsAdmin ? null : user.AllowedLibraries,
        libraryId,
        hideDuplicates ?? user.HideDuplicates ?? settings.Current.HideDuplicates,
        settings.Current.PreferredFormats,
        Language,
        user.Id);
}
