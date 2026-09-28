using System.Globalization;
using System.Net;
using System.Text;
using Nopds.Conversion;
using Nopds.Domain.Entities;
using Nopds.Formats;
using Nopds.Infrastructure.Browse;
using Nopds.Infrastructure.Settings;
using Nopds.Opds;
using Nopds.Web.Auth;
using Nopds.Web.Infrastructure;
using L = Nopds.Infrastructure.Localization.ServerStrings;

namespace Nopds.Web.Opds;

/// <summary>
/// Builds OPDS feeds. URL layout follows SimpleOPDS (catalogs, books/authors/series by language and
/// letters, genres, search/books/{b|m|e|a|s|as|g|u|d}/...) so existing reader bookmarks keep working.
/// </summary>
public sealed class OpdsCatalog(
    CatalogService catalog,
    ScopeFactory scopes,
    SettingsStore settings,
    ConversionService conversion,
    CurrentUser user)
{
    private static readonly HashSet<string> NoZipFormats = ["epub", "kepub", "mobi", "azw", "azw3", "cbz", "docx", "zip"];

    private OpdsRequest R { get; set; } = null!;
    private string Lang => scopes.Language;
    private AppSettings S => settings.Current;
    private Scope Scope(bool? hideDupes = null) => scopes.Create(R.LibraryId, hideDupes ?? DupesOverride);
    private bool? DupesOverride { get; set; }

    public async Task<Feed?> BuildAsync(OpdsRequest request, HttpRequest http, CancellationToken ct)
    {
        R = request;
        DupesOverride = http.Query["dupes"].ToString() switch
        {
            "show" => false,
            "hide" => true,
            _ => null,
        };

        if (R.LibraryId is { } lib && !catalog.CanAccessLibrary(Scope(), lib))
        {
            return null;
        }

        return R.Seg(0) switch
        {
            "" => await RootAsync(ct),
            "libraries" => await LibrariesAsync(ct),
            "catalogs" => await CatalogsAsync(ct),
            "books" => await AlphabetAsync(CatalogService.AlphabetKind.Books, ct),
            "authors" => await AlphabetAsync(CatalogService.AlphabetKind.Authors, ct),
            "series" => await AlphabetAsync(CatalogService.AlphabetKind.Series, ct),
            "genres" => R.Rest.Length > 1 ? await GenresAsync(R.Seg(1), ct) : await GenreSectionsAsync(ct),
            "new" => await BooksFeedAsync("new", L.Get(Lang, "recent"), new BookQuery { Sort = BookSort.Added }, ct),
            "shelf" => user.Id is { } uid
                ? await BooksFeedAsync("shelf", L.Get(Lang, "shelf"), new BookQuery { ShelfOf = uid, Sort = BookSort.Shelf }, ct)
                : null,
            "author" => await AuthorAsync(ct),
            "search" => await SearchAsync(http, ct),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ helpers

    private Feed NewFeed(string id, string title, bool acquisition = false, string? subtitle = null, int? page = null) =>
        new()
        {
            Id = "tag:nopds:" + id,
            Title = title,
            Subtitle = subtitle ?? S.Subtitle,
            IsAcquisition = acquisition,
            Icon = R.Origin + "/favicon.svg",
            ItemsPerPage = page is null ? null : S.MaxItems,
            CurrentPage = page,
        };

    private void CommonLinks(Feed f, string selfPath, string? query = null)
    {
        f.Links.Add(new FeedLink(Rel.Self, R.Url(selfPath, query: query), f.IsAcquisition ? R.AcqType : R.NavType));
        f.Links.Add(new FeedLink(Rel.Start, R.Url("", library: null), R.NavType, S.Title));
        f.Links.Add(new FeedLink(Rel.Search, R.FileUrl("search.xml"), OpdsTypes.OpenSearch, L.Get(Lang, "search")));
        if (R.V2)
        {
            f.Links.Add(new FeedLink(Rel.Search, R.Url("search/books/m") + "{?query}", OpdsTypes.Opds2, L.Get(Lang, "search.books")));
        }
        else
        {
            f.Links.Add(new FeedLink(Rel.Search, R.Url("search") + "{searchTerms}/", OpdsTypes.Acquisition, L.Get(Lang, "search")));
        }
    }

    private FeedEntry Nav(string id, string title, string href, string? summary = null, int? count = null, bool acquisition = false)
    {
        var e = new FeedEntry
        {
            Id = "tag:nopds:" + id,
            Title = title,
            Summary = summary,
            Count = count,
            ContentHtml = summary is null ? null : WebUtility.HtmlEncode(summary),
        };
        e.Links.Add(new FeedLink(Rel.Subsection, href, acquisition ? R.AcqType : R.NavType, title) { Count = count });
        return e;
    }

    private static void Paging(Feed f, OpdsRequest r, Func<int, string> pageUrl, int page, bool hasNext)
    {
        var type = f.IsAcquisition ? r.AcqType : r.NavType;
        if (page > 1)
        {
            f.Links.Add(new FeedLink(Rel.Previous, pageUrl(page - 1), type));
        }

        if (hasNext)
        {
            f.Links.Add(new FeedLink(Rel.Next, pageUrl(page + 1), type));
        }
    }

    private string PageQuery(int page, string? extra = null)
    {
        var parts = new List<string>();
        if (page > 1)
        {
            parts.Add("page=" + page);
        }

        if (DupesOverride is { } d)
        {
            parts.Add("dupes=" + (d ? "hide" : "show"));
        }

        if (!string.IsNullOrEmpty(extra))
        {
            parts.Add(extra);
        }

        return string.Join('&', parts);
    }

    // ------------------------------------------------------------------ root & libraries

    private async Task<Feed> RootAsync(CancellationToken ct)
    {
        var libs = await catalog.LibrariesAsync(Scope(), ct);
        var libName = R.LibraryId is { } id ? libs.FirstOrDefault(l => l.Id == id)?.Name : null;
        var f = NewFeed("root" + (R.LibraryId is { } lid ? ":" + lid : ""), libName is null ? S.Title : $"{S.Title} — {libName}");
        CommonLinks(f, "");
        var stats = await catalog.StatsAsync(Scope(), ct);

        if (R.LibraryId is null && libs.Count > 1)
        {
            f.Entries.Add(Nav("libraries", L.Get(Lang, "libraries"), R.Url("libraries"), L.Get(Lang, "libraries.desc"), libs.Count));
        }

        f.Entries.Add(Nav("catalogs", L.Get(Lang, "folders"), R.Url("catalogs"), L.Get(Lang, "folders.desc")));
        f.Entries.Add(Nav("books", L.Get(Lang, "titles"), R.Url("books"), L.Format(Lang, "books.count", stats.Books), (int)stats.Books));
        f.Entries.Add(Nav("authors", L.Get(Lang, "authors"), R.Url("authors"), L.Get(Lang, "authors.desc"), (int)stats.Authors));
        f.Entries.Add(Nav("series", L.Get(Lang, "series"), R.Url("series"), L.Get(Lang, "series.desc"), (int)stats.Series));
        f.Entries.Add(Nav("genres", L.Get(Lang, "genres"), R.Url("genres"), L.Get(Lang, "genres.desc"), (int)stats.Genres));
        f.Entries.Add(Nav("new", L.Get(Lang, "recent"), R.Url("new"), L.Get(Lang, "recent.desc"), acquisition: true));
        if (user.IsAuthenticated)
        {
            f.Entries.Add(Nav("shelf", L.Get(Lang, "shelf"), R.Url("shelf"), L.Get(Lang, "shelf.desc"), acquisition: true));
        }

        return f;
    }

    private async Task<Feed> LibrariesAsync(CancellationToken ct)
    {
        var f = NewFeed("libraries", L.Get(Lang, "libraries"));
        CommonLinks(f, "libraries");
        foreach (var l in await catalog.LibrariesAsync(Scope(), ct))
        {
            f.Entries.Add(Nav("library:" + l.Id, l.Name, R.Url("", library: l.Id), L.Format(Lang, "books.count", l.Books), l.Books));
        }

        return f;
    }

    private async Task<Feed?> CatalogsAsync(CancellationToken ct)
    {
        if (R.LibraryId is null)
        {
            var libs = await catalog.LibrariesAsync(Scope(), ct);
            if (libs.Count != 1)
            {
                var lf = NewFeed("catalogs", L.Get(Lang, "folders"));
                CommonLinks(lf, "catalogs");
                foreach (var l in libs)
                {
                    lf.Entries.Add(Nav("catalogs:" + l.Id, l.Name, R.Url("catalogs", library: l.Id), L.Format(Lang, "books.count", l.Books), l.Books));
                }

                return lf;
            }

            R = new OpdsRequest { Origin = R.Origin, Prefix = R.Prefix, Token = R.Token, V2 = R.V2, LibraryId = libs[0].Id, Rest = R.Rest, Page = R.Page };
        }

        var catId = R.Long(1);
        var page = R.Int(2) ?? R.Page;
        var listing = await catalog.CatalogAsync(Scope(), R.LibraryId!.Value, catId, page, S.MaxItems, ct);
        if (listing is null)
        {
            return null;
        }

        var title = listing.Current is null || listing.Current.Path == "." ? L.Get(Lang, "folders") : listing.Current.Name;
        var f = NewFeed("catalog:" + (listing.Current?.Id.ToString(CultureInfo.InvariantCulture) ?? "root"), title,
            acquisition: listing.Books.Items.Count > 0, page: page);
        var self = catId is null ? "catalogs" : $"catalogs/{catId}";
        CommonLinks(f, self, PageQuery(page));
        if (listing.Breadcrumbs.LastOrDefault() is { } parent)
        {
            f.Links.Add(new FeedLink(Rel.Up, R.Url($"catalogs/{parent.Id}"), R.NavType));
        }

        if (page == 1)
        {
            foreach (var c in listing.Children)
            {
                f.Entries.Add(Nav("catalog:" + c.Id, c.Name, R.Url($"catalogs/{c.Id}")));
            }
        }

        foreach (var b in listing.Books.Items)
        {
            f.Entries.Add(BookEntry(b));
        }

        Paging(f, R, p => R.Url(self, query: PageQuery(p)), page, listing.Books.HasNext);
        return f;
    }

    // ------------------------------------------------------------------ alphabet navigation

    private async Task<Feed?> AlphabetAsync(CatalogService.AlphabetKind kind, CancellationToken ct)
    {
        var section = R.Seg(0);
        var titleKey = kind switch
        {
            CatalogService.AlphabetKind.Books => "titles",
            CatalogService.AlphabetKind.Authors => "authors",
            _ => "series",
        };

        // /books/ → choose alphabet
        if (R.Rest.Length == 1)
        {
            var lf = NewFeed(section, L.Get(Lang, titleKey));
            CommonLinks(lf, section);
            foreach (var code in new[] { LangCode.Cyrillic, LangCode.Latin, LangCode.Digits, LangCode.Other, LangCode.All })
            {
                lf.Entries.Add(Nav($"{section}:lang:{(int)code}", L.Get(Lang, "lang." + (int)code), R.Url($"{section}/{(int)code}")));
            }

            return lf;
        }

        var lang = (LangCode)(R.Int(1) ?? 0);
        var prefix = R.Seg(2);
        var f = NewFeed($"{section}:{(int)lang}:{prefix}", prefix.Length > 0 ? $"{L.Get(Lang, titleKey)}: {prefix}" : L.Get(Lang, titleKey));
        CommonLinks(f, $"{section}/{(int)lang}/{prefix}");
        f.Links.Add(new FeedLink(Rel.Up, R.Url(prefix.Length > 1 ? $"{section}/{(int)lang}/{prefix[..^1]}" : $"{section}/{(int)lang}"), R.NavType));

        if (!S.AlphabetMenu && prefix.Length == 0)
        {
            return await SearchListAsync(kind, "b", "", lang, ct);
        }

        var groups = await catalog.AlphabetAsync(Scope(), kind, lang, prefix, ct);
        var searchKind = section == "books" ? "books" : section;
        foreach (var g in groups)
        {
            var deeper = g.Count > S.SplitItems && g.Prefix.Length < 20;
            var href = deeper
                ? R.Url($"{section}/{(int)lang}/{g.Prefix}")
                : R.Url($"search/{searchKind}/b/{g.Prefix}", query: lang == LangCode.All ? null : $"lang={(int)lang}");
            f.Entries.Add(Nav($"{section}:{(int)lang}:{g.Prefix}", g.Prefix, href, g.Count.ToString(CultureInfo.InvariantCulture), g.Count,
                acquisition: !deeper && kind == CatalogService.AlphabetKind.Books));
        }

        return f;
    }

    // ------------------------------------------------------------------ genres

    private async Task<Feed> GenreSectionsAsync(CancellationToken ct)
    {
        var f = NewFeed("genres", L.Get(Lang, "genres"));
        CommonLinks(f, "genres");
        foreach (var s in await catalog.GenreSectionsAsync(Scope(), ct))
        {
            f.Entries.Add(Nav("genres:" + s.Key, s.Name, R.Url($"genres/{s.Key}"), L.Format(Lang, "books.count", s.Books), s.Books));
        }

        return f;
    }

    private async Task<Feed> GenresAsync(string section, CancellationToken ct)
    {
        var f = NewFeed("genres:" + section, catalog.SectionName(section, Lang));
        CommonLinks(f, $"genres/{section}");
        f.Links.Add(new FeedLink(Rel.Up, R.Url("genres"), R.NavType));
        foreach (var g in await catalog.GenresAsync(Scope(), section, ct))
        {
            f.Entries.Add(Nav("genre:" + g.Id, g.Name, R.Url($"search/books/g/{g.Id}"), L.Format(Lang, "books.count", g.Books), g.Books, acquisition: true));
        }

        return f;
    }

    // ------------------------------------------------------------------ authors

    private async Task<Feed?> AuthorAsync(CancellationToken ct)
    {
        if (R.Long(1) is not { } id || await catalog.AuthorAsync(id, ct) is not { } author)
        {
            return null;
        }

        var f = NewFeed("author:" + id, author.Name);
        CommonLinks(f, $"author/{id}");
        f.Entries.Add(Nav($"author:{id}:all", L.Get(Lang, "author.all"), R.Url($"search/books/a/{id}"), acquisition: true));
        f.Entries.Add(Nav($"author:{id}:series", L.Get(Lang, "author.series"), R.Url($"search/books/as/{id}")));
        f.Entries.Add(Nav($"author:{id}:noseries", L.Get(Lang, "author.noseries"), R.Url($"search/books/as/{id}/0"), acquisition: true));
        return f;
    }

    // ------------------------------------------------------------------ search

    private async Task<Feed?> SearchAsync(HttpRequest http, CancellationToken ct)
    {
        // OPDS 2 template: search/books/m/?query=...
        var queryTerms = http.Query["query"].ToString();
        if (R.Rest.Length == 1)
        {
            return string.IsNullOrWhiteSpace(queryTerms) ? null : SearchTypes(queryTerms);
        }

        var what = R.Seg(1);
        if (what is not ("books" or "authors" or "series"))
        {
            // /search/{terms}/ → choose what to search
            return SearchTypes(R.Seg(1));
        }

        var type = R.Seg(2);
        var terms = R.Rest.Length > 3 ? R.Seg(3) : queryTerms;
        var lang = (LangCode)(int.TryParse(http.Query["lang"], out var lc) ? lc : 0);

        if (what == "books")
        {
            return type switch
            {
                "as" when R.Rest.Length <= 4 => await AuthorSeriesAsync(R.Long(3) ?? 0, ct),
                "as" => await BooksFeedAsync($"search/books/as/{R.Seg(3)}/{R.Seg(4)}", await AuthorSeriesTitleAsync(R.Long(3) ?? 0, R.Long(4) ?? 0, ct),
                    new BookQuery
                    {
                        AuthorId = R.Long(3) ?? 0,
                        SeriesId = R.Long(4) is > 0 ? R.Long(4) : null,
                        WithoutSeries = R.Long(4) is 0,
                        Sort = R.Long(4) is > 0 ? BookSort.SeriesNumber : BookSort.Title,
                    }, ct, pageSegment: 5),
                _ => await SearchListAsync(CatalogService.AlphabetKind.Books, type, terms, lang, ct),
            };
        }

        return await SearchListAsync(what == "authors" ? CatalogService.AlphabetKind.Authors : CatalogService.AlphabetKind.Series, type, terms, lang, ct);
    }

    private Feed SearchTypes(string terms)
    {
        var f = NewFeed("search:" + terms, $"{L.Get(Lang, "search")}: {terms}");
        CommonLinks(f, $"search/{terms}");
        f.Entries.Add(Nav("search:books", L.Get(Lang, "search.books"), R.Url($"search/books/m/{terms}"), L.Format(Lang, "search.books.desc", terms), acquisition: true));
        f.Entries.Add(Nav("search:authors", L.Get(Lang, "search.authors"), R.Url($"search/authors/m/{terms}"), L.Format(Lang, "search.authors.desc", terms)));
        f.Entries.Add(Nav("search:series", L.Get(Lang, "search.series"), R.Url($"search/series/m/{terms}"), L.Format(Lang, "search.series.desc", terms)));
        return f;
    }

    private async Task<Feed?> SearchListAsync(CatalogService.AlphabetKind kind, string type, string terms, LangCode lang, CancellationToken ct)
    {
        var match = type switch
        {
            "b" => TextMatch.Begins,
            "e" => TextMatch.Exact,
            _ => TextMatch.Contains,
        };
        var langFilter = lang == LangCode.All ? (LangCode?)null : lang;

        if (kind == CatalogService.AlphabetKind.Books)
        {
            var id = R.Long(3);
            var query = type switch
            {
                "a" => new BookQuery { AuthorId = id ?? 0 },
                "s" => new BookQuery { SeriesId = id ?? 0, Sort = BookSort.SeriesNumber },
                "g" => new BookQuery { GenreId = (int)(id ?? 0) },
                "d" => new BookQuery { EditionsOf = id ?? 0 },
                "u" => user.Id is { } uid ? new BookQuery { ShelfOf = uid, Sort = BookSort.Shelf } : null,
                _ => new BookQuery { Text = terms, Match = match, Lang = langFilter },
            };
            if (query is null)
            {
                return null;
            }

            var title = type switch
            {
                "a" when id is { } a => L.Format(Lang, "author.books", (await catalog.AuthorAsync(a, ct))?.Name),
                "s" when id is { } s => L.Format(Lang, "series.books", (await catalog.SeriesNameAsync(s, ct))?.Name),
                "g" when id is { } g => L.Format(Lang, "genre.books", (await catalog.GenreAsync((int)g, Lang, ct))?.Name),
                "d" => L.Get(Lang, "editions.title"),
                "u" => L.Get(Lang, "shelf"),
                _ => L.Get(Lang, "found.books"),
            };
            return await BooksFeedAsync($"search/books/{type}/{R.Seg(3)}", title, query, ct, pageSegment: 4,
                extraQuery: lang == LangCode.All ? null : $"lang={(int)lang}");
        }

        var page = R.Int(4) ?? R.Page;
        var nameQuery = new NameQuery { Text = terms, Match = match, Lang = langFilter, Page = page, PageSize = S.MaxItems };
        var isAuthors = kind == CatalogService.AlphabetKind.Authors;
        if (!isAuthors && type == "a" && long.TryParse(terms, out var authorId))
        {
            nameQuery = nameQuery with { Text = null, AuthorId = authorId };
        }

        var result = isAuthors ? await catalog.AuthorsAsync(Scope(), nameQuery, ct) : await catalog.SeriesAsync(Scope(), nameQuery, ct);
        var section = isAuthors ? "authors" : "series";
        var f = NewFeed($"search:{section}:{type}:{terms}", L.Get(Lang, isAuthors ? "found.authors" : "found.series"), page: page);
        var self = $"search/{section}/{type}/{terms}";
        var extra = lang == LangCode.All ? null : $"lang={(int)lang}";
        CommonLinks(f, self, PageQuery(page, extra));
        foreach (var item in result.Items)
        {
            f.Entries.Add(isAuthors
                ? Nav("author:" + item.Id, item.Name, R.Url($"author/{item.Id}"), L.Format(Lang, "books.count", item.Books), item.Books)
                : Nav("series:" + item.Id, item.Name, R.Url($"search/books/s/{item.Id}"), L.Format(Lang, "books.count", item.Books), item.Books, acquisition: true));
        }

        Paging(f, R, p => R.Url(self, query: PageQuery(p, extra)), page, result.HasNext);
        return f;
    }

    private async Task<Feed> AuthorSeriesAsync(long authorId, CancellationToken ct)
    {
        var author = await catalog.AuthorAsync(authorId, ct);
        var f = NewFeed($"author:{authorId}:series", $"{author?.Name}: {L.Get(Lang, "author.series")}");
        CommonLinks(f, $"search/books/as/{authorId}");
        f.Links.Add(new FeedLink(Rel.Up, R.Url($"author/{authorId}"), R.NavType));
        var page = 1;
        while (true)
        {
            var series = await catalog.SeriesAsync(Scope(), new NameQuery { AuthorId = authorId, Page = page, PageSize = 500 }, ct);
            foreach (var s in series.Items)
            {
                f.Entries.Add(Nav($"author:{authorId}:series:{s.Id}", s.Name, R.Url($"search/books/as/{authorId}/{s.Id}"),
                    L.Format(Lang, "books.count", s.Books), s.Books, acquisition: true));
            }

            if (!series.HasNext || page++ >= 10)
            {
                break;
            }
        }

        f.Entries.Add(Nav($"author:{authorId}:noseries", L.Get(Lang, "author.noseries"), R.Url($"search/books/as/{authorId}/0"), acquisition: true));
        return f;
    }

    private async Task<string> AuthorSeriesTitleAsync(long authorId, long seriesId, CancellationToken ct)
    {
        var author = (await catalog.AuthorAsync(authorId, ct))?.Name ?? "";
        var series = seriesId > 0 ? (await catalog.SeriesNameAsync(seriesId, ct))?.Name : L.Get(Lang, "author.noseries");
        return $"{author}: {series}";
    }

    // ------------------------------------------------------------------ book feeds

    private async Task<Feed> BooksFeedAsync(string selfPath, string title, BookQuery query, CancellationToken ct, int pageSegment = -1, string? extraQuery = null)
    {
        var page = (pageSegment >= 0 ? R.Int(pageSegment) : null) ?? R.Page;
        var scope = Scope();
        var result = await catalog.BooksAsync(scope, query with { Page = page, PageSize = S.MaxItems }, ct);
        var f = NewFeed(selfPath.Replace('/', ':'), title, acquisition: true, page: page);
        CommonLinks(f, selfPath, PageQuery(page, extraQuery));

        if (query.EditionsOf is null && query.ShelfOf is null)
        {
            var hidden = scope.HideDuplicates;
            f.Links.Add(new FeedLink(Rel.Facet, R.Url(selfPath, query: Join(extraQuery, "dupes=hide")), R.AcqType, L.Get(Lang, "dupes.hide"))
            {
                FacetGroup = L.Get(Lang, "editions.title"),
                ActiveFacet = hidden,
            });
            f.Links.Add(new FeedLink(Rel.Facet, R.Url(selfPath, query: Join(extraQuery, "dupes=show")), R.AcqType, L.Get(Lang, "dupes.show"))
            {
                FacetGroup = L.Get(Lang, "editions.title"),
                ActiveFacet = !hidden,
            });
        }

        foreach (var b in result.Items)
        {
            f.Entries.Add(BookEntry(b));
        }

        Paging(f, R, p => R.Url(selfPath, query: PageQuery(p, extraQuery)), page, result.HasNext);
        return f;
    }

    private static string Join(string? a, string b) => string.IsNullOrEmpty(a) ? b : a + "&" + b;

    private FeedEntry BookEntry(BookSummary b)
    {
        var sizeKb = Math.Max(1, b.FileSize / 1024);
        var html = new StringBuilder();
        foreach (var line in (b.Annotation ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            html.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        }

        void Row(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                html.Append("<b>").Append(WebUtility.HtmlEncode(L.Get(Lang, key))).Append(":</b> ").Append(WebUtility.HtmlEncode(value)).Append("<br/>");
            }
        }

        html.Append("<p>");
        Row("author", string.Join(", ", b.Authors.Select(a => a.Name)));
        Row("series", string.Join(", ", b.Series.Select(s => s.Number > 0 ? $"{s.Name} #{s.Number}" : s.Name)));
        Row("genre", string.Join(", ", b.Genres.Select(g => g.Name)));
        Row("language", b.Lang);
        Row("date", b.DocDate);
        Row("format", b.Format.ToUpperInvariant());
        Row("size", sizeKb.ToString(CultureInfo.InvariantCulture) + " KB");
        html.Append("</p>");

        var entry = new FeedEntry
        {
            Id = "tag:nopds:book:" + b.Id,
            Title = b.Title,
            Updated = b.RegisteredAt,
            Summary = b.Annotation is { Length: > 0 } ann ? (ann.Length > 500 ? ann[..500] + "…" : ann) : null,
            ContentHtml = html.ToString(),
            Publication = new PublicationInfo(b.Lang, b.DocDate, MediaTypes.ForFormat(b.Format), b.FileSize, b.Series.Select(s => (s.Name, s.Number)).ToList()),
        };

        foreach (var a in b.Authors)
        {
            entry.Authors.Add(new FeedAuthor(a.Name, R.Url($"author/{a.Id}")));
        }

        foreach (var g in b.Genres)
        {
            entry.Categories.Add(new FeedCategory(g.Code, g.Name));
        }

        entry.Links.Add(new FeedLink(Rel.Acquisition, R.FileUrl($"download/{b.Id}/0"), MediaTypes.ForFormat(b.Format), L.Get(Lang, "download")));
        if (!NoZipFormats.Contains(b.Format))
        {
            entry.Links.Add(new FeedLink(Rel.Acquisition, R.FileUrl($"download/{b.Id}/1"),
                b.Format == "fb2" ? MediaTypes.Fb2Zip : MediaTypes.Zip, L.Format(Lang, "download.zip", b.Format.ToUpperInvariant())));
        }

        foreach (var target in conversion.TargetsFor(b.Format))
        {
            entry.Links.Add(new FeedLink(Rel.Acquisition, R.FileUrl($"convert/{b.Id}/{target}"), MediaTypes.ForFormat(target),
                L.Format(Lang, "download.as", target.ToUpperInvariant())));
        }

        if (S.ShowCovers && b.Cover != CoverState.None)
        {
            entry.Links.Add(new FeedLink(Rel.Image, R.FileUrl($"cover/{b.Id}"), "image/jpeg"));
            entry.Links.Add(new FeedLink(Rel.Thumbnail, R.FileUrl($"thumb/{b.Id}"), "image/webp"));
        }

        foreach (var a in b.Authors)
        {
            entry.Links.Add(new FeedLink(Rel.Related, R.Url($"author/{a.Id}"), R.NavType, L.Format(Lang, "author.books", a.Name)));
        }

        foreach (var s in b.Series)
        {
            entry.Links.Add(new FeedLink(Rel.Related, R.Url($"search/books/s/{s.Id}"), R.AcqType, L.Format(Lang, "series.books", s.Name)));
        }

        if (b.Editions > 1)
        {
            entry.Links.Add(new FeedLink(Rel.Related, R.Url($"search/books/d/{b.Id}"), R.AcqType, L.Format(Lang, "editions", b.Editions - 1))
            {
                Count = b.Editions,
            });
        }

        return entry;
    }
}
