using System.Globalization;
using System.Text;
using System.Xml;

namespace Nopds.Opds;

/// <summary>Serializes a <see cref="Feed"/> as an OPDS 1.2 Atom document.</summary>
public static class AtomWriter
{
    private const string Atom = "http://www.w3.org/2005/Atom";
    private const string OpdsNs = "http://opds-spec.org/2010/catalog";
    private const string DcNs = "http://purl.org/dc/terms/";
    private const string OsNs = "http://a9.com/-/spec/opensearch/1.1/";
    private const string ThrNs = "http://purl.org/syndication/thread/1.0";

    public static byte[] Write(Feed feed)
    {
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("feed", Atom);
            w.WriteAttributeString("xmlns", "opds", null, OpdsNs);
            w.WriteAttributeString("xmlns", "dc", null, DcNs);
            w.WriteAttributeString("xmlns", "opensearch", null, OsNs);
            w.WriteAttributeString("xmlns", "thr", null, ThrNs);

            w.WriteElementString("id", Atom, feed.Id);
            w.WriteElementString("title", Atom, feed.Title);
            if (feed.Subtitle is not null)
            {
                w.WriteElementString("subtitle", Atom, feed.Subtitle);
            }

            w.WriteElementString("updated", Atom, Date(feed.Updated));
            if (feed.Icon is not null)
            {
                w.WriteElementString("icon", Atom, feed.Icon);
            }

            w.WriteStartElement("author", Atom);
            w.WriteElementString("name", Atom, ".NET OPDS");
            w.WriteElementString("uri", Atom, "https://github.com/jncchds/nopds");
            w.WriteEndElement();

            if (feed.TotalResults is { } total)
            {
                w.WriteElementString("totalResults", OsNs, total.ToString(CultureInfo.InvariantCulture));
            }

            if (feed.ItemsPerPage is { } per)
            {
                w.WriteElementString("itemsPerPage", OsNs, per.ToString(CultureInfo.InvariantCulture));
            }

            foreach (var l in feed.Links)
            {
                WriteLink(w, l);
            }

            foreach (var e in feed.Entries)
            {
                WriteEntry(w, e);
            }

            w.WriteEndElement();
            w.WriteEndDocument();
        }

        return ms.ToArray();
    }

    private static void WriteEntry(XmlWriter w, FeedEntry e)
    {
        w.WriteStartElement("entry", Atom);
        w.WriteElementString("title", Atom, e.Title);
        w.WriteElementString("id", Atom, e.Id);
        w.WriteElementString("updated", Atom, Date(e.Updated));

        foreach (var a in e.Authors)
        {
            w.WriteStartElement("author", Atom);
            w.WriteElementString("name", Atom, a.Name);
            if (a.Href is not null)
            {
                w.WriteElementString("uri", Atom, a.Href);
            }

            w.WriteEndElement();
        }

        foreach (var c in e.Categories)
        {
            w.WriteStartElement("category", Atom);
            w.WriteAttributeString("term", c.Term);
            w.WriteAttributeString("label", c.Label);
            w.WriteEndElement();
        }

        if (e.Publication is { } p)
        {
            if (!string.IsNullOrEmpty(p.Language))
            {
                w.WriteElementString("language", DcNs, p.Language);
            }

            if (!string.IsNullOrEmpty(p.Issued))
            {
                w.WriteElementString("issued", DcNs, p.Issued);
            }

            if (!string.IsNullOrEmpty(p.Format))
            {
                w.WriteElementString("format", DcNs, p.Format);
            }
        }

        if (e.Summary is not null)
        {
            w.WriteStartElement("summary", Atom);
            w.WriteAttributeString("type", "text");
            w.WriteString(e.Summary);
            w.WriteEndElement();
        }

        if (e.ContentHtml is not null)
        {
            w.WriteStartElement("content", Atom);
            w.WriteAttributeString("type", "html");
            w.WriteString(e.ContentHtml);
            w.WriteEndElement();
        }

        foreach (var l in e.Links)
        {
            WriteLink(w, l);
        }

        w.WriteEndElement();
    }

    private static void WriteLink(XmlWriter w, FeedLink l)
    {
        w.WriteStartElement("link", Atom);
        w.WriteAttributeString("rel", l.Rel);
        w.WriteAttributeString("href", l.Href);
        w.WriteAttributeString("type", l.Type);
        if (l.Title is not null)
        {
            w.WriteAttributeString("title", l.Title);
        }

        if (l.Count is { } count)
        {
            w.WriteAttributeString("count", ThrNs, count.ToString(CultureInfo.InvariantCulture));
        }

        if (l.FacetGroup is not null)
        {
            w.WriteAttributeString("facetGroup", OpdsNs, l.FacetGroup);
            if (l.ActiveFacet)
            {
                w.WriteAttributeString("activeFacet", OpdsNs, "true");
            }
        }

        w.WriteEndElement();
    }

    private static string Date(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static byte[] OpenSearch(string title, string description, string templateUrl, string lang)
    {
        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
        {
            w.WriteStartDocument();
            w.WriteStartElement("OpenSearchDescription", OsNs);
            w.WriteElementString("ShortName", OsNs, title.Length > 16 ? title[..16] : title);
            w.WriteElementString("Description", OsNs, description);
            w.WriteElementString("InputEncoding", OsNs, "UTF-8");
            w.WriteElementString("OutputEncoding", OsNs, "UTF-8");
            w.WriteElementString("Language", OsNs, lang);
            w.WriteStartElement("Url", OsNs);
            w.WriteAttributeString("type", OpdsTypes.Acquisition);
            w.WriteAttributeString("template", templateUrl);
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndDocument();
        }

        return ms.ToArray();
    }
}
