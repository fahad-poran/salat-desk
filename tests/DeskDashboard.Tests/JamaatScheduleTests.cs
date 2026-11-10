using DeskDashboard.Core.Data.Entities;
using DeskDashboard.Core.Prayer;

namespace DeskDashboard.Tests;

public class JamaatScheduleTests
{
    private static readonly DateOnly Date = new(2026, 11, 4);
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    private readonly PrayerTimeCalculator _calculator = new();
    private readonly TimeZoneInfo _timeZone = TimeZoneResolver.Resolve("Asia/Dhaka");

    [Theory]
    [InlineData("fajr")]
    [InlineData("dhuhr")]
    [InlineData("asr")]
    [InlineData("maghrib")]
    [InlineData("isha")]
    public void Jamaat_AtPrayerStart_IsValid(string key)
    {
        var window = GetWindow(key);

        var validation = JamaatSchedule.Validate(key, ToTime(window.Start), Date, window, _timeZone);

        Assert.True(validation.IsValid);
        Assert.Equal(window.Start, validation.JamaatAt);
    }

    [Theory]
    [InlineData("fajr")]
    [InlineData("dhuhr")]
    [InlineData("asr")]
    [InlineData("maghrib")]
    [InlineData("isha")]
    public void Jamaat_OneMinuteBeforeStart_IsInvalid(string key)
    {
        var window = GetWindow(key);

        var validation = JamaatSchedule.Validate(
            key,
            ToTime(window.Start.AddMinutes(-1)),
            Date,
            window,
            _timeZone);

        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
    }

    [Theory]
    [InlineData("fajr")]
    [InlineData("dhuhr")]
    [InlineData("asr")]
    [InlineData("maghrib")]
    [InlineData("isha")]
    public void Jamaat_OneMinuteBeforeEndBoundary_IsValid(string key)
    {
        var window = GetWindow(key);

        var validation = JamaatSchedule.Validate(
            key,
            ToTime(window.End.AddMinutes(-1)),
            Date,
            window,
            _timeZone);

        Assert.True(validation.IsValid);
        Assert.Equal(window.End.AddMinutes(-1), validation.JamaatAt);
    }

    [Theory]
    [InlineData("fajr")]
    [InlineData("dhuhr")]
    [InlineData("asr")]
    [InlineData("maghrib")]
    [InlineData("isha")]
    public void Jamaat_ExactlyAtEndBoundary_IsInvalid(string key)
    {
        var window = GetWindow(key);

        var validation = JamaatSchedule.Validate(key, ToTime(window.End), Date, window, _timeZone);

        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
    }

    [Theory]
    [InlineData("fajr")]
    [InlineData("dhuhr")]
    [InlineData("asr")]
    [InlineData("maghrib")]
    [InlineData("isha")]
    public void Jamaat_AfterEndBoundary_IsInvalid(string key)
    {
        var window = GetWindow(key);

        var validation = JamaatSchedule.Validate(
            key,
            ToTime(window.End.AddMinutes(1)),
            Date,
            window,
            _timeZone);

        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
    }

    [Fact]
    public void Isha_AfterMidnight_IsValidUntilNextFajr()
    {
        var window = GetWindow("isha");

        var afterMidnight = JamaatSchedule.Validate(
            "isha",
            new TimeOnly(0, 30),
            Date,
            window,
            _timeZone);

        Assert.True(window.SpansMidnight);
        Assert.True(afterMidnight.IsValid);
        Assert.Equal(Date.AddDays(1), DateOnly.FromDateTime(afterMidnight.JamaatAt.DateTime));
    }

    [Fact]
    public void Isha_AtNextDayFajr_IsInvalid()
    {
        var window = GetWindow("isha");

        var validation = JamaatSchedule.Validate("isha", ToTime(window.End), Date, window, _timeZone);

        Assert.False(validation.IsValid);
    }

    [Fact]
    public void Isha_AtNextDayFajrOnNewYearsEve_IsInvalid()
    {
        var date = new DateOnly(2026, 12, 31);
        var day = _calculator.Calculate(CreateDhakaSettings(), date, Now);
        var window = JamaatSchedule.Build(day, _timeZone)["isha"];

        var validation = JamaatSchedule.Validate("isha", ToTime(window.End), date, window, _timeZone);

        Assert.True(window.SpansMidnight);
        Assert.Equal(new DateOnly(2027, 1, 1), DateOnly.FromDateTime(window.End.DateTime));
        Assert.False(validation.IsValid);
    }

    [Fact]
    public void ManualAdjustment_MovesTheValidWindow()
    {
        var date = new DateOnly(2026, 11, 4);
        var baselineDay = _calculator.Calculate(CreateDhakaSettings(), date, Now);
        var baselineWindow = JamaatSchedule.Build(baselineDay, _timeZone)["dhuhr"];
        var previouslyValid = ToTime(baselineWindow.Start);

        var settings = CreateDhakaSettings();
        settings.DhuhrAdjustment = 15;
        var adjustedDay = _calculator.Calculate(settings, date, Now);
        var adjustedWindow = JamaatSchedule.Build(adjustedDay, _timeZone)["dhuhr"];

        Assert.Equal(baselineWindow.Start.AddMinutes(15), adjustedWindow.Start);

        var validation = JamaatSchedule.Validate(
            "dhuhr",
            previouslyValid,
            date,
            adjustedWindow,
            _timeZone);

        Assert.False(validation.IsValid);
        Assert.NotNull(validation.Error);
        Assert.Contains("Dhuhr", validation.Error);
    }

    [Fact]
    public void SettingsChange_ReportsConflictWithoutChangingTimetable()
    {
        var date = new DateOnly(2026, 11, 4);
        var settings = CreateDhakaSettings();
        var day = _calculator.Calculate(settings, date, Now);
        var dhuhrStart = ToTime(JamaatSchedule.Build(day, _timeZone)["dhuhr"].Start);
        var timetable = new MosqueTimetable
        {
            EffectiveFrom = date,
            Dhuhr = dhuhrStart
        };

        settings.DhuhrAdjustment = 15;
        var conflicts = JamaatSchedule.FindConflicts(
            settings,
            _calculator,
            [timetable],
            _timeZone,
            date);

        var conflict = Assert.Single(conflicts, item => item.PrayerKey == "dhuhr" && item.OnDate == date);
        Assert.Equal(JamaatSchedule.FormatTime(dhuhrStart), conflict.JamaatTime);
        Assert.Contains("outside", conflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(dhuhrStart, timetable.Dhuhr);
    }

    [Fact]
    public void ConflictScan_DoesNotReportValidTimetableOnTheSameDate()
    {
        var date = new DateOnly(2026, 11, 4);
        var settings = CreateDhakaSettings();
        var day = _calculator.Calculate(settings, date, Now);
        var windows = JamaatSchedule.Build(day, _timeZone);
        var timetable = new MosqueTimetable
        {
            EffectiveFrom = date,
            Fajr = ToTime(windows["fajr"].Start.AddMinutes(10)),
            Dhuhr = ToTime(windows["dhuhr"].Start.AddMinutes(10)),
            Asr = ToTime(windows["asr"].Start.AddMinutes(10)),
            Maghrib = ToTime(windows["maghrib"].Start.AddMinutes(10)),
            Isha = ToTime(windows["isha"].Start.AddMinutes(10))
        };

        var conflicts = JamaatSchedule.FindConflicts(
            settings,
            _calculator,
            [timetable],
            _timeZone,
            date);

        Assert.DoesNotContain(conflicts, item => item.OnDate == date);
    }

    [Fact]
    public void ConflictScan_ReportsOneConflictPerPrayerPerTimetable()
    {
        var date = new DateOnly(2026, 11, 4);
        var settings = CreateDhakaSettings();
        var day = _calculator.Calculate(settings, date, Now);
        var fajrStart = ToTime(JamaatSchedule.Build(day, _timeZone)["fajr"].Start);
        var timetable = new MosqueTimetable
        {
            EffectiveFrom = date,
            Fajr = fajrStart
        };

        settings.FajrAdjustment = 60;
        var conflicts = JamaatSchedule.FindConflicts(
            settings,
            _calculator,
            [timetable],
            _timeZone,
            date);

        var conflict = Assert.Single(conflicts);
        Assert.Equal("fajr", conflict.PrayerKey);
        Assert.Equal(date, conflict.OnDate);
    }

    private JamaatWindow GetWindow(string key)
    {
        var day = _calculator.Calculate(CreateDhakaSettings(), Date, Now);
        var windows = JamaatSchedule.Build(day, _timeZone);
        Assert.True(windows.ContainsKey(key));

        return windows[key];
    }

    private static TimeOnly ToTime(DateTimeOffset value) => TimeOnly.FromDateTime(value.DateTime);

    private static PrayerSettings CreateDhakaSettings() => new()
    {
        Country = "Bangladesh",
        City = "Dhaka",
        Latitude = 23.8103,
        Longitude = 90.4125,
        TimeZoneId = "Asia/Dhaka",
        MethodKey = "Karachi",
        FajrAngle = 18,
        IshaAngle = 18,
        AsrMadhab = "Hanafi"
    };
}
