using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskDashboard.Core.Content;

/// <summary>
/// Text rules shared by the weekly refresh. tools/content/build-content.mjs applies the same rules
/// to the bundled starter set; keep the two in step.
/// </summary>
public static partial class ContentText
{
    // Display limits: anything longer is not stored, because it cannot fit a phone screen.
    public const int QuranArabicMax = 360;
    public const int QuranEnglishMax = 400;
    public const int HadithArabicMax = 420;
    public const int HadithEnglishMax = 360;
    public const int HadithEnglishMin = 40;

    /// <summary>Removes HTML tags and collapses whitespace.</summary>
    public static string Clean(string? text)
    {
        var value = BreakTag().Replace(text ?? "", " ");
        value = AnyTag().Replace(value, "");
        return Whitespace().Replace(value, " ").Trim();
    }

    /// <summary>Hadith English cleanup: rejoin print hyphenation and space after "Narrated X:".</summary>
    public static string CleanHadithEnglish(string? text)
    {
        var value = Clean(text);
        value = PrintHyphen().Replace(value, "$1$2");
        return ColonNoSpace().Replace(value, "$1: ");
    }

    /// <summary>Narrations that refer to other narrations or only list chains.</summary>
    public static bool IsDependentNarration(string english) => Dependent().IsMatch(english);

    public static bool IsWeakGrade(IEnumerable<string> grades) =>
        grades.Any(grade => WeakGrade().IsMatch(grade));

    /// <summary>Standard (Arabic edition) number, e.g. Muslim 2609.01 → "2609a".</summary>
    public static string ReferenceNumber(string? arabicNumber, int hadithNumber)
    {
        if (!decimal.TryParse(arabicNumber, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            return hadithNumber.ToString(CultureInfo.InvariantCulture);
        }

        var whole = (int)Math.Floor(value);
        var part = (int)Math.Round((value - whole) * 100);
        return part is > 0 and <= 26
            ? $"{whole}{(char)('a' + part - 1)}"
            : whole.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// AlQuran.cloud prefixes the Basmala to ayah 1 of every surah except Al-Fatiha (where it is the
    /// ayah) and At-Tawbah. The Basmala is the first four words.
    /// </summary>
    public static string StripBasmala(string arabic, int surah, int ayah)
    {
        var text = arabic.TrimStart('﻿').Trim();
        if (ayah != 1 || surah is 1 or 9 || !text.StartsWith("بِسْمِ", StringComparison.Ordinal))
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 4 ? string.Join(' ', words.Skip(4)) : text;
    }

    public static string ArabicDigits(int number) =>
        string.Concat(number.ToString(CultureInfo.InvariantCulture).Select(d => "٠١٢٣٤٥٦٧٨٩"[d - '0']));

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BreakTag();

    [GeneratedRegex(@"</?[a-z][^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"([a-z])- ([a-z])")]
    private static partial Regex PrintHyphen();

    [GeneratedRegex(@"([a-z)\]]):(?=[A-Z“""'(])")]
    private static partial Regex ColonNoSpace();

    [GeneratedRegex(@"\b(same as|similar (to|hadith|narration)|previous|aforesaid|above[- ]mentioned|mentioned above|like this|this hadith (has been|was)? ?(narrated|transmitted)|(narrated|transmitted) (this|the|a) (hadith|tradition)|through (another|a different|the same) chain|chain of (narrators|transmitters)|rest of the (hadith|tradition)|in the (narration|version|hadith) (transmitted|narrated)|hadith is the same|but i did not hear|with (this|the following|an?) (addition|change|variation)|with (a|the) (slight|minor) (change|variation|difference)|(words|wording) (are|is) |ditto)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Dependent();

    [GeneratedRegex(@"da'?if|daif|weak|mawdu|fabricated|munkar", RegexOptions.IgnoreCase)]
    private static partial Regex WeakGrade();
}
