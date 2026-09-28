using Nopds.Formats;

namespace Nopds.Conversion;

internal static class MetadataMerge
{
    /// <summary>Copies fields that <paramref name="target"/> lacks from <paramref name="source"/>.</summary>
    public static BookMetadata FillGaps(BookMetadata target, BookMetadata source)
    {
        if (string.IsNullOrWhiteSpace(target.Title))
        {
            target.Title = source.Title;
        }

        if (string.IsNullOrWhiteSpace(target.Lang))
        {
            target.Lang = source.Lang;
        }

        if (string.IsNullOrWhiteSpace(target.Annotation))
        {
            target.Annotation = source.Annotation;
        }

        if (string.IsNullOrWhiteSpace(target.DocDate))
        {
            target.DocDate = source.DocDate;
        }

        if (target.Authors.Count == 0)
        {
            target.Authors.AddRange(source.Authors);
        }

        if (target.Genres.Count == 0)
        {
            target.Genres.AddRange(source.Genres);
        }

        if (target.Series.Count == 0)
        {
            target.Series.AddRange(source.Series);
        }

        return target;
    }

    /// <summary>Catalog metadata wins; the file's own metadata fills what the catalog lacks.</summary>
    public static BookMetadata Combine(BookMetadata catalog, BookMetadata? embedded)
    {
        var result = FillGaps(FillGaps(new BookMetadata(), catalog), embedded ?? new BookMetadata());
        result.Cover = catalog.Cover ?? embedded?.Cover;
        return result;
    }
}
