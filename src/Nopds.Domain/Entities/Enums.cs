namespace Nopds.Domain.Entities;

/// <summary>Kind of catalog node in a library tree.</summary>
public enum CatalogType
{
    Directory = 0,
    Zip = 1,
    Inpx = 2,
    Inp = 3,
}

/// <summary>Where the book bytes physically live.</summary>
public enum BookContainer
{
    /// <summary>Plain file on disk.</summary>
    File = 0,

    /// <summary>Entry inside a ZIP archive found while scanning.</summary>
    Zip = 1,

    /// <summary>Entry inside a ZIP archive described by an INPX index.</summary>
    Inpx = 2,
}

/// <summary>First-character classification used for alphabet navigation (ported from SimpleOPDS).</summary>
public enum LangCode
{
    All = 0,
    Cyrillic = 1,
    Latin = 2,
    Digits = 3,
    Other = 9,
}

public enum CoverState
{
    Unknown = 0,
    None = 1,
    Present = 2,
}
