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
    public string? Note { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
