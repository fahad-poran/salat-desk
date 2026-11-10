namespace DeskDashboard.Core.Data.Entities;

public sealed class PrayerSettings
{
    public int Id { get; set; } = 1;
    public string Country { get; set; } = "Bangladesh";
    public string City { get; set; } = "Dhaka";
    public double Latitude { get; set; } = 23.8103;
    public double Longitude { get; set; } = 90.4125;
    public string TimeZoneId { get; set; } = "Asia/Dhaka";
    public string MethodKey { get; set; } = "Karachi";
    public double FajrAngle { get; set; } = 18;
    public double IshaAngle { get; set; } = 18;
    public string AsrMadhab { get; set; } = "Hanafi";
    public int FajrAdjustment { get; set; }
    public int SunriseAdjustment { get; set; }
    public int DhuhrAdjustment { get; set; }
    public int AsrAdjustment { get; set; }
    public int MaghribAdjustment { get; set; }
    public int IshaAdjustment { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
