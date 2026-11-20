using System.Globalization;
using System.IO.Compression;
using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Export;

/// <summary>Erzeugt Karten-Word-Dateien aus der Schuldatei: eine Datei je Schüler oder ZIP für eine Klasse.</summary>
public static class ReportExport
{
    public const string DocxMime = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    /// <summary>Werte für ein Tag. <c>null</c> = unbekanntes Tag (wird nicht verändert).</summary>
    public static Func<string, string?> ValuesFor(SchoolFile f, Student s, DateOnly today)
    {
        var report = f.Reports.FirstOrDefault(r => r.StudentId == s.Id);
        var de = CultureInfo.InvariantCulture;
        var cls = f.Classes.FirstOrDefault(c => c.Name == s.ClassName);
        var catalog = cls is null ? null : CatalogEditor.For(f, cls.Grade, s.Track);
        var name = $"{s.FirstName} {s.LastName}".Trim();
        var halfYear = $"{f.Term.HalfYear}. Halbjahr";
        return tag => tag switch
        {
            "student.name" => name,
            "student.firstname" => s.FirstName,
            "student.lastname" => s.LastName,
            "student.birthdate" => s.DateOfBirth?.ToString("dd.MM.yyyy", de) ?? "",
            "student.header" => $"{name}, Klasse {s.ClassName}, Schuljahr {f.Term.SchoolYear}, {halfYear}",
            "class.name" => s.ClassName,
            "class.teacher" => f.Teachers.FirstOrDefault(t => t.Id == cls?.ClassTeacherId)?.DisplayName ?? "",
            "school.name" => f.School.Name,
            "school.address" => f.School.AddressLine1,
            "school.town" => f.School.Town,
            "term" => f.Term.ToString(),
            "term.schoolyear" => f.Term.SchoolYear,
            "term.halfyear" => halfYear,
            "date" => today.ToString("dd.MM.yyyy", de),
            _ when FieldKey.TryParse(tag, out var key) => Field(key, report?.Get(tag) ?? ""),
            _ => null,
        };

        string Field(FieldKey key, string value) => key.Kind switch
        {
            FieldKind.Grade => ReportText.GradeWord(value),
            // Auswahl: im Zeugnis steht der Text der gewählten Möglichkeit, mit Platzhaltern ersetzt.
            FieldKind.Selection => catalog?.Sections
                .FirstOrDefault(x => x.Kind == FieldKind.Selection && x.Key == key.Path)?.Items
                .FirstOrDefault(i => i.Id == value) is { } option
                ? ReportText.Fill(option.Text, s, f.Term)
                : value,
            _ => value,
        };
    }

    /// <summary>Ein Zeugnis als .docx für einen Schüler. Die Vorlage richtet sich nach Klasse und Bildungsgang.</summary>
    public static byte[] Student(SchoolFile f, Student s, DateOnly today)
    {
        var cls = MasterData.ClassByName(f, s.ClassName);
        var template = WordTemplates.For(f, cls.Grade, s.Track)
            ?? throw new InvalidOperationException(
                $"Für Klasse {cls.Grade} ({s.Track}) ist keine Word-Vorlage hochgeladen.");
        return TemplateFiller.Fill(template.Content, ValuesFor(f, s, today));
    }

    /// <summary>ZIP mit einer .docx je Schüler der Klasse, sortiert nach Nachname.</summary>
    public static byte[] ClassZip(SchoolFile f, string className, DateOnly today)
    {
        var cls = MasterData.ClassByName(f, className);
        var students = f.Students
            .Where(s => s.ClassName == cls.Name)
            .OrderBy(s => s.LastName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.FirstName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (students.Count == 0)
            throw new InvalidOperationException($"In Klasse {cls.Name} gibt es keine Schüler.");

        using var memory = new MemoryStream();
        using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in students)
            {
                var baseName = $"{Safe(s.LastName)}_{Safe(s.FirstName)}";
                var name = baseName;
                for (var i = 2; !used.Add(name); i++) name = $"{baseName}_{i}";
                using var entryStream = zip.CreateEntry($"{name}.docx").Open();
                entryStream.Write(Student(f, s, today));
            }
        }
        return memory.ToArray();
    }

    /// <summary>Dateiname ohne Zeichen, die Betriebssysteme oder das ZIP-Format nicht mögen.</summary>
    public static string Safe(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray()).Trim('_');
        return cleaned.Length == 0 ? "Schueler" : cleaned;
    }
}
