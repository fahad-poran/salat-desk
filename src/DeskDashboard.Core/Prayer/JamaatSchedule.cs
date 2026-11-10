using System.Globalization;
using DeskDashboard.Core.Data.Entities;

namespace DeskDashboard.Core.Prayer;

public sealed record JamaatWindow(
    string Key,
    string Name,
    DateTimeOffset Start,
    DateTimeOffset End,
    string EndLabel,
    bool SpansMidnight);

public sealed record JamaatValidation(
    bool IsValid,
    DateTimeOffset JamaatAt,
    string? Error,
    JamaatWindow Window);

public static class JamaatSchedule
{
    public const int ConflictScanDays = 30;

    public static IReadOnlyDictionary<string, JamaatWindow> Build(PrayerDayResult day, TimeZoneInfo timeZone)
    {
        var moments = day.Moments.ToDictionary(moment => moment.Key, StringComparer.OrdinalIgnoreCase);

        return new Dictionary<string, JamaatWindow>(StringComparer.OrdinalIgnoreCase)
        {
            ["fajr"] = Create("fajr", "Fajr", moments["fajr"].At, moments["sunrise"].At, "Sunrise", timeZone),
            ["dhuhr"] = Create("dhuhr", "Dhuhr", moments["dhuhr"].At, moments["asr"].At, "Asr", timeZone),
            ["asr"] = Create("asr", "Asr", moments["asr"].At, moments["maghrib"].At, "Maghrib", timeZone),
            ["maghrib"] = Create("maghrib", "Maghrib", moments["maghrib"].At, moments["isha"].At, "Isha", timeZone),
            ["isha"] = Create("isha", "Isha", moments["isha"].At, day.TomorrowFajr.At, "the next day's Fajr", timeZone)
        };
    }

    public static JamaatValidation Validate(
        string prayerKey,
        TimeOnly? jamaatTime,
        DateOnly date,
        JamaatWindow window,
        TimeZoneInfo timeZone)
    {
        if (jamaatTime is null)
        {
            throw new ArgumentNullException(nameof(jamaatTime));
        }

        var local = date.ToDateTime(jamaatTime.Value);
        var jamaatAt = new DateTimeOffset(local, timeZone.GetUtcOffset(local));

        if (window.SpansMidnight && jamaatAt < window.Start)
        {
            var nextDay = local.AddDays(1);
            jamaatAt = new DateTimeOffset(nextDay, timeZone.GetUtcOffset(nextDay));
        }

        if (jamaatAt < window.Start)
        {
            return new JamaatValidation(
                false,
                jamaatAt,
                $"{window.Name} Jamaat must be at or after the {window.Name} start ({FormatTime(window.Start, timeZone)}).",
                window);
        }

        if (jamaatAt >= window.End)
        {
            return new JamaatValidation(
                false,
                jamaatAt,
                $"{window.Name} Jamaat must be before {window.EndLabel} ({FormatTime(window.End, timeZone)}).",
                window);
        }

        return new JamaatValidation(true, jamaatAt, null, window);
    }

    public static string FormatTime(DateTimeOffset at, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(at, timeZone).ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly time) =>
        time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    public static string ToInputTime(DateTimeOffset at, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(at, timeZone).ToString("HH:mm", CultureInfo.InvariantCulture);

    public static string DescribeRange(JamaatWindow window, TimeZoneInfo timeZone)
    {
        var start = FormatTime(window.Start, timeZone);
        var latest = FormatTime(window.End.AddMinutes(-1), timeZone);
        return window.SpansMidnight
            ? $"{start} – {latest} (next day)"
            : $"{start} – {latest}";
    }

    public static IReadOnlyList<JamaatConflictDto> FindConflicts(
        PrayerSettings settings,
        PrayerTimeCalculator calculator,
        IReadOnlyList<MosqueTimetable> timetables,
        TimeZoneInfo timeZone,
        DateOnly today)
    {
        var conflicts = new List<JamaatConflictDto>();
        var horizon = today.AddDays(ConflictScanDays);

        for (var index = 0; index < timetables.Count; index++)
        {
            var timetable = timetables[index];
            var nextEffectiveFrom = index + 1 < timetables.Count
                ? timetables[index + 1].EffectiveFrom
                : (DateOnly?)null;

            var scanStart = timetable.EffectiveFrom > today ? timetable.EffectiveFrom : today;
            var scanEnd = nextEffectiveFrom.HasValue
                ? nextEffectiveFrom.Value.AddDays(-1)
                : horizon;

            if (scanEnd > horizon)
            {
                scanEnd = horizon;
            }

            if (scanEnd < scanStart)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var date = scanStart; date <= scanEnd; date = date.AddDays(1))
            {
                var day = calculator.Calculate(settings, date);
                var windows = Build(day, timeZone);

                foreach (var (key, time) in Entries(timetable))
                {
                    if (time is null)
                    {
                        continue;
                    }

                    var window = windows[key];
                    var validation = Validate(key, time, date, window, timeZone);
                    if (validation.IsValid)
                    {
                        continue;
                    }

                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    var range = DescribeRange(window, timeZone);
                    conflicts.Add(new JamaatConflictDto(
                        timetable.EffectiveFrom,
                        date,
                        key,
                        window.Name,
                        FormatTime(time.Value),
                        range,
                        $"{window.Name} Jamaat {FormatTime(time.Value)} is outside the allowed window. Correct this Jamaat time in the timetable."));
                }
            }
        }

        return conflicts;
    }

    private static IEnumerable<(string Key, TimeOnly? Time)> Entries(MosqueTimetable timetable)
    {
        yield return ("fajr", timetable.Fajr);
        yield return ("dhuhr", timetable.Dhuhr);
        yield return ("asr", timetable.Asr);
        yield return ("maghrib", timetable.Maghrib);
        yield return ("isha", timetable.Isha);
    }

    private static JamaatWindow Create(
        string key,
        string name,
        DateTimeOffset start,
        DateTimeOffset end,
        string endLabel,
        TimeZoneInfo timeZone)
    {
        var truncatedStart = TruncateToMinute(start, timeZone);
        var truncatedEnd = TruncateToMinute(end, timeZone);
        var spansMidnight = TimeZoneInfo.ConvertTime(truncatedEnd, timeZone).Date
            > TimeZoneInfo.ConvertTime(truncatedStart, timeZone).Date;

        return new JamaatWindow(key, name, truncatedStart, truncatedEnd, endLabel, spansMidnight);
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value, TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(value, timeZone);
        var truncated = new DateTime(
            local.Year,
            local.Month,
            local.Day,
            local.Hour,
            local.Minute,
            0,
            DateTimeKind.Unspecified);

        return new DateTimeOffset(truncated, timeZone.GetUtcOffset(truncated));
    }
}
