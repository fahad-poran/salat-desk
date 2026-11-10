namespace DeskDashboard.App;

/// <summary>Per-window screen brightness (no WRITE_SETTINGS permission needed).</summary>
public static class WindowBrightness
{
    /// <summary>Last requested value, re-applied when the activity resumes.</summary>
    public static float Current { get; private set; } = -1f;

    public static void Set(float value)
    {
        Current = value;
#if ANDROID
        var window = Platform.CurrentActivity?.Window;
        if (window?.Attributes is { } attributes)
        {
            attributes.ScreenBrightness = value;
            window.Attributes = attributes;
        }
#endif
    }

    public static void Reapply() => Set(Current);
}
