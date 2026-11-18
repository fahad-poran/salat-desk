using System.Text.Json;
using DeskDashboard.Core.Data.Entities;

namespace DeskDashboard.Core.Prayer;

/// <summary>
/// How one prayer's Jamaat time is set, chosen by the user for each prayer:
/// <list type="bullet">
/// <item><c>fixed</c>: the time in the timetable. <see cref="Fallback"/> says what to do on days when that
/// time falls outside the prayer window (for example when the Waqt moves past it in winter).</item>
/// <item><c>offset</c>: Waqt start + <see cref="Minutes"/>.</item>
/// <item><c>slot</c>: the first clock time on a <see cref="Slot"/>-minute grid (…:00, :15, :30 for 15) that is at
/// least <see cref="Gap"/> minutes after the Waqt start.</item>
/// </list>
/// A fallback uses the same shape with mode <c>offset</c>, <c>slot</c> or <c>warn</c> (keep the time, report it).
/// </summary>
public sealed record JamaatRule(
    string Mode = JamaatRule.Fixed,
    int? Minutes = null,
    int? Slot = null,
    int? Gap = null,
    JamaatRule? Fallback = null)
{
    public const string Fixed = "fixed";
    public const string Offset = "offset";
    public const string SlotMode = "slot";
    public const string Warn = "warn";

    public static readonly int[] SlotSizes = [5, 10, 15, 20, 30, 60];

    public const int DefaultFallbackMinutes = 10;
    public const int DefaultSlot = 15;
    public const int DefaultGap = 5;

    /// <summary>Fixed time; when the Waqt passes it, Waqt + 10 minutes.</summary>
    public static JamaatRule Default { get; } = new(Fixed, Fallback: new JamaatRule(Offset, DefaultFallbackMinutes));

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>The fallback, or the default one (Waqt + 10). Not serialised: it would loop through Default.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public JamaatRule EffectiveFallback => Fallback ?? Default.Fallback!;

    public static JamaatRule Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Default;
        }

        try
        {
            return JsonSerializer.Deserialize<JamaatRule>(json, Json) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public static string? Serialize(JamaatRule? rule) =>
        rule is null || rule == Default ? null : JsonSerializer.Serialize(Normalize(rule), Json);

    /// <summary>Drops fields that do not apply to the mode, so stored rules stay tidy.</summary>
    public static JamaatRule Normalize(JamaatRule rule) => rule.Mode switch
    {
        Offset => new JamaatRule(Offset, rule.Minutes ?? 0),
        SlotMode => new JamaatRule(SlotMode, Slot: rule.Slot ?? DefaultSlot, Gap: rule.Gap ?? DefaultGap),
        Warn => new JamaatRule(Warn),
        _ => new JamaatRule(Fixed, Fallback: Normalize(rule.EffectiveFallback))
    };

    /// <summary>Validation messages for a rule; <paramref name="isFallback"/> allows <c>warn</c> instead of <c>fixed</c>.</summary>
    public static IEnumerable<string> Errors(JamaatRule rule, bool isFallback = false)
    {
        var modes = isFallback ? new[] { Offset, SlotMode, Warn } : new[] { Fixed, Offset, SlotMode };
        if (!modes.Contains(rule.Mode))
        {
            yield return isFallback
                ? "Choose what happens when the Waqt passes the fixed time."
                : "Choose how the Jamaat time is set.";
            yield break;
        }

        if (rule.Mode == Offset && rule.Minutes is not (>= 0 and <= 120))
        {
            yield return "Minutes after the Waqt must be between 0 and 120.";
        }

        if (rule.Mode == SlotMode)
        {
            if (rule.Slot is not { } slot || !SlotSizes.Contains(slot))
            {
                yield return "Choose a slot size of 5, 10, 15, 20, 30 or 60 minutes.";
            }

            if (rule.Gap is not (>= 0 and <= 60))
            {
                yield return "The gap after the Waqt must be between 0 and 60 minutes.";
            }
        }

        if (rule.Mode == Fixed)
        {
            foreach (var error in Errors(rule.EffectiveFallback, isFallback: true))
            {
                yield return error;
            }
        }
    }

    /// <summary>Short description for the dashboard, e.g. "Waqt + 5 min" or "next 15-min slot".</summary>
    public string Describe() => Mode switch
    {
        Offset => $"Waqt + {Minutes ?? 0} min",
        SlotMode => $"next {Slot ?? DefaultSlot}-min slot, {Gap ?? DefaultGap}+ min after Waqt",
        Warn => "keep the time",
        _ => "fixed time"
    };
}

/// <summary>The Jamaat time a rule gives on one date.</summary>
public sealed record JamaatResolution(
    DateTimeOffset? At,
    bool Valid,
    bool Adjusted,
    string? Note,
    string? Error);

public static class JamaatResolver
{
    public static JamaatRule RuleFor(MosqueTimetable? timetable, string key) => JamaatRule.Parse(key switch
    {
        "fajr" => timetable?.FajrRule,
        "dhuhr" => timetable?.DhuhrRule,
        "asr" => timetable?.AsrRule,
        "maghrib" => timetable?.MaghribRule,
        "isha" => timetable?.IshaRule,
        _ => null
    });

    public static TimeOnly? TimeFor(MosqueTimetable? timetable, string key) => key switch
    {
        "fajr" => timetable?.Fajr,
        "dhuhr" => timetable?.Dhuhr,
        "asr" => timetable?.Asr,
        "maghrib" => timetable?.Maghrib,
        "isha" => timetable?.Isha,
        _ => null
    };

    /// <summary>Resolves the timetable's entry for one prayer on one date. Null when no Jamaat is set.</summary>
    public static JamaatResolution? Resolve(
        MosqueTimetable? timetable,
        string key,
        DateOnly date,
        JamaatWindow window,
        TimeZoneInfo timeZone) =>
        timetable is null ? null : Resolve(RuleFor(timetable, key), TimeFor(timetable, key), date, window, timeZone);

    public static JamaatResolution? Resolve(
        JamaatRule rule,
        TimeOnly? fixedTime,
        DateOnly date,
        JamaatWindow window,
        TimeZoneInfo timeZone)
    {
        if (rule.Mode is JamaatRule.Offset or JamaatRule.SlotMode)
        {
            return Dynamic(rule, window, timeZone, adjustedFrom: null);
        }

        if (fixedTime is null)
        {
            return null;
        }

        var validation = JamaatSchedule.Validate(window.Key, fixedTime, date, window, timeZone);
        if (validation.IsValid)
        {
            return new JamaatResolution(validation.JamaatAt, true, false, null, null);
        }

        var fallback = rule.EffectiveFallback;
        if (fallback.Mode == JamaatRule.Warn)
        {
            return new JamaatResolution(validation.JamaatAt, false, false, null, validation.Error);
        }

        return Dynamic(fallback, window, timeZone, adjustedFrom: fixedTime);
    }

    private static JamaatResolution Dynamic(
        JamaatRule rule,
        JamaatWindow window,
        TimeZoneInfo timeZone,
        TimeOnly? adjustedFrom)
    {
        var latest = window.End.AddMinutes(-1);
        DateTimeOffset at;

        if (rule.Mode == JamaatRule.SlotMode)
        {
            var slot = rule.Slot ?? JamaatRule.DefaultSlot;
            var earliest = TimeZoneInfo.ConvertTime(window.Start.AddMinutes(rule.Gap ?? JamaatRule.DefaultGap), timeZone);
            var minuteOfDay = earliest.Hour * 60 + earliest.Minute;
            var rounded = (minuteOfDay + slot - 1) / slot * slot;
            var local = earliest.Date.AddMinutes(rounded);
            at = new DateTimeOffset(local, timeZone.GetUtcOffset(local));
        }
        else
        {
            at = window.Start.AddMinutes(rule.Minutes ?? 0);
        }

        var capped = false;
        if (at > latest)
        {
            at = latest;
            capped = true;
        }

        if (at < window.Start)
        {
            // An empty window (start == end): nothing fits.
            return new JamaatResolution(null, false, false, null,
                $"{window.Name} has no time for a Jamaat in this window.");
        }

        string? note = null;
        if (adjustedFrom is { } original)
        {
            note = $"Your fixed time {JamaatSchedule.FormatTime(original)} is outside the {window.Name} window today " +
                   $"({JamaatSchedule.DescribeRange(window, timeZone)}), so {JamaatSchedule.FormatTime(at, timeZone)} is used " +
                   $"({rule.Describe()}). Update the Jamaat time when your mosque does.";
        }
        else if (capped)
        {
            note = $"{rule.Describe()} would be after the {window.Name} window, so the last minute " +
                   $"({JamaatSchedule.FormatTime(at, timeZone)}) is used.";
        }

        return new JamaatResolution(at, true, adjustedFrom is not null, note, null);
    }
}
