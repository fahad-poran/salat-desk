namespace DeskDashboard.Core.Data.Entities;

public sealed class MosqueTimetable
{
    public int Id { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public TimeOnly? Fajr { get; set; }
    public TimeOnly? Dhuhr { get; set; }
    public TimeOnly? Asr { get; set; }
    public TimeOnly? Maghrib { get; set; }
    public TimeOnly? Isha { get; set; }
    // How each prayer's Jamaat is set (JamaatRule as JSON). Null = fixed time, Waqt + 10 min when the
    // Waqt passes it. Added in 0.2.0: DashboardDatabase.MigrateSchema adds the columns to older databases.
    public string? FajrRule { get; set; }
    public string? DhuhrRule { get; set; }
    public string? AsrRule { get; set; }
    public string? MaghribRule { get; set; }
    public string? IshaRule { get; set; }

    public string? Note { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
