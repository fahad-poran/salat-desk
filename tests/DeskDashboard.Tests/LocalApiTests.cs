using System.Text.Json;
using DeskDashboard.Core;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Location;
using DeskDashboard.Core.Prayer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskDashboard.Tests;

public sealed class LocalApiTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly LocalApi api;

    public LocalApiTests()
    {
        connection.Open();
        var options = new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite(connection).Options;
        DashboardDbContext Factory() => new(options);

        using (var db = Factory())
        {
            DashboardDatabase.EnsureCreatedAndSeeded(db, null);
        }

        api = new LocalApi(
            Factory,
            new PrayerTimeCalculator(),
            new GeocodingClient(new HttpClient { BaseAddress = new Uri("https://invalid.test/") }),
            NullLoggerFactory.Instance);
    }

    public void Dispose() => connection.Dispose();

    [Fact]
    public async Task Today_returns_camel_case_payload_with_six_times()
    {
        var response = await api.HandleAsync("GET", "/api/prayer/today?date=2026-11-04", null);

        Assert.Equal(200, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("2026-11-04", json.RootElement.GetProperty("date").GetString());
        Assert.Equal(6, json.RootElement.GetProperty("times").GetArrayLength());
        Assert.True(json.RootElement.TryGetProperty("serverNowUtc", out _));
    }

    [Fact]
    public async Task Absolute_urls_are_routed_like_relative_ones()
    {
        var response = await api.HandleAsync("GET", "https://0.0.0.1/api/prayer/settings", null);

        Assert.Equal(200, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("Dhaka", json.RootElement.GetProperty("settings").GetProperty("city").GetString());
    }

    [Fact]
    public async Task Invalid_settings_return_validation_problem()
    {
        var settings = await GetSettingsJsonAsync();
        var body = settings.Replace("\"latitude\":23.8103", "\"latitude\":123");

        var response = await api.HandleAsync("PUT", "/api/prayer/settings", body);

        Assert.Equal(400, response.Status);
        using var json = JsonDocument.Parse(response.Body);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("latitude", out _));
    }

    [Fact]
    public async Task Timetable_round_trips_and_rejects_out_of_window_values()
    {
        var good = """{"effectiveFrom":"2026-11-01","fajr":"05:15","dhuhr":"13:15","asr":null,"maghrib":null,"isha":null,"note":"Test"}""";
        var saved = await api.HandleAsync("PUT", "/api/prayer/timetable", good);
        Assert.Equal(200, saved.Status);

        var read = await api.HandleAsync("GET", "/api/prayer/timetable?date=2026-11-04", null);
        using (var json = JsonDocument.Parse(read.Body))
        {
            Assert.Equal("13:15", json.RootElement.GetProperty("timetable").GetProperty("dhuhr").GetString());
        }

        var bad = good.Replace("\"fajr\":\"05:15\"", "\"fajr\":\"09:00\"");
        var rejected = await api.HandleAsync("PUT", "/api/prayer/timetable", bad);
        Assert.Equal(400, rejected.Status);
        Assert.Contains("\"fajr\"", rejected.Body);
    }

    [Theory]
    [InlineData("GET", "/api/unknown", 404)]
    [InlineData("GET", "/api/prayer/today?date=11-04-2026", 400)]
    [InlineData("PUT", "/api/prayer/settings", 400)]
    [InlineData("GET", "/api/location/search?q=a", 400)]
    public async Task Bad_requests_map_to_problem_status(string method, string url, int status)
    {
        var response = await api.HandleAsync(method, url, null);
        Assert.Equal(status, response.Status);
    }

    private async Task<string> GetSettingsJsonAsync()
    {
        var response = await api.HandleAsync("GET", "/api/prayer/settings", null);
        using var json = JsonDocument.Parse(response.Body);
        return json.RootElement.GetProperty("settings").GetRawText();
    }
}
