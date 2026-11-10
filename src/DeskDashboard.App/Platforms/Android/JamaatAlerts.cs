using System.Text.Json;
using Android.App;
using Android.Content;
using Color = Android.Graphics.Color;
using Android.OS;
using AndroidX.Core.App;

namespace DeskDashboard.App;

/// <summary>One upcoming Jamaat warning, as sent by the page (jamaat-state.js alertSchedule).</summary>
public sealed record JamaatAlert(
    string Id,
    string Key,
    string Name,
    long JamaatAt,
    long WarnAt,
    int Minutes,
    string? JamaatLabel);

/// <summary>
/// Schedules the Jamaat warning notifications with AlarmManager, so they arrive even when the app is
/// in the background. The notification channel asks for the red notification light and vibration;
/// whether the light blinks depends on the phone (most only light it while the screen is off).
/// </summary>
public static class JamaatAlerts
{
    public const string ChannelId = "jamaat_alerts";
    private const string CodesKey = "jamaat_alert_codes";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static int Schedule(string json)
    {
        var context = Android.App.Application.Context;
        EnsureChannel(context);

        var alarms = (AlarmManager)context.GetSystemService(Context.AlarmService)!;
        CancelAll(context, alarms);

        var alerts = JsonSerializer.Deserialize<List<JamaatAlert>>(json, Json) ?? [];
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var exact = Build.VERSION.SdkInt < BuildVersionCodes.S || alarms.CanScheduleExactAlarms();
        var codes = new List<int>();

        foreach (var alert in alerts.Where(a => a.WarnAt > now))
        {
            var code = RequestCode(alert.Id);
            var intent = new Intent(context, typeof(JamaatAlertReceiver));
            intent.PutExtra(JamaatAlertReceiver.ExtraTitle, $"{alert.Name} Jamaat in {alert.Minutes} minute{(alert.Minutes == 1 ? "" : "s")}");
            intent.PutExtra(JamaatAlertReceiver.ExtraText, alert.JamaatLabel is { Length: > 0 } label
                ? $"Jamaat at {label}. Prepare for prayer."
                : "Prepare for prayer.");
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

/// <summary>Posts the Jamaat warning notification when its alarm fires.</summary>
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
    }
}
