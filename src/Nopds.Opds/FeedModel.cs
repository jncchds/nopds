namespace Nopds.Opds;

public static class Rel
{
    public const string Self = "self";
    public const string Start = "start";
    public const string Up = "up";
    public const string Next = "next";
    public const string Previous = "previous";
    public const string Search = "search";
    public const string Subsection = "subsection";
    public const string Related = "related";
    public const string Alternate = "alternate";
    public const string Acquisition = "http://opds-spec.org/acquisition/open-access";
    public const string Image = "http://opds-spec.org/image";
    public const string Thumbnail = "http://opds-spec.org/image/thumbnail";
    public const string Facet = "http://opds-spec.org/facet";
    public const string Shelf = "http://opds-spec.org/shelf";
    public const string New = "http://opds-spec.org/sort/new";
}

public static class OpdsTypes
{
    public const string Navigation = "application/atom+xml;profile=opds-catalog;kind=navigation";
    public const string Acquisition = "application/atom+xml;profile=opds-catalog;kind=acquisition";
    public const string OpenSearch = "application/opensearchdescription+xml";
    public const string Opds2 = "application/opds+json";
    public const string Opds2Publication = "application/opds-publication+json";
}

public sealed record FeedLink(string Rel, string Href, string Type, string? Title = null)
{
    public int? Count { get; init; }
    public string? FacetGroup { get; init; }
    public bool ActiveFacet { get; init; }
}

public sealed record FeedAuthor(string Name, string? Href);

public sealed record FeedCategory(string Term, string Label);

public sealed record PublicationInfo(
    string? Language,
    string? Issued,
    string? Format,
    long Size,
    IReadOnlyList<(string Name, int Position)> Series);

/// <summary>A feed entry: navigation (links to another feed) or publication (with acquisition links).</summary>
public sealed class FeedEntry
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
    public string? Summary { get; init; }

    /// <summary>HTML content (escaped in Atom).</summary>
    public string? ContentHtml { get; init; }

    public List<FeedAuthor> Authors { get; } = [];
    public List<FeedCategory> Categories { get; } = [];
    public List<FeedLink> Links { get; } = [];
    public PublicationInfo? Publication { get; init; }
    public int? Count { get; init; }
}

public sealed class Feed
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public DateTimeOffset Updated { get; init; } = DateTimeOffset.UtcNow;
    public bool IsAcquisition { get; init; }
    public string? Icon { get; init; }
    public List<FeedLink> Links { get; } = [];
    public List<FeedEntry> Entries { get; } = [];
    public int? ItemsPerPage { get; init; }
    public int? CurrentPage { get; init; }
    public long? TotalResults { get; init; }
}
