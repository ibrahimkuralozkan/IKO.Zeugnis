using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>
/// Platzhalter in Katalogtexten, z. B. in Auswahlmöglichkeiten der Versetzung:
/// <c>{Name} wird in Klasse 4 versetzt.</c> Wird in der Bewertungsansicht und im Word-Zeugnis gleich ersetzt.
/// </summary>
public static class ReportText
{
    public static readonly string[] Placeholders = ["{Name}", "{Vorname}", "{Nachname}", "{Klasse}", "{Schuljahresende}"];

    public static string Fill(string text, Student s, Term? term) =>
        string.IsNullOrEmpty(text) || !text.Contains('{') ? text : text
            .Replace("{Name}", $"{s.FirstName} {s.LastName}".Trim())
            .Replace("{Vorname}", s.FirstName)
            .Replace("{Nachname}", s.LastName)
            .Replace("{Klasse}", s.ClassName)
            .Replace("{Schuljahresende}", SchoolYearEnd(term));

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
