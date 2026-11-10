using DeskDashboard.Core;
using DeskDashboard.Core.Content;

namespace DeskDashboard.Web.Features.Content;

public static class ContentEndpoints
{
    /// <summary>
    /// /api/content/* is served by the same LocalApi code the phone app uses, so the PC web host
    /// and the app behave identically.
    /// </summary>
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/api/content/{**rest}", ["GET", "POST", "PUT"], ForwardAsync).WithTags("Content");
        app.MapGet("/api/content", ForwardAsync).WithTags("Content");
        return app;
    }

    private static async Task<IResult> ForwardAsync(HttpContext context, LocalApi api, CancellationToken ct)
    {
        string? body = null;
        if (context.Request.ContentLength > 0 || context.Request.Headers.TransferEncoding.Count > 0)
        {
            using var reader = new StreamReader(context.Request.Body);
            body = await reader.ReadToEndAsync(ct);
        }

        var url = context.Request.Path + context.Request.QueryString;
        var response = await api.HandleAsync(context.Request.Method, url, body, ct);
        return response.Status == 204
            ? Results.NoContent()
            : Results.Content(response.Body, "application/json", statusCode: response.Status);
    }
}

/// <summary>Runs the weekly content update while the PC web host is running.</summary>
public sealed class ContentRefreshHostedService(ContentRefreshService refresh, ILogger<ContentRefreshHostedService> logger)
    : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        ContentRefreshLoop.RunAsync(refresh, logger, TimeSpan.FromSeconds(30), stoppingToken);
}
