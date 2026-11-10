namespace DeskDashboard.Core.Data.Entities;

/// <summary>One Quran ayah/passage or one hadith that ambient mode can show.</summary>
public sealed class ContentItem
{
    public int Id { get; set; }

    /// <summary>"quran" or "hadith".</summary>
    public string Type { get; set; } = "";

    /// <summary>Stable identity, e.g. "quran:2:45", "quran:94:5-8", "bukhari:6168", "muslim:2609a".</summary>
    public string SourceKey { get; set; } = "";

    /// <summary>Display reference, e.g. "Al-Baqara 2:45" or "Sahih Muslim 2609a".</summary>
    public string Ref { get; set; } = "";

    public string Source { get; set; } = "";
    public string? Grade { get; set; }
    public string Arabic { get; set; } = "";
    public string English { get; set; } = "";

    /// <summary>"seed" for the bundled starter set, otherwise the refresh week, e.g. "2026-W46".</summary>
    public string Batch { get; set; } = "seed";

    // Times are Unix milliseconds so SQLite can sort them.
    public long AddedAtMs { get; set; }
    public long? LastShownAtMs { get; set; }
    public int ShowCount { get; set; }

    /// <summary>Removed from rotation by the user. Kept so a refresh never adds it back.</summary>
    public bool Hidden { get; set; }
    public long? HiddenAtMs { get; set; }
}
