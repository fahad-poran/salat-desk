using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Prayer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Color = Android.Graphics.Color;

namespace DeskDashboard.App;

/// <summary>
/// Schedules the Jamaat warning notifications with AlarmManager, so they arrive even when the app is closed.
/// The warnings for the next 7 days are planned in C# (JamaatAlertPlanner) from the stored settings, so no page
/// is needed. They are planned again when the app starts, after a save, whenever one fires, after a reboot or
/// time change, and by a daily alarm, so the 7 days keep rolling forward without opening the app.
/// The channel asks for the red notification light and vibration; whether the light blinks depends on the
/// phone (most only light it while the screen is off).
/// </summary>
public static class JamaatAlerts
{
    public const string ChannelId = "jamaat_alerts";
    private const string CodesKey = "jamaat_alert_codes";
    private const int DailyCode = 1;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Plans and schedules in the background; safe to call often.</summary>
    public static void RescheduleInBackground(Context? context = null)
    {
        var appContext = context?.ApplicationContext ?? Android.App.Application.Context;
        _ = Task.Run(async () =>
        {
            try
            {
                await RescheduleAsync(appContext);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("SalatDesk", $"Scheduling Jamaat alerts failed: {ex}");
            }
        });
    }

    public static async Task<int> RescheduleAsync(Context context)
    {
        await Gate.WaitAsync();
        try
        {
            // Same file as MauiProgram (FileSystem.AppDataDirectory is the app's files folder on Android).
            var dbPath = Path.Combine(context.FilesDir!.AbsolutePath, "dashboard.db");
            if (!File.Exists(dbPath))
            {
                return 0; // never opened: nothing set up yet
            }

            var options = new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite($"Data Source={dbPath}").Options;
            IReadOnlyList<PlannedAlert> alerts;
            await using (var db = new DashboardDbContext(options))
            {
                // Can run after an update before the app is opened: bring the schema up to date first.
                DashboardDatabase.EnsureCreatedAndSeeded(db, null);
                var service = new PrayerService(db, new PrayerTimeCalculator(), NullLogger<PrayerService>.Instance);
                alerts = await service.PlanAlertsAsync(DateTimeOffset.UtcNow);
            }

            var count = Schedule(context, alerts);
            ScheduleDaily(context);
            return count;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static int Schedule(Context context, IReadOnlyList<PlannedAlert> alerts)
    {
        EnsureChannel(context);
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        CancelAll(context, alarms);

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var exact = Build.VERSION.SdkInt < BuildVersionCodes.S || alarms.CanScheduleExactAlarms();
        var codes = new List<int>();

        foreach (var alert in alerts.Where(a => a.WarnAt > now))
        {
            var code = RequestCode(alert.Id);
            var intent = new Intent(context, typeof(JamaatAlertReceiver));
            intent.PutExtra(JamaatAlertReceiver.ExtraTitle, $"{alert.Name} Jamaat in {alert.Minutes} minute{(alert.Minutes == 1 ? "" : "s")}");
            intent.PutExtra(JamaatAlertReceiver.ExtraText, $"Jamaat at {alert.JamaatLabel}. Prepare for prayer.");
            intent.PutExtra(JamaatAlertReceiver.ExtraId, code);

            var pending = PendingIntent.GetBroadcast(
                context, code, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

            if (exact)
            {
                alarms.SetExactAndAllowWhileIdle(AlarmType.RtcWakeup, alert.WarnAt, pending);
            }
            else
            {
                // Android 12+ without the exact-alarm permission: may arrive a few minutes late.
                alarms.SetAndAllowWhileIdle(AlarmType.RtcWakeup, alert.WarnAt, pending);
            }

            codes.Add(code);
        }

        Preferences.Default.Set(CodesKey, string.Join(',', codes));
        return codes.Count;
    }

    // A daily check shortly after midnight keeps the 7-day window filled even if no alert fires
    // (for example when every notification is switched off for a while, then on again).
    private static void ScheduleDaily(Context context)
    {
        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        var intent = new Intent(context, typeof(JamaatRescheduleReceiver));
        var pending = PendingIntent.GetBroadcast(
            context, DailyCode, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        var next = DateTimeOffset.Now.Date.AddDays(1).AddMinutes(10);
        alarms.SetInexactRepeating(
            AlarmType.Rtc,
            new DateTimeOffset(next).ToUnixTimeMilliseconds(),
            AlarmManager.IntervalDay,
            pending);
    }

    public static void EnsureChannel(Context context)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager)context.GetSystemService(Context.NotificationService)!;
        if (manager.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, "Jamaat alerts", NotificationImportance.High)
        {
            Description = "A notice a few minutes before each Jamaat you choose in Settings.",
            LightColor = Color.Red
        };
        channel.EnableLights(true);
        channel.EnableVibration(true);
        channel.SetVibrationPattern([0, 400, 250, 400]);
        manager.CreateNotificationChannel(channel);
    }

    private static void CancelAll(Context context, AlarmManager alarms)
    {
        var stored = Preferences.Default.Get(CodesKey, "");
        foreach (var part in stored.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(part, out var code))
            {
                continue;
            }

            var intent = new Intent(context, typeof(JamaatAlertReceiver));
            var pending = PendingIntent.GetBroadcast(
                context, code, intent, PendingIntentFlags.NoCreate | PendingIntentFlags.Immutable);
            if (pending is not null)
            {
                alarms.Cancel(pending);
                pending.Cancel();
            }
        }

        Preferences.Default.Remove(CodesKey);
    }

    // Stable, positive request code per prayer and Jamaat time ("asr|1794312600000").
    private static int RequestCode(string id)
    {
        var hash = 17;
        foreach (var c in id)
        {
            hash = unchecked(hash * 31 + c);
        }

        return (hash & 0x7FFFFFFF) % 1_000_000 + 1000;
    }
}

/// <summary>Posts the Jamaat warning notification when its alarm fires, then plans the next days.</summary>
[BroadcastReceiver(Enabled = true, Exported = false)]
public sealed class JamaatAlertReceiver : BroadcastReceiver
{
    public const string ExtraTitle = "title";
    public const string ExtraText = "text";
    public const string ExtraId = "id";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null)
        {
            return;
        }

        JamaatAlerts.EnsureChannel(context);

        var open = context.PackageManager?.GetLaunchIntentForPackage(context.PackageName!);
        open?.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        var contentIntent = open is null
            ? null
            : PendingIntent.GetActivity(context, 0, open, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var notification = new NotificationCompat.Builder(context, JamaatAlerts.ChannelId)
            .SetSmallIcon(Android.Resource.Drawable.IcLockIdleAlarm)
            .SetContentTitle(intent.GetStringExtra(ExtraTitle) ?? "Jamaat soon")
            .SetContentText(intent.GetStringExtra(ExtraText) ?? "Prepare for prayer.")
            .SetCategory(NotificationCompat.CategoryAlarm)
            .SetPriority(NotificationCompat.PriorityHigh)
            .SetLights(Color.Red, 500, 500)
            .SetVibrate([0, 400, 250, 400])
            .SetAutoCancel(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(contentIntent)
            .Build();

        NotificationManagerCompat.From(context).Notify(intent.GetIntExtra(ExtraId, 1), notification);

        RescheduleFromReceiver(this, context);
    }

    /// <summary>Plans again without letting Android stop the process half-way (goAsync).</summary>
    internal static void RescheduleFromReceiver(BroadcastReceiver receiver, Context context)
    {
        var pending = receiver.GoAsync();
        var appContext = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            try
            {
                await JamaatAlerts.RescheduleAsync(appContext);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("SalatDesk", $"Rescheduling Jamaat alerts failed: {ex}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }
}

/// <summary>
/// Plans the Jamaat notifications again after a restart, an app update, a clock or time-zone change, and
/// once a day. Android clears scheduled alarms on reboot.
/// </summary>
[BroadcastReceiver(Enabled = true, Exported = true)]
[IntentFilter([
    Intent.ActionBootCompleted,
    Intent.ActionMyPackageReplaced,
    Intent.ActionTimeChanged,
    Intent.ActionTimezoneChanged,
    "android.intent.action.QUICKBOOT_POWERON"])]
public sealed class JamaatRescheduleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            JamaatAlertReceiver.RescheduleFromReceiver(this, context);
        }
    }
}
