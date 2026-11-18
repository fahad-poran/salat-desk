using System.Text.Json;
using DeskDashboard.Core;

namespace DeskDashboard.App;

/// <summary>
/// Hosts the shared dashboard (wwwroot) and answers its calls in-process. The page calls
/// window.HybridWebView.InvokeDotNet('Api' | 'SetBrightness', [...]) instead of fetch().
/// </summary>
public sealed class MainPage : ContentPage
{
    public MainPage(LocalApi api)
    {
        BackgroundColor = Colors.Black;

        var webView = new HybridWebView
        {
            HybridRoot = "wwwroot",
            DefaultFile = "index.html",
            BackgroundColor = Colors.Black
        };
        webView.SetInvokeJavaScriptTarget(new Bridge(api));
        Content = webView;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // The dashboard is meant to stay visible on the desk while the app is open.
        DeviceDisplay.Current.KeepScreenOn = true;
    }

    protected override void OnDisappearing()
    {
        DeviceDisplay.Current.KeepScreenOn = false;
        base.OnDisappearing();
    }

    /// <summary>Methods the page can call.</summary>
    public sealed class Bridge(LocalApi api)
    {
        /// <summary>Returns JSON {"status":200,"body":"..."}.</summary>
        public async Task<string> Api(string method, string url, string? body)
        {
            var response = await api.HandleAsync(method, url, body);
            return JsonSerializer.Serialize(response, LocalApi.Json);
        }

        /// <summary>
        /// Asks once (Android 13+) whether Salat Desk may show notifications. The notifications themselves are
        /// planned natively (JamaatAlerts), from the saved settings.
        /// </summary>
        public async Task<bool> EnsureNotificationPermission()
        {
#if ANDROID
            if (!OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                return true;
            }

            return await MainThread.InvokeOnMainThreadAsync(async () =>
                await Permissions.CheckStatusAsync<Permissions.PostNotifications>() == PermissionStatus.Granted ||
                await Permissions.RequestAsync<Permissions.PostNotifications>() == PermissionStatus.Granted);
#else
            await Task.CompletedTask;
            return true;
#endif
        }

        /// <summary>Copies text (a Library reference) to the phone's clipboard.</summary>
        public async Task<bool> CopyText(string text)
        {
            await MainThread.InvokeOnMainThreadAsync(() => Clipboard.Default.SetTextAsync(text));
            return true;
        }

        /// <summary>
        /// Sets this app window's brightness: 0.0–1.0, or a negative value to follow the system
        /// setting. Only affects the app while it is in front; no system setting is changed.
        /// </summary>
        public bool SetBrightness(double level)
        {
            var value = level < 0 ? -1f : (float)Math.Clamp(level, 0.01, 1.0);
            MainThread.BeginInvokeOnMainThread(() => WindowBrightness.Set(value));
            return true;
        }
    }
}
