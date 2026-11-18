namespace DeskDashboard.Core.Data.Entities;

/// <summary>Jamaat warning settings for one prayer ("fajr" … "isha").</summary>
public sealed class AlertPreference
{
    public string Key { get; set; } = "";

    /// <summary>Show a phone notification when the warning starts.</summary>
    public bool Notify { get; set; } = true;

    /// <summary>Minutes before the Jamaat that the warning starts (capped at the Waqt start).</summary>
    public int Minutes { get; set; } = 10;
}
