using System.Text.RegularExpressions;

namespace DeskDashboard.Core.Content;

public sealed record QuranRef(int Surah, int First, int Last, bool IsSeed)
{
    public string SourceKey => First == Last ? $"quran:{Surah}:{First}" : $"quran:{Surah}:{First}-{Last}";
    public string Label => First == Last ? $"{Surah}:{First}" : $"{Surah}:{First}-{Last}";
}

/// <summary>The curated Quran references (Content/quran-pool.txt, embedded in this assembly).</summary>
public static partial class QuranPool
{
    private const string WeeklyMarker = "===== weekly =====";

    private static readonly Lazy<IReadOnlyList<QuranRef>> Embedded = new(() =>
    {
        using var stream = ContentService.OpenResource("quran-pool.txt");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    public static IReadOnlyList<QuranRef> All => Embedded.Value;

    public static IReadOnlyList<QuranRef> Parse(string text)
    {
        var refs = new List<QuranRef>();
        var isSeed = true;
        foreach (var raw in text.Split('\n'))
        {
            if (raw.Contains(WeeklyMarker, StringComparison.Ordinal))
            {
                isSeed = false;
                continue;
            }

            var hash = raw.IndexOf('#');
            var line = (hash >= 0 ? raw[..hash] : raw).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = RefPattern().Match(line);
            if (!match.Success)
            {
                throw new FormatException($"Bad Quran reference '{line}'.");
            }

            var surah = int.Parse(match.Groups[1].Value);
            var first = int.Parse(match.Groups[2].Value);
            var last = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : first;
            if (surah is < 1 or > 114 || first < 1 || last < first)
            {
                throw new FormatException($"Bad Quran reference '{line}'.");
            }

            refs.Add(new QuranRef(surah, first, last, isSeed));
        }

        return refs;
    }

    [GeneratedRegex(@"^(\d{1,3}):(\d{1,3})(?:-(\d{1,3}))?$")]
    private static partial Regex RefPattern();
}
