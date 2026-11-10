using System.Globalization;
using System.Text.Json;
using DeskDashboard.Core.Content;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Location;
using DeskDashboard.Core.Prayer;

namespace DeskDashboard.Core;

public sealed record LocalApiResponse(int Status, string Body);

/// <summary>
/// In-process version of the HTTP API that the dashboard frontend calls. The standalone
/// app routes the frontend's requests here instead of to a server, so the request and
/// response shapes (paths, camelCase JSON, validation problems) match the ASP.NET host.
/// </summary>
public sealed partial class LocalApi(
    Func<DashboardDbContext> dbFactory,
    PrayerTimeCalculator calculator,
    GeocodingClient geocoding,
    ILoggerFactory loggerFactory,
    ContentRefreshService? contentRefresh = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ILogger logger = loggerFactory.CreateLogger<LocalApi>();

    public async Task<LocalApiResponse> HandleAsync(
        string method,
        string url,
        string? body,
        CancellationToken ct = default)
    {
        var (path, query) = Split(url);
        method = method.ToUpperInvariant();

        try
        {
            await using var db = dbFactory();
            var service = new PrayerService(db, calculator, loggerFactory.CreateLogger<PrayerService>());

            return (method, path) switch
            {
                ("GET", "/health") => Ok(new { status = "ok", database = "ok", utc = DateTimeOffset.UtcNow }),

                ("GET", "/api/prayer/today") =>
                    Ok(await service.BuildTodayAsync(ParseDate(query), ct)),

                ("GET", "/api/prayer/settings") =>
                    Ok(await SettingsResponseAsync(service, await service.GetSettingsAsync(ct), ct)),

                ("PUT", "/api/prayer/settings") => await PutSettingsAsync(service, body, ct),

                ("GET", "/api/prayer/timetable") =>
                    Ok(await service.GetTimetableAsync(ParseDate(query), ct)),

                ("PUT", "/api/prayer/timetable") => await PutTimetableAsync(service, body, ct),

                ("GET", "/api/location/search") => await SearchLocationAsync(query, ct),

                ("GET", "/api/content") => await ListContentAsync(db, query, ct),

                ("GET", "/api/content/active") => Ok(await new ContentService(db).ActiveAsync(ct)),

                ("GET", "/api/content/status") =>
                    Ok(await new ContentService(db).StatusAsync(contentRefresh?.IsRunning ?? false, ct)),

                ("POST", "/api/content/refresh") => StartRefresh(),

                _ when ContentItemRoute().Match(path) is { Success: true } match =>
                    await ContentItemAsync(db, method, int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value, body, ct),

                _ => Problem(404, "Not found.", $"No local handler for {method} {path}.")
            };
        }
        catch (BadRequestException ex)
        {
            return Problem(400, "Bad request.", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Local API call {Method} {Url} failed.", method, url);
            return Problem(500, "An error occurred while processing your request.", null);
        }
    }

    private static async Task<LocalApiResponse> ListContentAsync(
        DashboardDbContext db,
        IReadOnlyDictionary<string, string> query,
        CancellationToken ct)
    {
        query.TryGetValue("type", out var type);
        if (!ContentTypes.IsValid(type))
        {
            return ValidationProblem(new Dictionary<string, string[]> { ["type"] = ["Use quran or hadith."] });
        }

        var hidden = query.TryGetValue("hidden", out var raw) && raw is "true" or "1";
        return Ok(await new ContentService(db).ListAsync(type!, hidden, ct));
    }

    private static async Task<LocalApiResponse> ContentItemAsync(
        DashboardDbContext db,
        string method,
        int id,
        string action,
        string? body,
        CancellationToken ct)
    {
        var service = new ContentService(db);
        switch (method, action)
        {
            case ("POST", "shown"):
                return await service.MarkShownAsync(id, ct)
                    ? new LocalApiResponse(204, "")
                    : Problem(404, "Not found.", $"No content item {id}.");
            case ("PUT", "hidden"):
                var input = Deserialize<ContentHiddenInput>(body);
                var item = await service.SetHiddenAsync(id, input.Hidden, ct);
                return item is null ? Problem(404, "Not found.", $"No content item {id}.") : Ok(item);
            default:
                return Problem(404, "Not found.", $"No local handler for {method} /api/content/{id}/{action}.");
        }
    }

    private LocalApiResponse StartRefresh()
    {
        if (contentRefresh is null)
        {
            return Problem(503, "Unavailable.", "Content updates are not available here.");
        }

        if (!contentRefresh.IsRunning)
        {
            // Runs in the background; the page polls /api/content/status for the result.
            _ = Task.Run(() => contentRefresh.RefreshAsync(CancellationToken.None));
        }

        return new LocalApiResponse(202, JsonSerializer.Serialize(new { started = true }, Json));
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^/api/content/(\d+)/(shown|hidden)$")]
    private static partial System.Text.RegularExpressions.Regex ContentItemRoute();

    private static async Task<PrayerSettingsResponse> SettingsResponseAsync(
        PrayerService service,
        Data.Entities.PrayerSettings settings,
        CancellationToken ct) =>
        new(
            service.ToDto(settings),
            PrayerMethodCatalog.Options,
            await service.GetConflictsAsync(ct),
            PrayerService.Disclaimer);

    private static async Task<LocalApiResponse> PutSettingsAsync(
        PrayerService service,
        string? body,
        CancellationToken ct)
    {
        var input = Deserialize<PrayerSettingsDto>(body);
        var result = await service.UpdateSettingsAsync(input, ct);
        return result.Ok
            ? Ok(await SettingsResponseAsync(service, result.Settings!, ct))
            : ValidationProblem(result.Errors);
    }

    private static async Task<LocalApiResponse> PutTimetableAsync(
        PrayerService service,
        string? body,
        CancellationToken ct)
    {
        var input = Deserialize<MosqueTimetableInput>(body);
        var result = await service.UpsertTimetableAsync(input, ct);
        return result.Ok
            ? Ok(service.ToDto(result.Timetable!))
            : ValidationProblem(result.Errors);
    }

    private async Task<LocalApiResponse> SearchLocationAsync(
        IReadOnlyDictionary<string, string> query,
        CancellationToken ct)
    {
        query.TryGetValue("q", out var q);
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["Enter at least 2 characters."] });
        }

        try
        {
            return Ok(await geocoding.SearchAsync(q.Trim(), ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            logger.LogWarning(ex, "City search failed for {Query}.", q);
            return Problem(502, "Bad gateway.", "City lookup is unavailable. Enter latitude and longitude manually.");
        }
    }

    private static T Deserialize<T>(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new BadRequestException("A JSON request body is required.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(body, Json)
                ?? throw new BadRequestException("A JSON request body is required.");
        }
        catch (JsonException ex)
        {
            throw new BadRequestException($"The request body is not valid JSON: {ex.Message}");
        }
    }

    private static DateOnly? ParseDate(IReadOnlyDictionary<string, string> query)
    {
        if (!query.TryGetValue("date", out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw new BadRequestException("date must use the yyyy-MM-dd format.");
    }

    private static (string Path, IReadOnlyDictionary<string, string> Query) Split(string url)
    {
        // Accept both relative ("/api/x?y=1") and absolute ("https://0.0.0.1/api/x") URLs.
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http"))
        {
            url = absolute.PathAndQuery;
        }

        var index = url.IndexOf('?');
        var path = (index < 0 ? url : url[..index]).TrimEnd('/');
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (index >= 0)
        {
            foreach (var pair in url[(index + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                query[key] = value;
            }
        }

        return (path.Length == 0 ? "/" : path, query);
    }

    private static LocalApiResponse Ok<T>(T value) => new(200, JsonSerializer.Serialize(value, Json));

    private static LocalApiResponse ValidationProblem(IDictionary<string, string[]> errors) =>
        new(400, JsonSerializer.Serialize(new
        {
            type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            title = "One or more validation errors occurred.",
            status = 400,
            errors
        }, Json));

    private static LocalApiResponse Problem(int status, string title, string? detail) =>
        new(status, JsonSerializer.Serialize(new { title, status, detail }, Json));

    private sealed class BadRequestException(string message) : Exception(message);
}
