using System.Globalization;
using System.Text;
using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public enum ImportRowStatus { New, Duplicate, Error }

public sealed record ImportRow(int Line, ImportRowStatus Status, Student? Student, string Message);

public sealed class ImportPreview
{
    public List<ImportRow> Rows { get; } = [];
    /// <summary>Fehler, die den ganzen Import verhindern (z. B. Pflichtspalte fehlt).</summary>
    public string? FatalError { get; set; }
    /// <summary>Hinweise zu fehlenden optionalen Spalten.</summary>
    public List<string> Notes { get; } = [];
    /// <summary>Klassen, die in der Datei vorkommen, aber nicht existieren.</summary>
    public SortedSet<string> MissingClasses { get; } = new(Comparer<string>.Create(MasterData.NaturalCompare));

    public IEnumerable<ImportRow> NewRows => Rows.Where(r => r.Status == ImportRowStatus.New);
}

/// <summary>
/// Schülerliste aus CSV (Excel: „Speichern unter → CSV UTF-8“).
/// Spalten werden über die Überschrift erkannt, die Reihenfolge ist egal.
/// Pflicht: Vorname, Nachname, Klasse. Optional: Geburtsdatum, Geschlecht, Bildungsgang.
/// </summary>
public static class StudentCsvImport
{
    private static readonly Dictionary<string, string[]> Aliases = new()
    {
        ["first"] = ["vorname", "first name", "firstname", "ad"],
        ["last"] = ["nachname", "familienname", "name", "last name", "lastname", "soyad", "soyadı"],
        ["class"] = ["klasse", "class", "sınıf", "sinif"],
        ["dob"] = ["geburtsdatum", "geboren", "geburtstag", "date of birth", "dob", "doğum tarihi"],
        ["gender"] = ["geschlecht", "gender", "cinsiyet"],
        ["track"] = ["bildungsgang", "typ", "förderung", "foerderung", "track", "tür"],
    };

    public const int MaxRows = 2000;

    public static ImportPreview Parse(string text, SchoolFile school, bool createMissingClasses = false)
    {
        var preview = new ImportPreview();
        var lines = SplitRecords(text.TrimStart('﻿'));
        if (lines.Count == 0) { preview.FatalError = "Die Datei ist leer."; return preview; }
        if (lines.Count - 1 > MaxRows) { preview.FatalError = $"Zu viele Zeilen (max. {MaxRows})."; return preview; }

        var separator = DetectSeparator(lines[0].Text);
        var header = SplitFields(lines[0].Text, separator).Select(Normalize).ToList();
        var col = Aliases.ToDictionary(a => a.Key, a => header.FindIndex(h => a.Value.Contains(h)));

        var missing = new[] { ("first", "Vorname"), ("last", "Nachname"), ("class", "Klasse") }
            .Where(m => col[m.Item1] < 0).Select(m => m.Item2).ToList();
        if (missing.Count > 0)
        {
            preview.FatalError = $"Pflichtspalte fehlt: {string.Join(", ", missing)}. Erste Zeile muss die Überschriften enthalten.";
            return preview;
        }
        if (col["dob"] < 0) preview.Notes.Add("Keine Spalte „Geburtsdatum“ – das Feld bleibt leer.");
        if (col["gender"] < 0) preview.Notes.Add("Keine Spalte „Geschlecht“ – „unbekannt“ wird gesetzt.");
        if (col["track"] < 0) preview.Notes.Add("Keine Spalte „Bildungsgang“ – „Regel“ wird gesetzt.");

        var seenInFile = new List<Student>();
        foreach (var (lineNo, raw) in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(raw.Replace(separator.ToString(), ""))) continue;
            var fields = SplitFields(raw, separator);
            string Get(string key) => col[key] >= 0 && col[key] < fields.Count ? fields[col[key]].Trim() : "";

            var errors = new List<string>();
            var first = Get("first");
            var last = Get("last");
            var className = Get("class");
            if (first.Length == 0) errors.Add("Vorname fehlt");
            if (last.Length == 0) errors.Add("Nachname fehlt");
            if (className.Length == 0) errors.Add("Klasse fehlt");

            DateOnly? dob = null;
            var dobText = Get("dob");
            if (dobText.Length > 0)
            {
                if (!TryParseDate(dobText, out var d)) errors.Add($"Geburtsdatum „{dobText}“ unlesbar");
                else if (!MasterData.IsPlausibleBirthDate(d)) errors.Add($"Geburtsdatum „{dobText}“ nicht plausibel");
                else dob = d;
            }

            var gender = ParseGender(Get("gender"), out var genderOk);
            if (!genderOk) errors.Add($"Geschlecht „{Get("gender")}“ unbekannt");
            var track = ParseTrack(Get("track"), out var trackOk);
            if (!trackOk) errors.Add($"Bildungsgang „{Get("track")}“ unbekannt");

            var cls = school.Classes.FirstOrDefault(c => MasterData.Same(c.Name, className));
            if (className.Length > 0 && cls is null)
            {
                if (createMissingClasses) preview.MissingClasses.Add(className.Trim());
                else errors.Add($"Klasse „{className}“ existiert nicht");
            }

            var student = new Student
            {
                FirstName = first, LastName = last, DateOfBirth = dob, Gender = gender, Track = track,
                ClassName = cls?.Name ?? className.Trim(),
            };

            if (errors.Count > 0)
                preview.Rows.Add(new ImportRow(lineNo, ImportRowStatus.Error, student, string.Join("; ", errors)));
            else if (MasterData.IsDuplicate(school, student))
            {
                var existing = school.Students.First(s => MasterData.Same(s.FirstName, first) && MasterData.Same(s.LastName, last) && s.DateOfBirth == dob);
                preview.Rows.Add(new ImportRow(lineNo, ImportRowStatus.Duplicate, student, $"bereits vorhanden in {existing.ClassName}"));
            }
            else if (seenInFile.Any(s => MasterData.Same(s.FirstName, first) && MasterData.Same(s.LastName, last) && s.DateOfBirth == dob))
                preview.Rows.Add(new ImportRow(lineNo, ImportRowStatus.Duplicate, student, "doppelt in der Datei"));
            else
            {
                seenInFile.Add(student);
                preview.Rows.Add(new ImportRow(lineNo, ImportRowStatus.New, student, "neu"));
            }
        }
        if (preview.Rows.Count == 0) preview.FatalError = "Die Datei enthält keine Schülerzeilen.";
        return preview;
    }

    /// <summary>Übernimmt alle neuen Zeilen. Fehlende Klassen werden auf Wunsch angelegt (Jahrgang aus der Bezeichnung).</summary>
    public static int Apply(SchoolFile school, ImportPreview preview, DateTimeOffset now)
    {
        if (preview.FatalError is not null) throw new MasterDataException(preview.FatalError);
        foreach (var name in preview.MissingClasses.Where(n => school.Classes.All(c => !MasterData.Same(c.Name, n))))
        {
            var grade = name.Length > 0 && name[0] is >= '1' and <= '4' ? name[0] - '0' : 0;
            if (grade == 0) throw new MasterDataException($"Für die Klasse „{name}“ kann die Jahrgangsstufe nicht erkannt werden. Bitte die Klasse zuerst anlegen.");
            MasterData.AddClass(school, name, grade, null, now);
        }
        var count = 0;
        foreach (var row in preview.NewRows)
        {
            MasterData.AddStudent(school, row.Student!, now);
            count++;
        }
        school.Audit.Add(new AuditEntry(now, "Schülerliste importiert", $"{count} Schüler"));
        return count;
    }

    // ---------- CSV-Grundlagen ----------

    private static char DetectSeparator(string header)
    {
        var candidates = new[] { ';', '\t', ',' };
        return candidates.OrderByDescending(c => header.Count(ch => ch == c)).First();
    }

    /// <summary>Zerlegt in Datensätze; Zeilenumbrüche innerhalb von Anführungszeichen bleiben erhalten.</summary>
    private static List<(int Line, string Text)> SplitRecords(string text)
    {
        var result = new List<(int, string)>();
        var sb = new StringBuilder();
        var inQuotes = false;
        int line = 1, start = 1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') inQuotes = !inQuotes;
            if (!inQuotes && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                result.Add((start, sb.ToString()));
                sb.Clear();
                line++;
                start = line;
                continue;
            }
            if (c == '\n') line++;
            sb.Append(c);
        }
        if (sb.Length > 0) result.Add((start, sb.ToString()));
        while (result.Count > 0 && string.IsNullOrWhiteSpace(result[^1].Item2)) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static List<string> SplitFields(string line, char separator)
    {
        var fields = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else if (c == '"') inQuotes = true;
            else if (c == separator) { fields.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        fields.Add(sb.ToString());
        return fields;
    }

    private static string Normalize(string header) => header.Trim().Trim('"').ToLowerInvariant();

    private static readonly string[] DateFormats = ["dd.MM.yyyy", "d.M.yyyy", "yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yy", "d.M.yy"];

    public static bool TryParseDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            if (date.Year > DateTime.Today.Year) date = date.AddYears(-100); // zweistellige Jahre
            return true;
        }
        return false;
    }

    private static Gender ParseGender(string text, out bool ok)
    {
        ok = true;
        switch (text.Trim().ToLowerInvariant())
        {
            case "": return Gender.Unknown;
            case "m" or "männlich" or "maennlich" or "male" or "junge" or "erkek" or "e": return Gender.Male;
            case "w" or "weiblich" or "f" or "female" or "mädchen" or "maedchen" or "kız" or "k": return Gender.Female;
            case "d" or "divers" or "diverse": return Gender.Diverse;
            default: ok = false; return Gender.Unknown;
        }
    }

    private static StudentTrack ParseTrack(string text, out bool ok)
    {
        ok = true;
        switch (text.Trim().ToLowerInvariant())
        {
            case "" or "normal" or "regel" or "regelschüler": return StudentTrack.Regular;
            case "se" or "seiteneinsteiger" or "sprachförderung": return StudentTrack.SE;
            case "förder" or "foerder" or "förderschwerpunkt" or "förderbedarf" or "support": return StudentTrack.Support;
            default: ok = false; return StudentTrack.Regular;
        }
    }
}
