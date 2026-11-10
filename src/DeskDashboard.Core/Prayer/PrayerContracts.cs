namespace DeskDashboard.Core.Prayer;

public sealed record PrayerLocationDto(
    string Country,
    string City,
    double Latitude,
    double Longitude);

public sealed record PrayerSettingsDto(
    string Country,
    string City,
    double Latitude,
    double Longitude,
    string TimeZoneId,
    string MethodKey,
    double FajrAngle,
    double IshaAngle,
    string AsrMadhab,
    int FajrAdjustment,
    int SunriseAdjustment,
    int DhuhrAdjustment,
    int AsrAdjustment,
    int MaghribAdjustment,
    int IshaAdjustment);

public sealed record PrayerTimeDto(
    string Key,
    string Name,
    string Time,
    DateTimeOffset At,
    int AdjustmentMinutes,
    bool IsPrayer,
    string? JamaatTime,
    DateTimeOffset? JamaatAt,
    int? DifferenceMinutes,
    string? JamaatStatus,
    string? JamaatRange,
    string? JamaatEarliest,
    string? JamaatLatest,
    bool? JamaatWithinWindow,
    string? JamaatValidationError);

public sealed record NextPrayerDto(
    string Key,
    string Name,
    string Time,
    DateTimeOffset At,
    bool IsTomorrow);

public sealed record JamaatConflictDto(
    DateOnly EffectiveFrom,
    DateOnly OnDate,
    string PrayerKey,
    string PrayerName,
    string JamaatTime,
    string AllowedRange,
    string Message);

public sealed record MosqueTimetableDto(
    DateOnly EffectiveFrom,
    string? Fajr,
    string? Dhuhr,
    string? Asr,
    string? Maghrib,
    string? Isha,
    string? Note);

public sealed record MosqueTimetableResponse(
    MosqueTimetableDto? Timetable,
    IReadOnlyList<JamaatConflictDto> Conflicts);

public sealed record PrayerTodayResponse(
    DateOnly Date,
    string TimeZoneId,
    PrayerLocationDto Location,
    PrayerSettingsDto Settings,
    IReadOnlyList<PrayerMethodOption> Methods,
    IReadOnlyList<PrayerTimeDto> Times,
    NextPrayerDto Next,
    PrayerTimeDto TomorrowFajr,
    PrayerTimeDto? PreviousIsha,
    DateTimeOffset ServerNowUtc,
    MosqueTimetableDto? Timetable,
    IReadOnlyList<JamaatConflictDto> Conflicts,
    string Disclaimer);

public sealed record PrayerSettingsResponse(
    PrayerSettingsDto Settings,
    IReadOnlyList<PrayerMethodOption> Methods,
    IReadOnlyList<JamaatConflictDto> Conflicts,
    string Disclaimer);

public sealed record MosqueTimetableInput(
    DateOnly EffectiveFrom,
    string? Fajr,
    string? Dhuhr,
    string? Asr,
    string? Maghrib,
    string? Isha,
    string? Note);
