using DeskDashboard.Core.Data.Entities;
using DeskDashboard.Core.Prayer;

namespace DeskDashboard.Tests;

public class PrayerTimeCalculatorTests
{
    private readonly PrayerTimeCalculator _calculator = new();

    [Fact]
    public void KarachiHanafi_CalculatesDhakaTimesForKnownDate()
    {
        var result = _calculator.Calculate(
            CreateDhakaSettings(),
            new DateOnly(2026, 11, 4),
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));

        var times = result.Moments.ToDictionary(moment => moment.Key);

        AssertNear(times["fajr"], 2026, 11, 4, 4, 49);
        AssertNear(times["sunrise"], 2026, 11, 4, 6, 6);
        AssertNear(times["dhuhr"], 2026, 11, 4, 11, 43);
        AssertNear(times["asr"], 2026, 11, 4, 15, 41);
        AssertNear(times["maghrib"], 2026, 11, 4, 17, 18);
        AssertNear(times["isha"], 2026, 11, 4, 18, 35);
    }

    [Fact]
    public void StandardAsr_IsEarlierThanHanafiAsr()
    {
        var date = new DateOnly(2026, 11, 4);

        var hanafi = _calculator.Calculate(CreateDhakaSettings("Hanafi"), date)
            .Moments.Single(moment => moment.Key == "asr");
        var standard = _calculator.Calculate(CreateDhakaSettings("Standard"), date)
            .Moments.Single(moment => moment.Key == "asr");

        Assert.True(
            standard.At < hanafi.At,
            $"Expected Standard Asr ({standard.Time}) before Hanafi Asr ({hanafi.Time}).");
        Assert.True((hanafi.At - standard.At).TotalMinutes > 20);
    }

    [Fact]
    public void Adjustments_ShiftEachPrayerByConfiguredMinutes()
    {
        var date = new DateOnly(2026, 11, 4);
        var baseline = _calculator.Calculate(CreateDhakaSettings(), date)
            .Moments.ToDictionary(moment => moment.Key);

        var settings = CreateDhakaSettings();
        settings.FajrAdjustment = 5;
        settings.SunriseAdjustment = -2;
        settings.DhuhrAdjustment = 3;
        settings.AsrAdjustment = 4;
        settings.MaghribAdjustment = 2;
        settings.IshaAdjustment = -4;

        var adjusted = _calculator.Calculate(settings, date)
            .Moments.ToDictionary(moment => moment.Key);

        Assert.Equal(5, (adjusted["fajr"].At - baseline["fajr"].At).TotalMinutes);
        Assert.Equal(-2, (adjusted["sunrise"].At - baseline["sunrise"].At).TotalMinutes);
        Assert.Equal(3, (adjusted["dhuhr"].At - baseline["dhuhr"].At).TotalMinutes);
        Assert.Equal(4, (adjusted["asr"].At - baseline["asr"].At).TotalMinutes);
        Assert.Equal(2, (adjusted["maghrib"].At - baseline["maghrib"].At).TotalMinutes);
        Assert.Equal(-4, (adjusted["isha"].At - baseline["isha"].At).TotalMinutes);
    }

    [Fact]
    public void NextPrayer_IsNextCalculatedPrayerDuringTheDay()
    {
        var result = _calculator.Calculate(
            CreateDhakaSettings(),
            new DateOnly(2026, 11, 4),
            new DateTimeOffset(2026, 11, 4, 4, 0, 0, TimeSpan.Zero));

        Assert.Equal("dhuhr", result.Next.Key);
        Assert.Equal(new DateOnly(2026, 11, 4), DateOnly.FromDateTime(result.Next.At.DateTime));
    }

    [Fact]
    public void NextPrayer_AfterIsha_IsFajrTheFollowingDay()
    {
        var result = _calculator.Calculate(
            CreateDhakaSettings(),
            new DateOnly(2026, 11, 4),
            new DateTimeOffset(2026, 11, 4, 13, 30, 0, TimeSpan.Zero));

        Assert.Equal("fajr", result.Next.Key);
        Assert.Equal(new DateOnly(2026, 11, 5), DateOnly.FromDateTime(result.Next.At.DateTime));
        Assert.Equal(new DateOnly(2026, 11, 5), DateOnly.FromDateTime(result.TomorrowFajr.At.DateTime));
    }

    [Fact]
    public void Sunrise_IsNotTreatedAsAPrayer()
    {
        var result = _calculator.Calculate(
            CreateDhakaSettings(),
            new DateOnly(2026, 11, 4),
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));

        var sunrise = result.Moments.Single(moment => moment.Key == "sunrise");

        Assert.False(sunrise.IsPrayer);
        Assert.All(
            result.Moments.Where(moment => moment.Key != "sunrise"),
            moment => Assert.True(moment.IsPrayer));
    }

    [Fact]
    public void MethodCatalog_ExposesSupportedKeysAndDefaults()
    {
        Assert.NotEmpty(PrayerMethodCatalog.Options);

        foreach (var option in PrayerMethodCatalog.Options)
        {
            Assert.True(PrayerMethodCatalog.IsSupported(option.Key));
            Assert.InRange(option.DefaultFajrAngle, 0, 30);
            Assert.InRange(option.DefaultIshaAngle, 0, 30);
        }

        Assert.Equal(18, PrayerMethodCatalog.GetOption("Karachi").DefaultFajrAngle);
        Assert.False(PrayerMethodCatalog.IsSupported("UnknownMethod"));
    }

    private static PrayerSettings CreateDhakaSettings(string madhab = "Hanafi") => new()
    {
        Country = "Bangladesh",
        City = "Dhaka",
        Latitude = 23.8103,
        Longitude = 90.4125,
        TimeZoneId = "Asia/Dhaka",
        MethodKey = "Karachi",
        FajrAngle = 18,
        IshaAngle = 18,
        AsrMadhab = madhab
    };

    private static void AssertNear(
        PrayerMoment moment,
        int year,
        int month,
        int day,
        int hour,
        int minute)
    {
        var expected = new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(6));
        var difference = Math.Abs((moment.At - expected).TotalMinutes);

        Assert.True(
            difference <= 2,
            $"{moment.Name} was {moment.At:O} but expected {expected:O} (±2 minutes).");
    }
}
