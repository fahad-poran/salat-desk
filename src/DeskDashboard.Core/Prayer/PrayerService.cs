using System.Globalization;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeskDashboard.Core.Prayer;

public sealed class PrayerService(
    DashboardDbContext db,
    PrayerTimeCalculator calculator,
    ILogger<PrayerService> logger)
{
    public const string Disclaimer =
        "Calculated times are astronomical estimates using the selected method, angles, and location. They are not a religious authority. Verify all prayer start times against your local mosque. Jamaat times are entered manually.";

    public async Task<PrayerSettings> GetSettingsAsync(CancellationToken ct) =>
        await db.PrayerSettings
            .AsNoTracking()
            .OrderBy(x => x.Id)
            .FirstAsync(ct);

    public PrayerSettingsDto ToDto(PrayerSettings settings) => new(
        settings.Country,
        settings.City,
        settings.Latitude,
        settings.Longitude,
        settings.TimeZoneId,
        settings.MethodKey,
        settings.FajrAngle,
        settings.IshaAngle,
        settings.AsrMadhab,
        settings.FajrAdjustment,
        settings.SunriseAdjustment,
        settings.DhuhrAdjustment,
        settings.AsrAdjustment,
        settings.MaghribAdjustment,
        settings.IshaAdjustment);

    public async Task<(bool Ok, Dictionary<string, string[]> Errors, PrayerSettings? Settings)> UpdateSettingsAsync(
        PrayerSettingsDto input,
        CancellationToken ct)
    {
        var errors = ValidateSettings(input);
        if (errors.Count > 0)
        {
            return (false, errors, null);
        }

        var settings = await db.PrayerSettings.OrderBy(x => x.Id).FirstAsync(ct);
        ApplySettings(settings, input);

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Prayer settings updated for {City}, {Country}.", settings.City, settings.Country);

        return (true, [], settings);
    }

    /// <summary>
    /// Saves the whole Settings dialog at once: prayer settings, (optionally) the Jamaat timetable and the alert
    /// preferences. Everything is validated first, against the new settings, and written in one SaveChanges, so a
    /// failure leaves nothing half-saved. Error keys: settings fields as-is, "timetable.*" and "alerts.*".
    /// </summary>
    public async Task<(bool Ok, Dictionary<string, string[]> Errors, SettingsSaveResponse? Result)> SaveAllAsync(
        SettingsSaveInput input,
        CancellationToken ct)
    {
        if (input.Prayer is null)
        {
            return (false, new Dictionary<string, string[]> { ["prayer"] = ["Prayer settings are required."] }, null);
        }

        var errors = ValidateSettings(input.Prayer);
        var settingsValid = errors.Count == 0;

        TimetableValues? timetable = null;
        if (input.Timetable is not null)
        {
            PrayerSettings? candidate = null;
            if (settingsValid)
            {
                candidate = new PrayerSettings();
                ApplySettings(candidate, input.Prayer);
            }

            var (timetableErrors, values) = ValidateTimetable(input.Timetable, candidate);
            foreach (var (key, messages) in timetableErrors)
            {
                errors["timetable." + key] = messages;
            }

            timetable = values;
        }

        foreach (var (key, messages) in ValidateAlerts(input.Alerts))
        {
            errors["alerts." + key] = messages;
        }

        if (errors.Count > 0)
        {
            return (false, errors, null);
        }

        var settings = await db.PrayerSettings.OrderBy(x => x.Id).FirstAsync(ct);
        ApplySettings(settings, input.Prayer);

        MosqueTimetable? saved = null;
        if (input.Timetable is not null && timetable is not null)
        {
            saved = await ApplyTimetableAsync(input.Timetable, timetable, ct);
        }

        if (input.Alerts is not null)
        {
            await ApplyAlertsAsync(input.Alerts, ct);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Settings saved for {City}, {Country}.", settings.City, settings.Country);

        saved ??= await GetActiveTimetableAsync(
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneResolver.Resolve(settings.TimeZoneId)).DateTime),
            ct);

        return (true, [], new SettingsSaveResponse(
            ToDto(settings),
            saved is null ? null : ToDto(saved),
            await GetAlertPreferencesAsync(ct),
            await GetConflictsAsync(ct)));
    }

    public static readonly string[] PrayerKeys = ["fajr", "dhuhr", "asr", "maghrib", "isha"];

    /// <summary>The Jamaat warnings for the coming days, for the phone to schedule (see JamaatAlertPlanner).</summary>
    public async Task<IReadOnlyList<PlannedAlert>> PlanAlertsAsync(
        DateTimeOffset nowUtc,
        int days = JamaatAlertPlanner.DefaultDays,
        CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        var timetables = await db.MosqueTimetables.AsNoTracking().ToListAsync(ct);
        var preferences = await GetAlertPreferencesAsync(ct);
        return JamaatAlertPlanner.Plan(settings, calculator, timetables, preferences, nowUtc, days);
    }

    public async Task<IReadOnlyDictionary<string, AlertPreferenceDto>> GetAlertPreferencesAsync(CancellationToken ct)
    {
        var stored = await db.AlertPreferences.AsNoTracking().ToListAsync(ct);
        return PrayerKeys.ToDictionary(
            key => key,
            key => stored.FirstOrDefault(x => x.Key == key) is { } pref
                ? new AlertPreferenceDto(pref.Notify, pref.Minutes)
                : new AlertPreferenceDto(true, 10));
    }

    private static Dictionary<string, string[]> ValidateAlerts(IReadOnlyDictionary<string, AlertPreferenceDto>? alerts)
    {
        var errors = new Dictionary<string, string[]>();
        if (alerts is null)
        {
            return errors;
        }

        foreach (var (key, pref) in alerts)
        {
            if (!PrayerKeys.Contains(key))
            {
                errors[key] = ["Unknown prayer."];
            }
            else if (pref is null || pref.Minutes is < 1 or > 120)
            {
                errors[key] = ["Warning minutes must be between 1 and 120."];
            }
        }

        return errors;
    }

    private async Task ApplyAlertsAsync(IReadOnlyDictionary<string, AlertPreferenceDto> alerts, CancellationToken ct)
    {
        var stored = await db.AlertPreferences.ToListAsync(ct);
        foreach (var (key, pref) in alerts)
        {
            var row = stored.FirstOrDefault(x => x.Key == key);
            if (row is null)
            {
                row = new AlertPreference { Key = key };
                db.AlertPreferences.Add(row);
            }

            row.Notify = pref.Notify;
            row.Minutes = pref.Minutes;
        }
    }

    private static void ApplySettings(PrayerSettings settings, PrayerSettingsDto input)
    {
        settings.Country = input.Country.Trim();
        settings.City = input.City.Trim();
        settings.Latitude = input.Latitude;
        settings.Longitude = input.Longitude;
        settings.TimeZoneId = input.TimeZoneId.Trim();
        settings.MethodKey = PrayerMethodCatalog.GetOption(input.MethodKey).Key;
        settings.FajrAngle = input.FajrAngle;
        settings.IshaAngle = input.IshaAngle;
        settings.AsrMadhab = string.Equals(input.AsrMadhab.Trim(), "Hanafi", StringComparison.OrdinalIgnoreCase)
            ? "Hanafi"
            : "Standard";
        settings.FajrAdjustment = input.FajrAdjustment;
        settings.SunriseAdjustment = input.SunriseAdjustment;
        settings.DhuhrAdjustment = input.DhuhrAdjustment;
        settings.AsrAdjustment = input.AsrAdjustment;
        settings.MaghribAdjustment = input.MaghribAdjustment;
        settings.IshaAdjustment = input.IshaAdjustment;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public async Task<PrayerTodayResponse> BuildTodayAsync(DateOnly? requestedDate, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
        var targetDate = requestedDate ??
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);

        var day = calculator.Calculate(settings, targetDate);
        var timetable = await GetActiveTimetableAsync(targetDate, ct);
        var windows = JamaatSchedule.Build(day, timeZone);
        var times = MapTimes(day, timetable, targetDate, timeZone, windows);
        var nextIsTomorrow = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(day.Next.At, timeZone).DateTime) > targetDate;

        var tomorrowDate = targetDate.AddDays(1);
        var tomorrowDay = calculator.Calculate(settings, tomorrowDate);
        var tomorrowTimetable = await GetActiveTimetableAsync(tomorrowDate, ct);
        var tomorrowFajr = MapMoment(
            tomorrowDay.Moments.First(moment => moment.Key == "fajr"),
            tomorrowTimetable,
            tomorrowDate,
            timeZone,
            JamaatSchedule.Build(tomorrowDay, timeZone));

        var previousDate = targetDate.AddDays(-1);
        var previousDay = calculator.Calculate(settings, previousDate);
        var previousTimetable = await GetActiveTimetableAsync(previousDate, ct);
        var previousIsha = MapMoment(
            previousDay.Moments.First(moment => moment.Key == "isha"),
            previousTimetable,
            previousDate,
            timeZone,
            JamaatSchedule.Build(previousDay, timeZone));

        return new PrayerTodayResponse(
            targetDate,
            timeZone.Id,
            new PrayerLocationDto(settings.Country, settings.City, settings.Latitude, settings.Longitude),
            ToDto(settings),
            PrayerMethodCatalog.Options,
            times,
            new NextPrayerDto(day.Next.Key, day.Next.Name, day.Next.Time, day.Next.At, nextIsTomorrow),
            tomorrowFajr,
            previousIsha,
            day.ServerNowUtc,
            timetable is null ? null : ToDto(timetable),
            ConflictsFrom(times, timetable, targetDate),
            Disclaimer,
            await GetAlertPreferencesAsync(ct));
    }

    public Task<MosqueTimetable?> GetActiveTimetableAsync(DateOnly date, CancellationToken ct) =>
        db.MosqueTimetables
            .AsNoTracking()
            .Where(x => x.EffectiveFrom <= date)
            .OrderByDescending(x => x.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

    public async Task<MosqueTimetableResponse> GetTimetableAsync(DateOnly? requestedDate, CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
        var date = requestedDate ??
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        var timetable = await GetActiveTimetableAsync(date, ct);
        var day = calculator.Calculate(settings, date);
        var times = MapTimes(day, timetable, date, timeZone, JamaatSchedule.Build(day, timeZone));

        return new MosqueTimetableResponse(
            timetable is null ? null : ToDto(timetable),
            ConflictsFrom(times, timetable, date));
    }

    public async Task<IReadOnlyList<JamaatConflictDto>> GetConflictsAsync(CancellationToken ct)
    {
        var settings = await GetSettingsAsync(ct);
        var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).DateTime);
        var timetables = await db.MosqueTimetables
            .AsNoTracking()
            .OrderBy(x => x.EffectiveFrom)
            .ToListAsync(ct);

        return JamaatSchedule.FindConflicts(settings, calculator, timetables, timeZone, today);
    }

    public async Task<(bool Ok, Dictionary<string, string[]> Errors, MosqueTimetable? Timetable)> UpsertTimetableAsync(
        MosqueTimetableInput input,
        CancellationToken ct)
    {
        var (errors, values) = ValidateTimetable(input, await GetSettingsAsync(ct));
        if (errors.Count > 0)
        {
            return (false, errors, null);
        }

        var timetable = await ApplyTimetableAsync(input, values, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Jamaat timetable saved effective {EffectiveFrom}.", timetable.EffectiveFrom);

        return (true, [], timetable);
    }

    private sealed record TimetableValues(
        IReadOnlyDictionary<string, TimeOnly?> Times,
        IReadOnlyDictionary<string, JamaatRule> Rules);

    /// <summary>
    /// Parses and checks a timetable. Fixed times are checked against the windows of the effective date, using
    /// <paramref name="settings"/> (null when the settings themselves are invalid: then only the format is checked).
    /// Error keys: "fajr" … for times, "fajrRule" … for rules.
    /// </summary>
    private (Dictionary<string, string[]> Errors, TimetableValues Values) ValidateTimetable(
        MosqueTimetableInput input,
        PrayerSettings? settings)
    {
        var errors = new Dictionary<string, string[]>();
        var times = new Dictionary<string, TimeOnly?>
        {
            ["fajr"] = ParseTime(input.Fajr, "fajr", errors),
            ["dhuhr"] = ParseTime(input.Dhuhr, "dhuhr", errors),
            ["asr"] = ParseTime(input.Asr, "asr", errors),
            ["maghrib"] = ParseTime(input.Maghrib, "maghrib", errors),
            ["isha"] = ParseTime(input.Isha, "isha", errors)
        };

        var rules = new Dictionary<string, JamaatRule>();
        foreach (var key in PrayerKeys)
        {
            var rule = input.Rules is not null && input.Rules.TryGetValue(key, out var given) && given is not null
                ? given
                : JamaatRule.Default;
            var ruleErrors = JamaatRule.Errors(rule).ToArray();
            if (ruleErrors.Length > 0)
            {
                errors[key + "Rule"] = ruleErrors;
            }

            rules[key] = JamaatRule.Normalize(rule);
        }

        if (input.Note?.Length > 200)
        {
            errors["note"] = ["Note must be 200 characters or fewer."];
        }

        if (input.EffectiveFrom == default)
        {
            errors["effectiveFrom"] = ["Choose the date this timetable becomes effective."];
        }

        if (errors.Count == 0 && settings is not null)
        {
            var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
            var day = calculator.Calculate(settings, input.EffectiveFrom);
            var windows = JamaatSchedule.Build(day, timeZone);

            foreach (var key in PrayerKeys)
            {
                // Only a fixed time is the user's own number to check; the other modes always land in the window.
                if (rules[key].Mode != JamaatRule.Fixed || times[key] is not { } time)
                {
                    continue;
                }

                var validation = JamaatSchedule.Validate(key, time, input.EffectiveFrom, windows[key], timeZone);
                if (!validation.IsValid)
                {
                    errors[key] =
                    [
                        $"{validation.Error} Allowed Jamaat window: {JamaatSchedule.DescribeRange(windows[key], timeZone)}."
                    ];
                }
            }
        }

        return (errors, new TimetableValues(times, rules));
    }

    private async Task<MosqueTimetable> ApplyTimetableAsync(
        MosqueTimetableInput input,
        TimetableValues values,
        CancellationToken ct)
    {
        var timetable = await db.MosqueTimetables
            .FirstOrDefaultAsync(x => x.EffectiveFrom == input.EffectiveFrom, ct);

        if (timetable is null)
        {
            timetable = new MosqueTimetable { EffectiveFrom = input.EffectiveFrom };
            db.MosqueTimetables.Add(timetable);
        }

        timetable.Fajr = values.Times["fajr"];
        timetable.Dhuhr = values.Times["dhuhr"];
        timetable.Asr = values.Times["asr"];
        timetable.Maghrib = values.Times["maghrib"];
        timetable.Isha = values.Times["isha"];
        timetable.FajrRule = JamaatRule.Serialize(values.Rules["fajr"]);
        timetable.DhuhrRule = JamaatRule.Serialize(values.Rules["dhuhr"]);
        timetable.AsrRule = JamaatRule.Serialize(values.Rules["asr"]);
        timetable.MaghribRule = JamaatRule.Serialize(values.Rules["maghrib"]);
        timetable.IshaRule = JamaatRule.Serialize(values.Rules["isha"]);
        timetable.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        timetable.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return timetable;
    }

    public MosqueTimetableDto ToDto(MosqueTimetable timetable) => new(
        timetable.EffectiveFrom,
        FormatTime(timetable.Fajr),
        FormatTime(timetable.Dhuhr),
        FormatTime(timetable.Asr),
        FormatTime(timetable.Maghrib),
        FormatTime(timetable.Isha),
        timetable.Note,
        PrayerKeys.ToDictionary(key => key, key => JamaatResolver.RuleFor(timetable, key)));

    private static Dictionary<string, string[]> ValidateSettings(PrayerSettingsDto input)
    {
        var errors = new Dictionary<string, string[]>();

        void Add(string field, string message) => errors[field] = [message];

        if (string.IsNullOrWhiteSpace(input.Country) || input.Country.Trim().Length > 100)
        {
            Add("country", "Country is required and must be 100 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(input.City) || input.City.Trim().Length > 100)
        {
            Add("city", "City is required and must be 100 characters or fewer.");
        }

        if (!double.IsFinite(input.Latitude) || input.Latitude is < -90 or > 90)
        {
            Add("latitude", "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(input.Longitude) || input.Longitude is < -180 or > 180)
        {
            Add("longitude", "Longitude must be between -180 and 180.");
        }

        if (!TimeZoneResolver.TryResolve(input.TimeZoneId, out _))
        {
            Add("timeZoneId", "Use a valid IANA time zone such as Asia/Dhaka.");
        }

        if (!PrayerMethodCatalog.IsSupported(input.MethodKey))
        {
            Add("methodKey", "Select a supported calculation method.");
        }

        if (!double.IsFinite(input.FajrAngle) || input.FajrAngle is < 0 or > 30)
        {
            Add("fajrAngle", "Fajr angle must be between 0 and 30 degrees.");
        }

        if (!double.IsFinite(input.IshaAngle) || input.IshaAngle is < 0 or > 30)
        {
            Add("ishaAngle", "Isha angle must be between 0 and 30 degrees.");
        }

        if (!string.Equals(input.AsrMadhab, "Hanafi", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(input.AsrMadhab, "Standard", StringComparison.OrdinalIgnoreCase))
        {
            Add("asrMadhab", "Asr method must be Hanafi or Standard.");
        }

        ValidateAdjustment(errors, "fajrAdjustment", input.FajrAdjustment);
        ValidateAdjustment(errors, "sunriseAdjustment", input.SunriseAdjustment);
        ValidateAdjustment(errors, "dhuhrAdjustment", input.DhuhrAdjustment);
        ValidateAdjustment(errors, "asrAdjustment", input.AsrAdjustment);
        ValidateAdjustment(errors, "maghribAdjustment", input.MaghribAdjustment);
        ValidateAdjustment(errors, "ishaAdjustment", input.IshaAdjustment);

        return errors;
    }

    private static void ValidateAdjustment(
        Dictionary<string, string[]> errors,
        string field,
        int value)
    {
        if (value is < -60 or > 60)
        {
            errors[field] = ["Adjustment must be between -60 and 60 minutes."];
        }
    }

    private static IReadOnlyList<PrayerTimeDto> MapTimes(
        PrayerDayResult day,
        MosqueTimetable? timetable,
        DateOnly date,
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<string, JamaatWindow> windows) =>
        day.Moments
            .Select(moment => MapMoment(moment, timetable, date, timeZone, windows))
            .ToList();

    private static PrayerTimeDto MapMoment(
        PrayerMoment moment,
        MosqueTimetable? timetable,
        DateOnly date,
        TimeZoneInfo timeZone,
        IReadOnlyDictionary<string, JamaatWindow> windows)
    {
        windows.TryGetValue(moment.Key, out var window);

        string? jamaatTime = null;
        DateTimeOffset? jamaatAt = null;
        int? difference = null;
        string? status = null;
        bool? withinWindow = null;
        string? validationError = null;
        string? mode = null;
        string? ruleText = null;
        var adjusted = false;
        string? note = null;

        if (moment.IsPrayer && window is not null && timetable is not null)
        {
            var rule = JamaatResolver.RuleFor(timetable, moment.Key);
            var resolution = JamaatResolver.Resolve(rule, JamaatResolver.TimeFor(timetable, moment.Key), date, window, timeZone);
            if (resolution is not null)
            {
                mode = rule.Mode;
                ruleText = rule.Mode == JamaatRule.Fixed
                    ? $"fixed time; if the Waqt passes it: {rule.EffectiveFallback.Describe()}"
                    : rule.Describe();
                adjusted = resolution.Adjusted;
                note = resolution.Note;
                withinWindow = resolution.Valid;
                validationError = resolution.Error;
                jamaatAt = resolution.At;
            }

            if (jamaatAt is { } at)
            {
                jamaatTime = JamaatSchedule.ToInputTime(at, timeZone);
                difference = (int)Math.Round((at - moment.At).TotalMinutes, MidpointRounding.AwayFromZero);
                status = difference == 0
                    ? "same"
                    : difference > 0
                        ? "jamaat-later"
                        : "jamaat-earlier";
            }
        }

        return new PrayerTimeDto(
            moment.Key,
            moment.Name,
            moment.Time,
            moment.At,
            moment.AdjustmentMinutes,
            moment.IsPrayer,
            jamaatTime,
            jamaatAt,
            difference,
            status,
            window is null ? null : JamaatSchedule.DescribeRange(window, timeZone),
            window is null ? null : JamaatSchedule.ToInputTime(window.Start, timeZone),
            window is null ? null : JamaatSchedule.ToInputTime(window.End.AddMinutes(-1), timeZone),
            withinWindow,
            validationError,
            mode,
            ruleText,
            adjusted,
            note);
    }

    private static IReadOnlyList<JamaatConflictDto> ConflictsFrom(
        IReadOnlyList<PrayerTimeDto> times,
        MosqueTimetable? timetable,
        DateOnly date)
    {
        var effectiveFrom = timetable?.EffectiveFrom ?? date;

        return times
            .Where(time => time.JamaatWithinWindow == false)
            .Select(time => new JamaatConflictDto(
                effectiveFrom,
                date,
                time.Key,
                time.Name,
                time.JamaatTime ?? "—",
                time.JamaatRange ?? "—",
                time.JamaatValidationError ?? "The Jamaat time is outside its prayer window."))
            .ToList();
    }

    private static string? FormatTime(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static TimeOnly? ParseTime(
        string? value,
        string field,
        Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var formats = new[] { "HH:mm", "H:mm", "HH:mm:ss" };
        if (TimeOnly.TryParseExact(
                value.Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var time))
        {
            return time;
        }

        errors[field] = ["Use a 24-hour time such as 05:15."];
        return null;
    }
}
