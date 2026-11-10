using DeskDashboard.Core;
using DeskDashboard.Core.Content;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Location;
using DeskDashboard.Core.Prayer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DeskDashboard.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Services.AddHybridWebViewDeveloperTools();
#endif

        // Everything lives on the phone: the SQLite file sits in the app's private data folder.
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, "dashboard.db");
        var dbOptions = new DbContextOptionsBuilder<DashboardDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var geocodingHttp = new HttpClient
        {
            BaseAddress = new Uri("https://geocoding-api.open-meteo.com/"),
            Timeout = TimeSpan.FromSeconds(8)
        };
        geocodingHttp.DefaultRequestHeaders.UserAgent.ParseAdd("SalatDesk/0.1");

        var contentHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        contentHttp.DefaultRequestHeaders.UserAgent.ParseAdd("SalatDesk/0.1");

        builder.Services.AddSingleton<PrayerTimeCalculator>();
        builder.Services.AddSingleton(new GeocodingClient(geocodingHttp));
        builder.Services.AddSingleton(services => new ContentRefreshService(
            () => new DashboardDbContext(dbOptions),
            contentHttp,
            services.GetRequiredService<ILogger<ContentRefreshService>>())
        {
            // The weekly download is a few MB, so it waits for Wi-Fi (or Ethernet).
            CanDownload = () => Connectivity.Current.NetworkAccess == NetworkAccess.Internet &&
                                Connectivity.Current.ConnectionProfiles.Any(p =>
                                    p is ConnectionProfile.WiFi or ConnectionProfile.Ethernet)
        });
        builder.Services.AddSingleton(services => new LocalApi(
            () => new DashboardDbContext(dbOptions),
            services.GetRequiredService<PrayerTimeCalculator>(),
            services.GetRequiredService<GeocodingClient>(),
            services.GetRequiredService<ILoggerFactory>(),
            services.GetRequiredService<ContentRefreshService>()));
        builder.Services.AddSingleton<MainPage>();

        using (var db = new DashboardDbContext(dbOptions))
        {
            // Defaults match the PC app's appsettings.json (Dhaka, Karachi 18°/18°, Hanafi).
            DashboardDatabase.EnsureCreatedAndSeeded(db, null);
        }

        var app = builder.Build();

        // In the background: import the starter Ayat/Hadith on first run, then check the weekly
        // update shortly after start and every few hours.
        var refresh = app.Services.GetRequiredService<ContentRefreshService>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ContentRefresh");
        _ = Task.Run(async () =>
        {
            try
            {
                await using var db = new DashboardDbContext(dbOptions);
                await new ContentService(db).SeedIfNeededAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Importing the starter content failed.");
            }

            await ContentRefreshLoop.RunAsync(refresh, logger, TimeSpan.FromSeconds(20), CancellationToken.None);
        });

        return app;
    }
}
