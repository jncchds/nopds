using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nopds.Opds;

/// <summary>Serializes a <see cref="Feed"/> as OPDS 2.0 JSON (navigation + publications).</summary>
public static class Opds2Writer
{
    public static byte[] Write(Feed feed)
    {
        var metadata = new JsonObject { ["title"] = feed.Title };
        if (feed.Subtitle is not null)
        {
            metadata["subtitle"] = feed.Subtitle;
        }

        metadata["modified"] = feed.Updated.UtcDateTime.ToString("o");
        if (feed.ItemsPerPage is { } per)
        {
            metadata["itemsPerPage"] = per;
        }

        if (feed.CurrentPage is { } cur)
        {
            metadata["currentPage"] = cur;
        }

        if (feed.TotalResults is { } total)
        {
            metadata["numberOfItems"] = total;
        }

        var root = new JsonObject
        {
            ["metadata"] = metadata,
            ["links"] = new JsonArray(feed.Links.Where(l => l.FacetGroup is null).Select(Link).ToArray()),
        };

        var facets = feed.Links.Where(l => l.FacetGroup is not null).GroupBy(l => l.FacetGroup!).ToList();
        if (facets.Count > 0)
        {
            root["facets"] = new JsonArray(facets.Select(g => (JsonNode)new JsonObject
            {
                ["metadata"] = new JsonObject { ["title"] = g.Key },
                ["links"] = new JsonArray(g.Select(Link).ToArray()),
            }).ToArray());
        }

        var navigation = feed.Entries.Where(e => e.Publication is null).ToList();
        var publications = feed.Entries.Where(e => e.Publication is not null).ToList();
        if (navigation.Count > 0)
        {
            root["navigation"] = new JsonArray(navigation.Select(e =>
            {
                var target = e.Links.FirstOrDefault(l => l.Rel is Rel.Subsection or Rel.Alternate) ?? e.Links.FirstOrDefault();
                var nav = new JsonObject
                {
                    ["href"] = target?.Href,
                    ["title"] = e.Title,
                    ["type"] = OpdsTypes.Opds2,
                    ["rel"] = "subsection",
                };
                if (e.Count is { } c)
                {
                    nav["properties"] = new JsonObject { ["numberOfItems"] = c };
                }

                return (JsonNode)nav;
            }).ToArray());
        }

        if (publications.Count > 0 || feed.IsAcquisition)
        {
            root["publications"] = new JsonArray(publications.Select(Publication).ToArray());
        }

        return JsonSerializer.SerializeToUtf8Bytes(root);
    }

    private static JsonNode Publication(FeedEntry e)
    {
        var p = e.Publication!;
        var meta = new JsonObject
        {
            ["@type"] = "http://schema.org/Book",
            ["identifier"] = e.Id,
            ["title"] = e.Title,
            ["modified"] = e.Updated.UtcDateTime.ToString("o"),
        };
        if (e.Authors.Count > 0)
        {
            meta["author"] = new JsonArray(e.Authors.Select(a =>
            {
                var o = new JsonObject { ["name"] = a.Name };
                if (a.Href is not null)
                {
                    o["links"] = new JsonArray(new JsonObject { ["href"] = a.Href, ["type"] = OpdsTypes.Opds2 });
                }

                return (JsonNode)o;
            }).ToArray());
        }

        if (!string.IsNullOrEmpty(p.Language))
        {
            meta["language"] = p.Language;
        }

        if (!string.IsNullOrEmpty(p.Issued))
        {
            meta["published"] = p.Issued;
        }

        if (e.Summary is not null)
        {
            meta["description"] = e.Summary;
        }

        if (e.Categories.Count > 0)
        {
            meta["subject"] = new JsonArray(e.Categories.Select(c => (JsonNode)new JsonObject { ["name"] = c.Label, ["code"] = c.Term }).ToArray());
        }

        if (p.Series.Count > 0)
        {
            meta["belongsTo"] = new JsonObject
            {
                ["series"] = new JsonArray(p.Series.Select(s =>
                {
                    var o = new JsonObject { ["name"] = s.Name };
                    if (s.Position > 0)
                    {
                        o["position"] = s.Position;
                    }

                    return (JsonNode)o;
                }).ToArray()),
            };
        }

        var links = e.Links.Where(l => l.Rel is not (Rel.Image or Rel.Thumbnail)).Select(Link).ToArray();
        var images = e.Links.Where(l => l.Rel is Rel.Image or Rel.Thumbnail)
            .OrderBy(l => l.Rel == Rel.Thumbnail ? 0 : 1).Select(l => (JsonNode)new JsonObject { ["href"] = l.Href, ["type"] = l.Type }).ToArray();
        var pub = new JsonObject { ["metadata"] = meta, ["links"] = new JsonArray(links) };
        if (images.Length > 0)
        {
            pub["images"] = new JsonArray(images);
        }

        return pub;
    }

    private static JsonNode Link(FeedLink l)
    {
        var type = l.Type.StartsWith("application/atom+xml", StringComparison.Ordinal) ? OpdsTypes.Opds2 : l.Type;
        var o = new JsonObject { ["rel"] = l.Rel, ["href"] = l.Href, ["type"] = type };
        if (l.Title is not null)
        {
            o["title"] = l.Title;
        }

        if (l.Count is not null || l.ActiveFacet)
        {
            var props = new JsonObject();
            if (l.Count is { } count)
            {
                props["numberOfItems"] = count;
            }

            o["properties"] = props;
        }

        return o;
    }
}
