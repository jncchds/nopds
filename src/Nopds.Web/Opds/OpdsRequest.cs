namespace Nopds.Web.Opds;

/// <summary>
/// Parsed OPDS URL: /opds[/t/{token}][/v2][/l/{library}]/{rest...}. The prefix is preserved in every
/// generated link so feed-token and library-scoped URLs keep working while browsing.
/// </summary>
public sealed class OpdsRequest
{
    public required string Origin { get; init; }
    public required string Prefix { get; init; }
    public string? Token { get; init; }
    public bool V2 { get; init; }
    public int? LibraryId { get; init; }
    public required string[] Rest { get; init; }
    public int Page { get; init; } = 1;

    public static OpdsRequest Parse(HttpRequest request, string? path)
    {
        var segments = (path ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToList();
        string? token = null;
        var v2 = false;
        int? library = null;
        var prefix = request.PathBase + "/opds";

        if (segments.Count >= 2 && segments[0] == "t")
        {
            token = segments[1];
            prefix += "/t/" + Uri.EscapeDataString(token);
            segments.RemoveRange(0, 2);
        }

        if (segments.Count >= 1 && segments[0] == "v2")
        {
            v2 = true;
            prefix += "/v2";
            segments.RemoveAt(0);
        }

        if (segments.Count >= 2 && segments[0] == "l" && int.TryParse(segments[1], out var lib))
        {
            library = lib;
            segments.RemoveRange(0, 2);
        }

        var page = int.TryParse(request.Query["page"], out var p) && p > 0 ? p : 1;
        return new OpdsRequest
        {
            Origin = $"{request.Scheme}://{request.Host}",
            Prefix = prefix,
            Token = token,
            V2 = v2,
            LibraryId = library,
            Rest = segments.ToArray(),
            Page = page,
        };
    }

    public string Seg(int i) => i < Rest.Length ? Rest[i] : string.Empty;

    public long? Long(int i) => long.TryParse(Seg(i), out var v) ? v : null;

    public int? Int(int i) => int.TryParse(Seg(i), out var v) ? v : null;

    /// <summary>Absolute URL inside the current prefix (keeps library scope unless <paramref name="library"/> overrides it).</summary>
    public string Url(string path, int? library = -1, string? query = null)
    {
        var lib = library == -1 ? LibraryId : library;
        var sb = new System.Text.StringBuilder(Origin).Append(Prefix);
        if (lib is { } l)
        {
            sb.Append("/l/").Append(l);
        }

        sb.Append('/');
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(Uri.EscapeDataString(part)).Append('/');
        }

        if (!string.IsNullOrEmpty(query))
        {
            sb.Append('?').Append(query);
        }

        return sb.ToString();
    }

    /// <summary>URL for binary resources (downloads/covers): not versioned, token preserved.</summary>
    public string FileUrl(string path)
    {
        var prefix = Prefix.EndsWith("/v2", StringComparison.Ordinal) ? Prefix[..^3] : Prefix;
        return Origin + prefix + "/" + path.TrimStart('/');
    }

    public string NavType => V2 ? Nopds.Opds.OpdsTypes.Opds2 : Nopds.Opds.OpdsTypes.Navigation;

    public string AcqType => V2 ? Nopds.Opds.OpdsTypes.Opds2 : Nopds.Opds.OpdsTypes.Acquisition;
}
