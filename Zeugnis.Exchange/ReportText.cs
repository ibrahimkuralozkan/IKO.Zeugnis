using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>
/// Platzhalter in Katalogtexten, z. B. in Auswahlmöglichkeiten der Versetzung:
/// <c>{Name} wird in Klasse 4 versetzt.</c> Wird in der Bewertungsansicht und im Word-Zeugnis gleich ersetzt.
/// </summary>
public static class ReportText
{
    public static readonly string[] Placeholders =
        ["{Name}", "{Vorname}", "{Nachname}", "{Klasse}", "{Schuljahresende}", "{ErSie}", "{n:Feld}", "{d:Feld}", "{t:Feld}"];

    private static readonly System.Text.RegularExpressions.Regex FieldPlaceholder =
        new(@"\{([a-z]:[^{}\s]+)\}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Ersetzt Platzhalter. <paramref name="fieldValue"/> liefert Werte für <c>{n:…}</c>, <c>{d:…}</c> usw.;
    /// leere Werte werden als Lücke („__“ bzw. „____“) gedruckt, damit sie im Zeugnis auffallen.
    /// </summary>
    public static string Fill(string text, Student s, Term? term, Func<string, string?>? fieldValue = null)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text;
        var result = text
            .Replace("{Name}", $"{s.FirstName} {s.LastName}".Trim())
            .Replace("{Vorname}", s.FirstName)
            .Replace("{Nachname}", s.LastName)
            .Replace("{Klasse}", s.ClassName)
            .Replace("{Schuljahresende}", SchoolYearEnd(term))
            .Replace("{ErSie}", s.Gender == Gender.Female ? "Sie" : "Er");
        if (fieldValue is null) return result;
        return FieldPlaceholder.Replace(result, m =>
        {
            var key = m.Groups[1].Value;
            var value = fieldValue(key) ?? "";
            return key[0] switch
            {
                'd' => DateText(value) is { Length: > 0 } d ? d : "____",
                'n' => value.Length > 0 ? value : "__",
                _ => value.Length > 0 ? value : "____",
            };
        });
    }

    /// <summary>„2024-03-15“ → „15.03.2024“; anderes bleibt, wie es ist.</summary>
    public static string DateText(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : iso ?? "";

    /// <summary>„2026/27“ → „2027“ (Jahr, in dem das Schuljahr endet).</summary>
    public static string SchoolYearEnd(Term? term)
    {
        var year = term?.SchoolYear ?? "";
        return year.Length == 7 && int.TryParse(year[..4], out var start) ? (start + 1).ToString() : "";
    }

    // Feste Feldschlüssel, aus denen das Zeugnis Sätze bildet (im Katalog als „Zahl“ bzw. „Auswahl“ anlegen).
    public const string AbsenceKey = "n:Fehlstunden";
    public const string UnexcusedKey = "n:Fehlstunden-unentschuldigt";
    public const string LateKey = "n:Verspaetungen";
    public const string SchoolTypeKey = "s:Schulform";
    public const string SchoolTypeLimitedKey = "s:Schulform-eingeschraenkt";

    public static string Weekday(DateOnly d) => d.DayOfWeek switch
    {
        DayOfWeek.Monday => "Montag",
        DayOfWeek.Tuesday => "Dienstag",
        DayOfWeek.Wednesday => "Mittwoch",
        DayOfWeek.Thursday => "Donnerstag",
        DayOfWeek.Friday => "Freitag",
        DayOfWeek.Saturday => "Samstag",
        _ => "Sonntag",
    };

    /// <summary>Note als Wort, wie sie im Zeugnis steht.</summary>
    public static string GradeWord(string? grade) => grade switch
    {
        "1" => "sehr gut",
        "2" => "gut",
        "3" => "befriedigend",
        "4" => "ausreichend",
        "5" => "mangelhaft",
        "6" => "ungenügend",
        _ => grade ?? "",
    };
}
