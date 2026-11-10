using System.Text.Json;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeskDashboard.Core.Content;

/// <summary>Stores, lists and updates the Ayat/Hadith shown in ambient mode.</summary>
public sealed class ContentService(DashboardDbContext db, TimeProvider? clock = null)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(7);

    public static readonly IReadOnlyList<string> Credits =
    [
        "Quran Arabic text: Tanzil.net (Uthmani), via AlQuran.cloud.",
        "Quran English translation: Sahih International, via AlQuran.cloud.",
        "Hadith Arabic and English: fawazahmed0/hadith-api (public domain)."
    ];

    private readonly TimeProvider time = clock ?? TimeProvider.System;

    private long NowMs => time.GetUtcNow().ToUnixTimeMilliseconds();

    /// <summary>Imports the starter bundle once, on first run, so the app works offline.</summary>
    public async Task<int> SeedIfNeededAsync(CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        if (state.SeededAtMs is not null)
        {
            return 0;
        }

        var added = await AddAsync(ReadSeedBundle(), "seed", ct);
        state.SeededAtMs = NowMs;
        await db.SaveChangesAsync(ct);
        return added;
    }

    /// <summary>Adds candidates whose SourceKey is not stored yet. Returns how many were added.</summary>
    public async Task<int> AddAsync(IEnumerable<ContentCandidate> candidates, string batch, CancellationToken ct = default)
    {
        var existing = (await db.ContentItems.Select(x => x.SourceKey).ToListAsync(ct)).ToHashSet();
        var now = NowMs;
        var added = 0;
        foreach (var candidate in candidates)
        {
            if (!ContentTypes.IsValid(candidate.Type) || !existing.Add(candidate.SourceKey))
            {
                continue;
            }

            db.ContentItems.Add(new ContentItem
            {
                Type = candidate.Type,
                SourceKey = candidate.SourceKey,
                Ref = candidate.Ref,
                Source = candidate.Source,
                Grade = candidate.Grade,
                Arabic = candidate.Arabic,
                English = candidate.English,
                Batch = batch,
                AddedAtMs = now
            });
            added += 1;
        }

        await db.SaveChangesAsync(ct);
        return added;
    }

    public async Task<IReadOnlySet<string>> GetSourceKeysAsync(string type, CancellationToken ct = default) =>
        (await db.ContentItems.Where(x => x.Type == type).Select(x => x.SourceKey).ToListAsync(ct)).ToHashSet();

    /// <summary>Library list: most recently shown first, then never-shown items, newest first.</summary>
    public async Task<IReadOnlyList<ContentItemDto>> ListAsync(string type, bool hidden, CancellationToken ct = default)
    {
        var items = await db.ContentItems
            .AsNoTracking()
            .Where(x => x.Type == type && x.Hidden == hidden)
            .OrderByDescending(x => x.LastShownAtMs.HasValue)
            .ThenByDescending(x => x.LastShownAtMs)
            .ThenByDescending(x => x.AddedAtMs)
            .ThenBy(x => x.Id)
            .ToListAsync(ct);

        return items.Select(ToDto).ToList();
    }

    /// <summary>Everything that may appear in ambient mode (not removed by the user).</summary>
    public async Task<IReadOnlyList<ActiveContentDto>> ActiveAsync(CancellationToken ct = default) =>
        await db.ContentItems
            .AsNoTracking()
            .Where(x => !x.Hidden)
            .OrderBy(x => x.Id)
            .Select(x => new ActiveContentDto(x.Id, x.Type, x.Ref, x.Grade, x.Arabic, x.English, x.LastShownAtMs))
            .ToListAsync(ct);

    public async Task<bool> MarkShownAsync(int id, CancellationToken ct = default)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null)
        {
            return false;
        }

        item.LastShownAtMs = NowMs;
        item.ShowCount += 1;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<ContentItemDto?> SetHiddenAsync(int id, bool hidden, CancellationToken ct = default)
    {
        var item = await db.ContentItems.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null)
        {
            return null;
        }

        item.Hidden = hidden;
        item.HiddenAtMs = hidden ? NowMs : null;
        await db.SaveChangesAsync(ct);
        return ToDto(item);
    }

    public async Task<ContentStatusDto> StatusAsync(bool refreshing, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        var counts = await db.ContentItems
            .AsNoTracking()
            .GroupBy(x => new { x.Type, x.Hidden })
            .Select(g => new { g.Key.Type, g.Key.Hidden, Count = g.Count() })
            .ToListAsync(ct);

        int Count(string type) => counts.Where(c => c.Type == type && !c.Hidden).Sum(c => c.Count);

        return new ContentStatusDto(
            Count(ContentTypes.Quran),
            Count(ContentTypes.Hadith),
            counts.Where(c => c.Hidden).Sum(c => c.Count),
            state.LastRefreshAtMs,
            state.LastAttemptAtMs,
            state.LastResult,
            state.LastAddedQuran,
            state.LastAddedHadith,
            NextDueAt(state),
            refreshing,
            Credits);
    }

    public static long NextDueAt(ContentState state) =>
        (state.LastRefreshAtMs ?? 0) + (long)RefreshInterval.TotalMilliseconds;

    public async Task<ContentState> GetStateAsync(CancellationToken ct = default)
    {
        var state = await db.ContentStates.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (state is null)
        {
            state = new ContentState { Id = 1 };
            db.ContentStates.Add(state);
            await db.SaveChangesAsync(ct);
        }

        return state;
    }

    public async Task RecordRefreshAsync(bool success, string result, int addedQuran, int addedHadith, CancellationToken ct = default)
    {
        var state = await GetStateAsync(ct);
        state.LastAttemptAtMs = NowMs;
        state.LastResult = result;
        if (success)
        {
            state.LastRefreshAtMs = NowMs;
            state.LastAddedQuran = addedQuran;
            state.LastAddedHadith = addedHadith;
        }

        await db.SaveChangesAsync(ct);
    }

    private static ContentItemDto ToDto(ContentItem x) => new(
        x.Id, x.Type, x.Ref, x.Source, x.Grade, x.Arabic, x.English, x.Batch,
        x.AddedAtMs, x.LastShownAtMs, x.ShowCount, x.Hidden);

    /// <summary>The starter set built by tools/content/build-content.mjs, embedded in this assembly.</summary>
    public static IReadOnlyList<ContentCandidate> ReadSeedBundle()
    {
        using var stream = OpenResource("seed-content.json");
        using var doc = JsonDocument.Parse(stream);
        var result = new List<ContentCandidate>();
        foreach (var group in new[] { "quran", "hadith" })
        {
            foreach (var item in doc.RootElement.GetProperty(group).EnumerateArray())
            {
                result.Add(new ContentCandidate(
                    item.GetProperty("type").GetString()!,
                    item.GetProperty("key").GetString()!,
                    item.GetProperty("ref").GetString()!,
                    item.GetProperty("source").GetString()!,
                    item.TryGetProperty("grade", out var grade) ? grade.GetString() : null,
                    item.GetProperty("ar").GetString()!,
                    item.GetProperty("en").GetString()!));
            }
        }

        return result;
    }

    internal static Stream OpenResource(string fileName)
    {
        var assembly = typeof(ContentService).Assembly;
        var name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        return assembly.GetManifestResourceStream(name)!;
    }
}
