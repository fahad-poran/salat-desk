using DeskDashboard.Core.Data.Entities;

namespace DeskDashboard.Core.Prayer;

/// <summary>One Jamaat warning to show as a phone notification. Times are Unix milliseconds.</summary>
public sealed record PlannedAlert(
    string Id,
    string Key,
    string Name,
    long JamaatAt,
    long WarnAt,
    int Minutes,
    string JamaatLabel);

/// <summary>
/// Works out the Jamaat warnings for the coming days, from the stored settings, timetables and alert
/// preferences, without the dashboard page. The phone schedules these with the OS, so they arrive even when
/// the app has not been opened (and are planned again on boot, on each alarm and once a day).
/// Same rules as the on-screen warning (jamaat-state.js warningMsFor): the warning starts the chosen minutes
/// before the Jamaat, but never before the Waqt.
/// </summary>
public static class JamaatAlertPlanner
{
    public const int DefaultDays = 7;

    public static IReadOnlyList<PlannedAlert> Plan(
        PrayerSettings settings,
        PrayerTimeCalculator calculator,
        IReadOnlyList<MosqueTimetable> timetables,
        IReadOnlyDictionary<string, AlertPreferenceDto> preferences,
        DateTimeOffset nowUtc,
        int days = DefaultDays)
    {
        var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, timeZone).DateTime);
        var horizon = nowUtc.AddDays(days);
        var ordered = timetables.OrderBy(t => t.EffectiveFrom).ToList();
        var alerts = new List<PlannedAlert>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Start a day early: yesterday's Isha Jamaat can fall after midnight.
        for (var date = today.AddDays(-1); date <= today.AddDays(days); date = date.AddDays(1))
        {
            var timetable = ordered.LastOrDefault(t => t.EffectiveFrom <= date);
            if (timetable is null)
            {
                continue;
            }

            var day = calculator.Calculate(settings, date);
            var windows = JamaatSchedule.Build(day, timeZone);

            foreach (var key in PrayerService.PrayerKeys)
            {
                if (!preferences.TryGetValue(key, out var pref) || !pref.Notify)
                {
                    continue;
                }

                var window = windows[key];
                var resolution = JamaatResolver.Resolve(timetable, key, date, window, timeZone);
                if (resolution is not { Valid: true, At: { } jamaatAt })
                {
                    continue;
                }

                var wanted = TimeSpan.FromMinutes(Math.Clamp(pref.Minutes, 1, 120));
                var sinceWaqt = jamaatAt - window.Start;
                var warning = sinceWaqt > TimeSpan.Zero && sinceWaqt < wanted ? sinceWaqt : wanted;
                var warnAt = jamaatAt - warning;
                var id = $"{key}|{jamaatAt.ToUnixTimeMilliseconds()}";

                if (warnAt <= nowUtc || warnAt > horizon || !seen.Add(id))
                {
                    continue;
                }

                alerts.Add(new PlannedAlert(
                    id,
                    key,
                    window.Name,
                    jamaatAt.ToUnixTimeMilliseconds(),
                    warnAt.ToUnixTimeMilliseconds(),
                    Math.Max(1, (int)Math.Round(warning.TotalMinutes, MidpointRounding.AwayFromZero)),
                    JamaatSchedule.FormatTime(jamaatAt, timeZone)));
            }
        }

        return alerts.OrderBy(a => a.WarnAt).ToList();
    }
}
