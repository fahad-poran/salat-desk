using DeskDashboard.Core.Prayer;

namespace DeskDashboard.Web.Features.Prayer;

public static class PrayerEndpoints
{
    public static IEndpointRouteBuilder MapPrayerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/prayer").WithTags("Prayer");

        group.MapGet("/today", async (DateOnly? date, PrayerService service, CancellationToken ct) =>
            Results.Ok(await service.BuildTodayAsync(date, ct)));

        group.MapGet("/settings", async (PrayerService service, CancellationToken ct) =>
        {
            var settings = await service.GetSettingsAsync(ct);
            return Results.Ok(new PrayerSettingsResponse(
                service.ToDto(settings),
                PrayerMethodCatalog.Options,
                await service.GetConflictsAsync(ct),
                PrayerService.Disclaimer));
        });

        group.MapPut("/settings", async (
            PrayerSettingsDto input,
            PrayerService service,
            CancellationToken ct) =>
        {
            var result = await service.UpdateSettingsAsync(input, ct);
            if (!result.Ok)
            {
                return Results.ValidationProblem(result.Errors);
            }

            return Results.Ok(new PrayerSettingsResponse(
                service.ToDto(result.Settings!),
                PrayerMethodCatalog.Options,
                await service.GetConflictsAsync(ct),
                PrayerService.Disclaimer));
        });

        group.MapGet("/timetable", async (
            DateOnly? date,
            PrayerService service,
            CancellationToken ct) =>
            Results.Ok(await service.GetTimetableAsync(date, ct)));

        group.MapPut("/timetable", async (
            MosqueTimetableInput input,
            PrayerService service,
            CancellationToken ct) =>
        {
            var result = await service.UpsertTimetableAsync(input, ct);
            return result.Ok
                ? Results.Ok(service.ToDto(result.Timetable!))
                : Results.ValidationProblem(result.Errors);
        });

        return app;
    }
}
