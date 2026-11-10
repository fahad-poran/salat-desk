using System.Globalization;
using Batoulapps.Adhan;
using Batoulapps.Adhan.Internal;
using DeskDashboard.Core.Data.Entities;

namespace DeskDashboard.Core.Prayer;

public sealed record PrayerMoment(
    string Key,
    string Name,
    string Time,
    DateTimeOffset At,
    int AdjustmentMinutes,
    bool IsPrayer);

public sealed record PrayerDayResult(
    DateOnly Date,
    string TimeZoneId,
    IReadOnlyList<PrayerMoment> Moments,
    PrayerMoment Next,
    PrayerMoment TomorrowFajr,
    DateTimeOffset ServerNowUtc);

public sealed class PrayerTimeCalculator
{
    private static readonly (string Key, string Name, bool IsPrayer, Func<PrayerTimes, DateTime> Selector)[] Definitions =
    [
        ("fajr", "Fajr", true, times => times.Fajr),
        ("sunrise", "Sunrise", false, times => times.Sunrise),
        ("dhuhr", "Dhuhr", true, times => times.Dhuhr),
        ("asr", "Asr", true, times => times.Asr),
        ("maghrib", "Maghrib", true, times => times.Maghrib),
        ("isha", "Isha", true, times => times.Isha)
    ];

    public PrayerDayResult Calculate(PrayerSettings settings, DateOnly date, DateTimeOffset? nowUtc = null)
    {
        if (!TimeZoneResolver.TryResolve(settings.TimeZoneId, out var timeZone))
        {
            throw new InvalidOperationException($"Unknown time zone '{settings.TimeZoneId}'.");
        }

        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var moments = BuildMoments(settings, date, timeZone);
        var tomorrowFajr = BuildMoments(settings, date.AddDays(1), timeZone).First(moment => moment.Key == "fajr");
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var next = moments.FirstOrDefault(moment => moment.IsPrayer && moment.At > localNow) ?? tomorrowFajr;

        return new PrayerDayResult(date, timeZone.Id, moments, next, tomorrowFajr, now.ToUniversalTime());
    }

    private static IReadOnlyList<PrayerMoment> BuildMoments(PrayerSettings settings, DateOnly date, TimeZoneInfo timeZone)
    {
        var parameters = PrayerMethodCatalog.CreateParameters(settings);
        var prayerTimes = new PrayerTimes(
            new Coordinates(settings.Latitude, settings.Longitude),
            new DateComponents(date.Year, date.Month, date.Day),
            parameters);

        var adjustments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["fajr"] = settings.FajrAdjustment,
            ["sunrise"] = settings.SunriseAdjustment,
            ["dhuhr"] = settings.DhuhrAdjustment,
            ["asr"] = settings.AsrAdjustment,
            ["maghrib"] = settings.MaghribAdjustment,
            ["isha"] = settings.IshaAdjustment
        };

        var result = new List<PrayerMoment>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            var utc = DateTime.SpecifyKind(definition.Selector(prayerTimes), DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, timeZone);
            var at = new DateTimeOffset(local, timeZone.GetUtcOffset(local));

            result.Add(new PrayerMoment(
                definition.Key,
                definition.Name,
                at.ToString("HH:mm", CultureInfo.InvariantCulture),
                at,
                adjustments[definition.Key],
                definition.IsPrayer));
        }

        return result;
    }
}
