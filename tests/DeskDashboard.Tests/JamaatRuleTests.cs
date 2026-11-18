using System.Text.Json;
using DeskDashboard.Core;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Data.Entities;
using DeskDashboard.Core.Location;
using DeskDashboard.Core.Prayer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskDashboard.Tests;

/// <summary>Jamaat methods (fixed / after Waqt / rounded slot), the fallback, the planner and the single save.</summary>
public sealed class JamaatRuleTests : IDisposable
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneResolver.Resolve("Asia/Dhaka");
    private static readonly DateOnly Date = new(2026, 11, 4);

    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<DashboardDbContext> options;
    private readonly LocalApi api;

    public JamaatRuleTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite(connection).Options;
        using (var db = new DashboardDbContext(options))
        {
            DashboardDatabase.EnsureCreatedAndSeeded(db, null);
        }

        api = new LocalApi(
            () => new DashboardDbContext(options),
            new PrayerTimeCalculator(),
            new GeocodingClient(new HttpClient { BaseAddress = new Uri("https://invalid.test/") }),
            NullLoggerFactory.Instance);
    }

    public void Dispose() => connection.Dispose();

    // A synthetic Asr window: 4:08 PM to 5:21 PM (last minute 5:20).
    private static JamaatWindow Window(int startHour = 16, int startMinute = 8, int endHour = 17, int endMinute = 21)
    {
        DateTimeOffset At(int h, int m) => new(Date.ToDateTime(new TimeOnly(h, m)), Dhaka.GetUtcOffset(Date.ToDateTime(new TimeOnly(h, m))));
        return new JamaatWindow("asr", "Asr", At(startHour, startMinute), At(endHour, endMinute), "Maghrib", false);
    }

    private static string Local(DateTimeOffset? at) => JamaatSchedule.ToInputTime(at!.Value, Dhaka);

    private static JamaatResolution Resolve(JamaatRule rule, string? fixedTime = null, JamaatWindow? window = null) =>
        JamaatResolver.Resolve(rule, fixedTime is null ? null : TimeOnly.Parse(fixedTime), Date, window ?? Window(), Dhaka)!;

    [Fact]
    public void Fixed_time_inside_the_window_is_used()
    {
        var result = Resolve(JamaatRule.Default, "16:30");
        Assert.Equal("16:30", Local(result.At));
        Assert.True(result.Valid);
        Assert.False(result.Adjusted);
    }

    [Theory]
    [InlineData("16:00")] // before the Waqt
    [InlineData("17:40")] // after the window end
    public void Fixed_time_outside_the_window_uses_the_offset_fallback(string time)
    {
        var rule = new JamaatRule(JamaatRule.Fixed, Fallback: new JamaatRule(JamaatRule.Offset, 10));
        var result = Resolve(rule, time);
        Assert.Equal("16:18", Local(result.At));
        Assert.True(result.Valid);
        Assert.True(result.Adjusted);
        Assert.Contains("Update the Jamaat time", result.Note);
    }

    [Fact]
    public void Fixed_time_outside_the_window_uses_the_slot_fallback()
    {
        var rule = new JamaatRule(JamaatRule.Fixed, Fallback: new JamaatRule(JamaatRule.SlotMode, Slot: 15, Gap: 5));
        Assert.Equal("16:15", Local(Resolve(rule, "16:00").At));
    }

    [Fact]
    public void Warn_fallback_keeps_the_time_and_is_invalid()
    {
        var rule = new JamaatRule(JamaatRule.Fixed, Fallback: new JamaatRule(JamaatRule.Warn));
        var result = Resolve(rule, "16:00");
        Assert.Equal("16:00", Local(result.At));
        Assert.False(result.Valid);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Offset_follows_the_Waqt_and_is_capped_at_the_window()
    {
        Assert.Equal("16:13", Local(Resolve(new JamaatRule(JamaatRule.Offset, 5)).At));
        var capped = Resolve(new JamaatRule(JamaatRule.Offset, 120));
        Assert.Equal("17:20", Local(capped.At));
        Assert.Contains("last minute", capped.Note);
    }

    [Theory]
    [InlineData(10, 0, "16:10")]
    [InlineData(15, 5, "16:15")]
    [InlineData(30, 10, "16:30")]
    [InlineData(15, 7, "16:15")] // exactly on a slot
    [InlineData(15, 8, "16:30")]
    public void Slot_rounds_up_after_the_gap(int slot, int gap, string expected)
    {
        var result = Resolve(new JamaatRule(JamaatRule.SlotMode, Slot: slot, Gap: gap));
        Assert.Equal(expected, Local(result.At));
        Assert.False(result.Adjusted);
    }

    [Fact]
    public void Isha_slot_after_midnight_stays_in_the_window()
    {
        var start = new DateTimeOffset(Date.ToDateTime(new TimeOnly(23, 50)), Dhaka.GetUtcOffset(Date.ToDateTime(new TimeOnly(23, 50))));
        var end = start.AddHours(5);
        var window = new JamaatWindow("isha", "Isha", start, end, "Fajr", true);
        var result = JamaatResolver.Resolve(new JamaatRule(JamaatRule.SlotMode, Slot: 30, Gap: 5), null, Date, window, Dhaka)!;
        Assert.Equal("00:00", Local(result.At));
        Assert.True(result.At > start);
    }

    [Fact]
    public void Empty_window_has_no_Jamaat()
    {
        var window = Window(16, 8, 16, 8);
        var result = Resolve(new JamaatRule(JamaatRule.Offset, 5), window: window);
        Assert.False(result.Valid);
        Assert.Null(result.At);
    }

    [Fact]
    public void Rules_round_trip_as_compact_json()
    {
        Assert.Null(JamaatRule.Serialize(JamaatRule.Default));
        var rule = new JamaatRule(JamaatRule.SlotMode, Minutes: 99, Slot: 15, Gap: 5);
        var json = JamaatRule.Serialize(rule);
        Assert.Equal("""{"mode":"slot","slot":15,"gap":5}""", json);
        Assert.Equal(new JamaatRule(JamaatRule.SlotMode, Slot: 15, Gap: 5), JamaatRule.Parse(json));
        Assert.Equal(JamaatRule.Default, JamaatRule.Parse("not json"));
    }

    [Fact]
    public void Invalid_rules_are_reported()
    {
        Assert.NotEmpty(JamaatRule.Errors(new JamaatRule("sometimes")));
        Assert.NotEmpty(JamaatRule.Errors(new JamaatRule(JamaatRule.SlotMode, Slot: 7, Gap: 5)));
        Assert.NotEmpty(JamaatRule.Errors(new JamaatRule(JamaatRule.Offset, 500)));
        Assert.NotEmpty(JamaatRule.Errors(new JamaatRule(JamaatRule.Fixed, Fallback: new JamaatRule(JamaatRule.Fixed))));
        Assert.Empty(JamaatRule.Errors(JamaatRule.Default));
    }

    [Fact]
    public async Task Save_all_stores_rules_and_alerts_together()
    {
        var body = JsonSerializer.Serialize(new
        {
            prayer = await CurrentSettingsAsync(),
            timetable = new
            {
                effectiveFrom = "2026-11-01",
                dhuhr = "13:15",
                rules = new Dictionary<string, object>
                {
                    ["maghrib"] = new { mode = "offset", minutes = 5 },
                    ["asr"] = new { mode = "slot", slot = 15, gap = 5 }
                }
            },
            alerts = new Dictionary<string, object> { ["asr"] = new { notify = false, minutes = 20 } }
        }, LocalApi.Json);

        var saved = await api.HandleAsync("PUT", "/api/settings", body);
        Assert.Equal(200, saved.Status);

        var today = await api.HandleAsync("GET", "/api/prayer/today?date=2026-11-04", null);
        using var json = JsonDocument.Parse(today.Body);
        var times = json.RootElement.GetProperty("times").EnumerateArray().ToDictionary(t => t.GetProperty("key").GetString()!);
        Assert.Equal("13:15", times["dhuhr"].GetProperty("jamaatTime").GetString());
        Assert.Equal("offset", times["maghrib"].GetProperty("jamaatMode").GetString());
        Assert.Equal(0, int.Parse(times["asr"].GetProperty("jamaatTime").GetString()![3..]) % 15); // on a 15-min slot
        Assert.False(json.RootElement.GetProperty("alerts").GetProperty("asr").GetProperty("notify").GetBoolean());
        Assert.Equal(20, json.RootElement.GetProperty("alerts").GetProperty("asr").GetProperty("minutes").GetInt32());
    }

    [Fact]
    public async Task Save_all_saves_nothing_when_any_part_is_invalid()
    {
        var settings = await CurrentSettingsAsync();
        var body = JsonSerializer.Serialize(new
        {
            prayer = settings with { City = "Chittagong" },
            timetable = new { effectiveFrom = "2026-11-01", fajr = "09:00" }, // after sunrise
            alerts = new Dictionary<string, object> { ["fajr"] = new { notify = true, minutes = 500 } }
        }, LocalApi.Json);

        var response = await api.HandleAsync("PUT", "/api/settings", body);
        Assert.Equal(400, response.Status);
        Assert.Contains("timetable.fajr", response.Body);
        Assert.Contains("alerts.fajr", response.Body);
        Assert.Equal("Dhaka", (await CurrentSettingsAsync()).City);
    }

    [Fact]
    public async Task Planner_gives_a_week_of_warnings_for_prayers_with_notify_on()
    {
        await using (var db = new DashboardDbContext(options))
        {
            db.MosqueTimetables.Add(new MosqueTimetable
            {
                EffectiveFrom = new DateOnly(2026, 1, 1),
                MaghribRule = JamaatRule.Serialize(new JamaatRule(JamaatRule.Offset, 5)),
                IshaRule = JamaatRule.Serialize(new JamaatRule(JamaatRule.SlotMode, Slot: 15, Gap: 5))
            });
            db.AlertPreferences.Add(new AlertPreference { Key = "isha", Notify = false, Minutes = 10 });
            db.AlertPreferences.Add(new AlertPreference { Key = "maghrib", Notify = true, Minutes = 30 });
            await db.SaveChangesAsync();
        }

        var now = new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
        await using var read = new DashboardDbContext(options);
        var service = new PrayerService(read, new PrayerTimeCalculator(), NullLogger<PrayerService>.Instance);
        var alerts = await service.PlanAlertsAsync(now);

        Assert.All(alerts, a => Assert.Equal("maghrib", a.Key)); // isha off, others have no Jamaat
        Assert.InRange(alerts.Count, 6, 8);
        Assert.All(alerts, a => Assert.Equal(5, a.Minutes)); // 30 wanted, capped at the 5 minutes since the Waqt
        Assert.All(alerts, a => Assert.True(a.WarnAt > now.ToUnixTimeMilliseconds()));
        Assert.Equal(alerts.Count, alerts.Select(a => a.Id).Distinct().Count());
    }

    [Fact]
    public void Migration_adds_new_columns_to_an_old_database()
    {
        using var old = new SqliteConnection("Data Source=:memory:");
        old.Open();
        using (var command = old.CreateCommand())
        {
            // The 0.1.0 shape of the two tables that gained columns.
            command.CommandText = """
                CREATE TABLE "MosqueTimetables" ("Id" INTEGER PRIMARY KEY, "EffectiveFrom" TEXT NOT NULL, "Fajr" TEXT NULL);
                CREATE TABLE "ContentItems" ("Id" INTEGER PRIMARY KEY, "SourceKey" TEXT NOT NULL);
                """;
            command.ExecuteNonQuery();
        }

        using var db = new DashboardDbContext(new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite(old).Options);
        DashboardDatabase.MigrateSchema(db);
        DashboardDatabase.MigrateSchema(db); // safe to run twice

        var columns = new List<string>();
        using (var info = old.CreateCommand())
        {
            info.CommandText = "SELECT name FROM pragma_table_info('MosqueTimetables') UNION ALL SELECT name FROM pragma_table_info('ContentItems')";
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(reader.GetString(0));
            }
        }

        Assert.Contains("IshaRule", columns);
        Assert.Contains("RefBook", columns);
        Assert.Contains("RefNumber", columns);
    }

    private async Task<PrayerSettingsDto> CurrentSettingsAsync()
    {
        var response = await api.HandleAsync("GET", "/api/prayer/settings", null);
        using var json = JsonDocument.Parse(response.Body);
        return JsonSerializer.Deserialize<PrayerSettingsDto>(json.RootElement.GetProperty("settings").GetRawText(), LocalApi.Json)!;
    }
}
