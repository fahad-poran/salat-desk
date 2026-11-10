namespace DeskDashboard.Core.Data.Entities;

/// <summary>Single row tracking the bundled seed and the weekly content refresh.</summary>
public sealed class ContentState
{
    public int Id { get; set; } = 1;
    public long? SeededAtMs { get; set; }
    public long? LastRefreshAtMs { get; set; }
    public long? LastAttemptAtMs { get; set; }
    public string? LastResult { get; set; }
    public int LastAddedQuran { get; set; }
    public int LastAddedHadith { get; set; }
}
