namespace DeskDashboard.Core.Content;

public static class ContentTypes
{
    public const string Quran = "quran";
    public const string Hadith = "hadith";

    public static bool IsValid(string? type) => type is Quran or Hadith;
}

/// <summary>Full item for the Library.</summary>
public sealed record ContentItemDto(
    int Id,
    string Type,
    string Ref,
    string Source,
    string? Grade,
    string Arabic,
    string English,
    string Batch,
    long AddedAt,
    long? LastShownAt,
    int ShowCount,
    bool Hidden);

/// <summary>Compact item for ambient rotation.</summary>
public sealed record ActiveContentDto(
    int Id,
    string Type,
    string Ref,
    string? Grade,
    string Arabic,
    string English,
    long? LastShownAt);

public sealed record ContentStatusDto(
    int QuranCount,
    int HadithCount,
    int HiddenCount,
    long? LastRefreshAt,
    long? LastAttemptAt,
    string? LastResult,
    int LastAddedQuran,
    int LastAddedHadith,
    long NextDueAt,
    bool Refreshing,
    IReadOnlyList<string> Credits);

public sealed record ContentHiddenInput(bool Hidden);

/// <summary>An item produced by the seed bundle or a weekly refresh, before it is stored.</summary>
public sealed record ContentCandidate(
    string Type,
    string SourceKey,
    string Ref,
    string Source,
    string? Grade,
    string Arabic,
    string English);
