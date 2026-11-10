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

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Prayer settings updated for {City}, {Country}.", settings.City, settings.Country);

        return (true, [], settings);
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
            Disclaimer);
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
        var errors = new Dictionary<string, string[]>();
        var fajr = ParseTime(input.Fajr, "fajr", errors);
        var dhuhr = ParseTime(input.Dhuhr, "dhuhr", errors);
        var asr = ParseTime(input.Asr, "asr", errors);
        var maghrib = ParseTime(input.Maghrib, "maghrib", errors);
        var isha = ParseTime(input.Isha, "isha", errors);

        if (input.Note?.Length > 200)
        {
            errors["note"] = ["Note must be 200 characters or fewer."];
        }

        if (input.EffectiveFrom == default)
        {
            errors["effectiveFrom"] = ["Choose the date this timetable becomes effective."];
        }

        if (errors.Count == 0)
        {
            var settings = await GetSettingsAsync(ct);
            var timeZone = TimeZoneResolver.Resolve(settings.TimeZoneId);
            var day = calculator.Calculate(settings, input.EffectiveFrom);
            var windows = JamaatSchedule.Build(day, timeZone);

            foreach (var (key, time) in new[]
                     {
                         ("fajr", fajr),
                         ("dhuhr", dhuhr),
                         ("asr", asr),
                         ("maghrib", maghrib),
                         ("isha", isha)
                     })
            {
                if (time is null)
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

        if (errors.Count > 0)
        {
            return (false, errors, null);
        }

        var timetable = await db.MosqueTimetables
            .FirstOrDefaultAsync(x => x.EffectiveFrom == input.EffectiveFrom, ct);

        if (timetable is null)
        {
            timetable = new MosqueTimetable { EffectiveFrom = input.EffectiveFrom };
            db.MosqueTimetables.Add(timetable);
        }

        timetable.Fajr = fajr;
        timetable.Dhuhr = dhuhr;
        timetable.Asr = asr;
        timetable.Maghrib = maghrib;
        timetable.Isha = isha;
        timetable.Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        timetable.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Jamaat timetable saved effective {EffectiveFrom}.", timetable.EffectiveFrom);

        return (true, [], timetable);
    }

    public MosqueTimetableDto ToDto(MosqueTimetable timetable) => new(
        timetable.EffectiveFrom,
        FormatTime(timetable.Fajr),
        FormatTime(timetable.Dhuhr),
        FormatTime(timetable.Asr),
        FormatTime(timetable.Maghrib),
        FormatTime(timetable.Isha),
        timetable.Note);

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

        if (moment.IsPrayer && window is not null)
        {
            var configured = GetJamaatTime(timetable, moment.Key);
            if (configured is { } time)
            {
                jamaatTime = FormatTime(time);
                var validation = JamaatSchedule.Validate(moment.Key, time, date, window, timeZone);
                jamaatAt = validation.JamaatAt;
                withinWindow = validation.IsValid;
                validationError = validation.Error;
                difference = (int)Math.Round(
                    (validation.JamaatAt - moment.At).TotalMinutes,
                    MidpointRounding.AwayFromZero);
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
            validationError);
    }

    private static TimeOnly? GetJamaatTime(MosqueTimetable? timetable, string prayerKey) => prayerKey switch
    {
        "fajr" => timetable?.Fajr,
        "dhuhr" => timetable?.Dhuhr,
        "asr" => timetable?.Asr,
        "maghrib" => timetable?.Maghrib,
        "isha" => timetable?.Isha,
        _ => null
    };

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
