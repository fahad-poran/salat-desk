using DeskDashboard.Core;
using DeskDashboard.Core.Content;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Data.Entities;
using DeskDashboard.Core.Location;
using DeskDashboard.Web.Features.Location;
using DeskDashboard.Core.Prayer;
using DeskDashboard.Web.Features.Prayer;
using DeskDashboard.Web.Features.Content;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dashboard.db");
Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

builder.Services.AddDbContext<DashboardDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));
builder.Services.AddSingleton<PrayerTimeCalculator>();
builder.Services.AddScoped<PrayerService>();
builder.Services.AddHttpClient<GeocodingClient>(client =>
{
    client.BaseAddress = new Uri("https://geocoding-api.open-meteo.com/");
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("DeskDashboard/0.1");
});
builder.Services.AddProblemDetails();

// Ayat/Hadith content (shared with the phone app through LocalApi).
var dbOptions = new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite($"Data Source={dbPath}").Options;
builder.Services.AddHttpClient(nameof(ContentRefreshService), client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("SalatDesk/0.1");
});
builder.Services.AddSingleton(services => new ContentRefreshService(
    () => new DashboardDbContext(dbOptions),
    services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ContentRefreshService)),
    services.GetRequiredService<ILogger<ContentRefreshService>>()));
builder.Services.AddSingleton(services => new LocalApi(
    () => new DashboardDbContext(dbOptions),
    services.GetRequiredService<PrayerTimeCalculator>(),
    services.GetRequiredService<GeocodingClient>(),
    services.GetRequiredService<ILoggerFactory>(),
    services.GetRequiredService<ContentRefreshService>()));
builder.Services.AddHostedService<ContentRefreshHostedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DashboardDbContext>();
    var seed = builder.Configuration.GetSection("Prayer").Get<PrayerSettings>();
    if (DashboardDatabase.EnsureCreatedAndSeeded(db, seed))
    {
        app.Logger.LogInformation("Seeded prayer settings from appsettings.json.");
    }

    var imported = await new ContentService(db).SeedIfNeededAsync();
    if (imported > 0)
    {
        app.Logger.LogInformation("Imported {Count} starter Ayat/Hadith.", imported);
    }
}

app.UseExceptionHandler();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", async (DashboardDbContext db, CancellationToken ct) =>
{
    var databaseOk = await db.Database.CanConnectAsync(ct);
    return databaseOk
        ? Results.Ok(new { status = "ok", database = "ok", utc = DateTimeOffset.UtcNow })
        : Results.Problem("Database is not reachable.", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapPrayerEndpoints();
app.MapLocationEndpoints();
app.MapContentEndpoints();

app.Run();

public partial class Program;
