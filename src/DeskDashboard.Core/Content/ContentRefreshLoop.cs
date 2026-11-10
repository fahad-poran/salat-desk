namespace DeskDashboard.Core.Content;

/// <summary>Checks every few hours whether the weekly content update is due, and runs it.</summary>
public static class ContentRefreshLoop
{
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    public static async Task RunAsync(
        ContentRefreshService refresh,
        ILogger logger,
        TimeSpan initialDelay,
        CancellationToken ct)
    {
        try
        {
            await Task.Delay(initialDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await refresh.RefreshIfDueAsync(ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Scheduled content check failed.");
                }

                await Task.Delay(CheckInterval, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Host is stopping.
        }
    }
}
