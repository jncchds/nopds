namespace Nopds.Infrastructure.Settings;

public enum AccessMode
{
    /// <summary>Everything requires login (web and OPDS).</summary>
    Private = 0,

    /// <summary>Anonymous visitors may browse and download; shelf/progress need login.</summary>
    Public = 1,
}

/// <summary>Runtime settings editable from the admin UI (replaces SimpleOPDS constance config).</summary>
public sealed record AppSettings
{
    public string Title { get; init; } = ".NET OPDS";
    public string Subtitle { get; init; } = ".NET OPDS by CHDS";
    public AccessMode Access { get; init; } = AccessMode.Private;

    /// <summary>Items per page in OPDS feeds and API lists (SOPDS_MAXITEMS).</summary>
    public int MaxItems { get; init; } = 60;

    /// <summary>Alphabet groups with more items than this are split by the next letter (SOPDS_SPLITITEMS).</summary>
    public int SplitItems { get; init; } = 300;

    public bool AlphabetMenu { get; init; } = true;

    /// <summary>Use transliterated title instead of the file name for downloads.</summary>
    public bool TitleAsFilename { get; init; } = true;

    public bool ShowCovers { get; init; } = true;

    /// <summary>Default duplicate hiding for anonymous users.</summary>
    public bool HideDuplicates { get; init; } = true;

    /// <summary>Format preference when picking the primary edition of a duplicate group.</summary>
    public string[] PreferredFormats { get; init; } = ["epub", "fb2", "azw3", "mobi", "pdf", "djvu"];

    public ConversionSettings Conversion { get; init; } = new();
    public TelegramSettings Telegram { get; init; } = new();
}

public sealed record ConversionSettings
{
    public bool BuiltInFb2ToEpub { get; init; } = true;

    /// <summary>External converters, e.g. {"Target":"mobi","Command":"ebook-convert {input} {output}"}.</summary>
    public ExternalConverter[] External { get; init; } = [];

    public int CacheSizeMb { get; init; } = 1024;
    public int TimeoutSeconds { get; init; } = 120;
}

public sealed record ExternalConverter
{
    public string Source { get; init; } = "fb2";
    public string Target { get; init; } = "mobi";
    public string Command { get; init; } = "";
}

public sealed record TelegramSettings
{
    public bool Enabled { get; init; }
    public string? BotToken { get; init; }

    /// <summary>Only Telegram users linked to an account may use the bot.</summary>
    public bool RequireLinkedUser { get; init; } = true;

    public int MaxItems { get; init; } = 10;
}
