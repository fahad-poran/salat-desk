using System.Text.Json;

using DeskDashboard.Core.Location;

namespace DeskDashboard.Web.Features.Location;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/location/search", async (
            string? q,
            GeocodingClient client,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["q"] = ["Enter at least 2 characters."]
                });
            }

            try
            {
                var results = await client.SearchAsync(q.Trim(), ct);
                return Results.Ok(results);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                loggerFactory.CreateLogger("LocationSearch")
                    .LogWarning(ex, "City search failed for {Query}.", q);
                return Results.Problem(
                    "City lookup is unavailable. Enter latitude and longitude manually.",
                    statusCode: StatusCodes.Status502BadGateway);
            }
        });

        return app;
    }
}
