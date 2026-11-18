using System.Globalization;
using System.Text.Json;
using DeskDashboard.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace DeskDashboard.Core.Content;

public sealed record RefreshOutcome(bool Ran, bool Success, int AddedQuran, int AddedHadith, string Message);

/// <summary>
/// Weekly download of new Ayat (from the curated pool) and Hadith (from reminder-type books).
/// Runs only when due, never twice at once, and never removes or changes existing content, so a
/// failed or partial refresh leaves the app working offline with what it already has.
/// </summary>
public sealed class ContentRefreshService(
    Func<DashboardDbContext> dbFactory,
    HttpClient http,
    ILogger<ContentRefreshService> logger,
    TimeProvider? clock = null)
{
    public const string QuranApi = "https://api.alquran.cloud/v1/ayah/";
    public const string HadithApi = "https://cdn.jsdelivr.net/gh/fawazahmed0/hadith-api@1/editions/";

    /// <summary>Books (sections) used for hadith: reminders, manners, supplication, faith.</summary>
    public static readonly IReadOnlyList<HadithBook> Books =
    [
        new("bukhari", "Sahih al-Bukhari", "Sahih", [2, 3, 66, 75, 78, 79, 80, 81, 82, 97]),
        new("muslim", "Sahih Muslim", "Sahih", [1, 43, 45, 46, 47, 48, 49, 50, 53, 55]),
        new("nawawi", "An-Nawawi's Forty Hadith", null, [1]),
        new("qudsi", "Forty Hadith Qudsi", null, [1])
    ];

    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private int running;

    public int QuranPerRefresh { get; init; } = 70;
    public int HadithPerRefresh { get; init; } = 200;

    /// <summary>Pause between AlQuran.cloud requests, which has a soft per-second rate limit.</summary>
    public TimeSpan QuranRequestGap { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Set by the host, e.g. the phone app allows downloads only on Wi-Fi.</summary>
    public Func<bool> CanDownload { get; set; } = () => true;

    public bool IsRunning => Volatile.Read(ref running) == 1;

    public async Task<RefreshOutcome> RefreshIfDueAsync(CancellationToken ct = default)
    {
        await using (var db = dbFactory())
        {
            var state = await new ContentService(db, time).GetStateAsync(ct);
            if (time.GetUtcNow().ToUnixTimeMilliseconds() < ContentService.NextDueAt(state))
            {
                await BackfillReferencesIfNeededAsync(ct);
                return new RefreshOutcome(false, true, 0, 0, "Not due yet.");
            }
        }

        return await RefreshAsync(ct);
    }

    /// <summary>
    /// Hadith downloaded before 0.2.0 have no book/hadith reference. Reads the English section files once
    /// (on Wi-Fi) to fill them in, instead of waiting for the next weekly update. Returns how many were filled.
    /// </summary>
    public async Task<int> BackfillReferencesIfNeededAsync(CancellationToken ct = default)
    {
        await using var db = dbFactory();
        var books = Books.Select(b => b.Key + ":").ToArray();
        var missing = await db.ContentItems
            .Where(x => x.Type == ContentTypes.Hadith && x.RefBook == null)
            .Select(x => x.SourceKey)
            .ToListAsync(ct);
        missing = missing.Where(key => books.Any(key.StartsWith)).ToList();
        if (missing.Count == 0 || !CanDownload() || Interlocked.Exchange(ref running, 1) == 1)
        {
            return 0;
        }

        try
        {
            var wanted = missing.ToHashSet();
            var references = new List<ContentCandidate>();
            foreach (var book in Books)
            {
                foreach (var section in book.Sections)
                {
                    try
                    {
                        foreach (var hadith in await FetchSectionAsync($"eng-{book.Key}", section, ct))
                        {
                            var key = $"{book.Key}:{ContentText.ReferenceNumber(hadith.ArabicNumber, hadith.HadithNumber)}";
                            if (hadith.RefBook is not null && hadith.RefNumber is not null && wanted.Contains(key))
                            {
                                references.Add(new ContentCandidate(ContentTypes.Hadith, key, "", book.Name, null, "", "",
                                    hadith.RefBook, hadith.RefNumber));
                            }
                        }
                    }
                    catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
                    {
                        logger.LogWarning(ex, "Could not read {Book} section {Section} for references.", book.Key, section);
                    }
                }
            }

            var filled = await new ContentService(db, time).BackfillReferencesAsync(references, ct);
            logger.LogInformation("Hadith references filled in for {Count} of {Missing} items.", filled, missing.Count);
            return filled;
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    public async Task<RefreshOutcome> RefreshAsync(CancellationToken ct = default)
    {
        if (!CanDownload())
        {
            return new RefreshOutcome(false, false, 0, 0, "Waiting for Wi-Fi.");
        }

        if (Interlocked.Exchange(ref running, 1) == 1)
        {
            return new RefreshOutcome(false, false, 0, 0, "An update is already running.");
        }

        try
        {
            var batch = BatchName(time.GetUtcNow());
            int addedQuran, addedHadith;
            var errors = new List<string>();

            await using (var db = dbFactory())
            {
                var service = new ContentService(db, time);
                var quranKeys = await service.GetSourceKeysAsync(ContentTypes.Quran, ct);
                var quran = await FetchQuranAsync(quranKeys, errors, ct);
                addedQuran = await service.AddAsync(quran, batch, ct);

                var hadithKeys = await service.GetSourceKeysAsync(ContentTypes.Hadith, ct);
                var references = new List<ContentCandidate>();
                var hadith = await FetchHadithAsync(hadithKeys, batch, errors, references, ct);
                addedHadith = await service.AddAsync(hadith, batch, ct);

                // Items stored before 0.2.0 get their book numbers from the section files read anyway.
                await service.BackfillReferencesAsync(references, ct);

                // Success when anything arrived, or nothing failed (the pools may simply be used up).
                var success = addedQuran + addedHadith > 0 || errors.Count == 0;
                var message = $"Added {addedQuran} ayat and {addedHadith} hadith." +
                    (errors.Count > 0 ? $" {errors.Count} download(s) failed." : "");
                await service.RecordRefreshAsync(success, message, addedQuran, addedHadith, ct);
                logger.LogInformation("Content refresh: {Message}", message);
                return new RefreshOutcome(true, success, addedQuran, addedHadith, message);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Content refresh failed.");
            await using var db = dbFactory();
            await new ContentService(db, time).RecordRefreshAsync(false, "Update failed. Will retry later.", 0, 0, CancellationToken.None);
            return new RefreshOutcome(true, false, 0, 0, "Update failed. Will retry later.");
        }
        finally
        {
            Volatile.Write(ref running, 0);
        }
    }

    public static string BatchName(DateTimeOffset now) =>
        $"{ISOWeek.GetYear(now.UtcDateTime)}-W{ISOWeek.GetWeekOfYear(now.UtcDateTime):00}";

    private async Task<List<ContentCandidate>> FetchQuranAsync(
        IReadOnlySet<string> existing,
        List<string> errors,
        CancellationToken ct)
    {
        var result = new List<ContentCandidate>();
        var attempts = 0;
        var first = true;
        foreach (var reference in QuranPool.All.Where(r => !existing.Contains(r.SourceKey)))
        {
            if (result.Count >= QuranPerRefresh || attempts >= QuranPerRefresh + 30)
            {
                break;
            }

            attempts += 1;
            try
            {
                var arabic = new List<string>();
                var english = new List<string>();
                string? surahName = null;
                for (var ayah = reference.First; ayah <= reference.Last; ayah += 1)
                {
                    if (!first)
                    {
                        await Task.Delay(QuranRequestGap, time, ct);
                    }

                    first = false;
                    var (ar, en, name) = await FetchAyahAsync(reference.Surah, ayah, ct);
                    surahName ??= name;
                    var text = ContentText.StripBasmala(ar, reference.Surah, ayah);
                    arabic.Add(reference.First == reference.Last ? text : $"{text} ﴿{ContentText.ArabicDigits(ayah)}﴾");
                    english.Add(ContentText.Clean(en));
                }

                var candidate = new ContentCandidate(
                    ContentTypes.Quran,
                    reference.SourceKey,
                    $"{surahName} {reference.Surah}:{reference.First}{(reference.Last != reference.First ? $"–{reference.Last}" : "")}",
                    "Quran",
                    null,
                    ContentText.Clean(string.Join(' ', arabic)),
                    string.Join(' ', english));

                if (candidate.Arabic.Length <= ContentText.QuranArabicMax &&
                    candidate.English.Length <= ContentText.QuranEnglishMax)
                {
                    result.Add(candidate);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
            {
                errors.Add($"Quran {reference.Label}");
                logger.LogWarning(ex, "Could not download Quran {Reference}.", reference.Label);
            }
        }

        return result;
    }

    private async Task<(string Arabic, string English, string SurahName)> FetchAyahAsync(int surah, int ayah, CancellationToken ct)
    {
        using var response = await http.GetAsync($"{QuranApi}{surah}:{ayah}/editions/quran-uthmani,en.sahih", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        string? arabic = null, english = null, name = null;
        foreach (var entry in doc.RootElement.GetProperty("data").EnumerateArray())
        {
            var edition = entry.GetProperty("edition").GetProperty("identifier").GetString();
            if (entry.GetProperty("surah").GetProperty("number").GetInt32() != surah ||
                entry.GetProperty("numberInSurah").GetInt32() != ayah)
            {
                throw new InvalidOperationException($"Unexpected ayah in response for {surah}:{ayah}.");
            }

            name ??= entry.GetProperty("surah").GetProperty("englishName").GetString();
            if (edition == "quran-uthmani")
            {
                arabic = entry.GetProperty("text").GetString();
            }
            else if (edition == "en.sahih")
            {
                english = entry.GetProperty("text").GetString();
            }
        }

        return (arabic ?? throw new KeyNotFoundException("Arabic text missing."),
                english ?? throw new KeyNotFoundException("English text missing."),
                name ?? "Surah " + surah);
    }

    private async Task<List<ContentCandidate>> FetchHadithAsync(
        IReadOnlySet<string> existing,
        string batch,
        List<string> errors,
        List<ContentCandidate> references,
        CancellationToken ct)
    {
        var candidates = new List<ContentCandidate>();
        var seen = new HashSet<string>(existing);
        foreach (var book in Books)
        {
            foreach (var section in book.Sections)
            {
                try
                {
                    var english = await FetchSectionAsync($"eng-{book.Key}", section, ct);
                    var arabic = (await FetchSectionAsync($"ara-{book.Key}", section, ct))
                        .GroupBy(h => h.HadithNumber)
                        .ToDictionary(g => g.Key, g => g.First());

                    foreach (var hadith in english)
                    {
                        var refKey = $"{book.Key}:{ContentText.ReferenceNumber(hadith.ArabicNumber, hadith.HadithNumber)}";
                        if (hadith.RefBook is not null && hadith.RefNumber is not null && existing.Contains(refKey))
                        {
                            references.Add(new ContentCandidate(ContentTypes.Hadith, refKey, "", book.Name, null, "", "",
                                hadith.RefBook, hadith.RefNumber));
                        }

                        var en = ContentText.CleanHadithEnglish(hadith.Text);
                        var ar = arabic.TryGetValue(hadith.HadithNumber, out var match) ? ContentText.Clean(match.Text) : "";
                        if (en.Length < ContentText.HadithEnglishMin || ar.Length == 0 ||
                            en.Length > ContentText.HadithEnglishMax || ar.Length > ContentText.HadithArabicMax ||
                            ContentText.IsDependentNarration(en) || ContentText.IsWeakGrade(hadith.Grades))
                        {
                            continue;
                        }

                        var number = ContentText.ReferenceNumber(hadith.ArabicNumber, hadith.HadithNumber);
                        var key = $"{book.Key}:{number}";
                        if (!seen.Add(key))
                        {
                            continue;
                        }

                        candidates.Add(new ContentCandidate(
                            ContentTypes.Hadith, key, $"{book.Name} {number}", book.Name, book.Grade, ar, en,
                            hadith.RefBook, hadith.RefNumber));
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
                {
                    errors.Add($"{book.Name} book {section}");
                    logger.LogWarning(ex, "Could not download {Book} section {Section}.", book.Key, section);
                }
            }
        }

        // A fresh, varied mix each week; the seed makes one week's choice reproducible.
        var random = new Random(StableSeed(batch));
        return candidates.OrderBy(_ => random.Next()).Take(HadithPerRefresh).ToList();
    }

    private async Task<List<RawHadith>> FetchSectionAsync(string edition, int section, CancellationToken ct)
    {
        // The hadith-api README asks clients to fall back from .min.json to .json: jsDelivr
        // occasionally refuses one of the two (seen: 403 for ara-bukhari section 66 .min.json).
        var response = await http.GetAsync($"{HadithApi}{edition}/sections/{section}.min.json", ct);
        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            response = await http.GetAsync($"{HadithApi}{edition}/sections/{section}.json", ct);
        }

        using var _ = response;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var list = new List<RawHadith>();
        foreach (var item in doc.RootElement.GetProperty("hadiths").EnumerateArray())
        {
            var number = item.GetProperty("hadithnumber").ValueKind == JsonValueKind.Number
                ? (int)item.GetProperty("hadithnumber").GetDouble()
                : int.Parse(item.GetProperty("hadithnumber").GetString()!, CultureInfo.InvariantCulture);
            var arabicNumber = item.TryGetProperty("arabicnumber", out var an)
                ? an.ValueKind == JsonValueKind.Number ? an.GetDouble().ToString(CultureInfo.InvariantCulture) : an.GetString()
                : null;
            var grades = item.TryGetProperty("grades", out var g) && g.ValueKind == JsonValueKind.Array
                ? g.EnumerateArray().Select(x => x.TryGetProperty("grade", out var v) ? v.GetString() ?? "" : "").ToList()
                : [];
            int? refBook = null, refNumber = null;
            if (item.TryGetProperty("reference", out var reference) && reference.ValueKind == JsonValueKind.Object &&
                reference.TryGetProperty("book", out var b) && b.ValueKind == JsonValueKind.Number &&
                reference.TryGetProperty("hadith", out var h) && h.ValueKind == JsonValueKind.Number)
            {
                refBook = (int)b.GetDouble();
                refNumber = (int)h.GetDouble();
            }

            list.Add(new RawHadith(number, arabicNumber, item.GetProperty("text").GetString() ?? "", grades, refBook, refNumber));
        }

        return list;
    }

    private static int StableSeed(string text)
    {
        var hash = 17;
        foreach (var c in text)
        {
            hash = unchecked(hash * 31 + c);
        }

        return hash;
    }

    public sealed record HadithBook(string Key, string Name, string? Grade, IReadOnlyList<int> Sections);

    private sealed record RawHadith(
        int HadithNumber,
        string? ArabicNumber,
        string Text,
        IReadOnlyList<string> Grades,
        int? RefBook,
        int? RefNumber);
}
