using Batoulapps.Adhan;
using DeskDashboard.Core.Data.Entities;

namespace DeskDashboard.Core.Prayer;

public sealed record PrayerMethodOption(
    string Key,
    string Label,
    string Description,
    double DefaultFajrAngle,
    double DefaultIshaAngle);

public static class PrayerMethodCatalog
{
    private static readonly Dictionary<string, CalculationMethod> MethodMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Karachi"] = CalculationMethod.KARACHI,
        ["MuslimWorldLeague"] = CalculationMethod.MUSLIM_WORLD_LEAGUE,
        ["NorthAmerica"] = CalculationMethod.NORTH_AMERICA,
        ["Egyptian"] = CalculationMethod.EGYPTIAN,
        ["Dubai"] = CalculationMethod.DUBAI,
        ["Kuwait"] = CalculationMethod.KUWAIT,
        ["Singapore"] = CalculationMethod.SINGAPORE,
        ["MoonsightingCommittee"] = CalculationMethod.MOON_SIGHTING_COMMITTEE,
        ["Custom"] = CalculationMethod.OTHER
    };

    public static IReadOnlyList<PrayerMethodOption> Options { get; } =
    [
        new("Karachi", "University of Islamic Sciences, Karachi", "Fajr 18 degrees, Isha 18 degrees. Default for this dashboard.", 18, 18),
        new("MuslimWorldLeague", "Muslim World League", "Fajr 18 degrees, Isha 17 degrees.", 18, 17),
        new("NorthAmerica", "Islamic Society of North America (ISNA)", "Fajr 15 degrees, Isha 15 degrees.", 15, 15),
        new("Egyptian", "Egyptian General Authority of Survey", "Fajr 19.5 degrees, Isha 17.5 degrees.", 19.5, 17.5),
        new("Dubai", "Dubai", "Fajr 18.2 degrees, Isha 18.2 degrees.", 18.2, 18.2),
        new("Kuwait", "Kuwait", "Fajr 18 degrees, Isha 17.5 degrees.", 18, 17.5),
        new("Singapore", "Singapore", "Fajr 20 degrees, Isha 18 degrees.", 20, 18),
        new("MoonsightingCommittee", "Moonsighting Committee", "Fajr 18 degrees, Isha 18 degrees with seasonal adjustments.", 18, 18),
        new("Custom", "Custom angles", "Use your own Fajr and Isha angles.", 18, 18)
    ];

    public static bool IsSupported(string? key) =>
        !string.IsNullOrWhiteSpace(key) && MethodMap.ContainsKey(key);

    public static PrayerMethodOption GetOption(string key) =>
        Options.First(option => string.Equals(option.Key, key, StringComparison.OrdinalIgnoreCase));

    public static CalculationParameters CreateParameters(PrayerSettings settings)
    {
        var method = MethodMap.TryGetValue(settings.MethodKey, out var mapped)
            ? mapped
            : CalculationMethod.OTHER;

        CalculationParameters parameters;
        if (method == CalculationMethod.OTHER)
        {
            parameters = new CalculationParameters(settings.FajrAngle, settings.IshaAngle, CalculationMethod.OTHER)
            {
                Madhab = ResolveMadhab(settings.AsrMadhab),
                MethodAdjustments = new PrayerAdjustments(),
                IshaInterval = 0
            };
        }
        else
        {
            var baseParameters = method.GetParameters();
            parameters = new CalculationParameters(settings.FajrAngle, settings.IshaAngle, method)
            {
                Madhab = ResolveMadhab(settings.AsrMadhab),
                HighLatitudeRule = baseParameters.HighLatitudeRule,
                MethodAdjustments = baseParameters.MethodAdjustments ?? new PrayerAdjustments(),
                IshaInterval = 0
            };
        }

        parameters.Adjustments = new PrayerAdjustments(
            settings.FajrAdjustment,
            settings.SunriseAdjustment,
            settings.DhuhrAdjustment,
            settings.AsrAdjustment,
            settings.MaghribAdjustment,
            settings.IshaAdjustment);

        return parameters;
    }

    public static Madhab ResolveMadhab(string? asrMadhab) =>
        string.Equals(asrMadhab, "Hanafi", StringComparison.OrdinalIgnoreCase)
            ? Madhab.HANAFI
            : Madhab.SHAFI;
}
