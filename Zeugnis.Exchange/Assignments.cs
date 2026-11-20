using System.Security.Cryptography;
using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public sealed record AssignmentRequest(
    Guid TeacherId,
    IReadOnlyCollection<string> ClassNames,
    string Label,
    IReadOnlyCollection<string> AllowedFields,
    DateTimeOffset ExpiresUtc,
    IReadOnlyCollection<Guid>? StudentIds = null,
    string? Password = null);

/// <summary>Überschneidung mit einem anderen offenen Auftrag (gleiche Schüler, gleiche Felder).</summary>
public sealed record AssignmentOverlap(IssuedAssignment Other, int Students, int Fields);

/// <summary>Erzeugt Arbeitsaufträge aus der Schuldatei (Verwaltungsseite).</summary>
public static class AssignmentFactory
{
    /// <summary>Legt den Auftrag in der Schuldatei an und liefert die Datei für die Lehrkraft.</summary>
    public static AssignmentFile Create(SchoolFile school, AssignmentRequest request, DateTimeOffset now)
    {
        var teacher = school.Teachers.FirstOrDefault(t => t.Id == request.TeacherId)
            ?? throw new MasterDataException("Bitte eine Lehrkraft wählen.");
        var wanted = request.ClassNames.Distinct(StringComparer.Ordinal).ToHashSet();
        if (wanted.Count == 0) throw new MasterDataException("Bitte mindestens eine Klasse wählen.");
        var missing = wanted.FirstOrDefault(n => school.Classes.All(c => c.Name != n));
        if (missing is not null) throw new MasterDataException($"Die Klasse '{missing}' ist nicht vorhanden.");
        var classes = school.Classes.Where(c => wanted.Contains(c.Name)).ToList(); // Reihenfolge der Schuldatei
        if (request.AllowedFields.Count == 0)
            throw new MasterDataException("Bitte mindestens einen Bereich freigeben.");
        var invalid = request.AllowedFields.FirstOrDefault(p => !FieldScope.IsValidPattern(p));
        if (invalid is not null) throw new MasterDataException($"Ungültige Freigabe: '{invalid}'.");
        if (request.ExpiresUtc <= now)
            throw new MasterDataException("Das Abgabedatum muss in der Zukunft liegen.");
        var password = request.Password ?? GeneratePassword();
        EnvelopeCodec.ValidatePassword(password);

        var students = school.Students.Where(s => wanted.Contains(s.ClassName)).ToList();
        if (request.StudentIds is { Count: > 0 } ids)
        {
            if (ids.Except(students.Select(s => s.Id)).Any())
                throw new MasterDataException("Einige Schüler gehören nicht zu den gewählten Klassen.");
            students = students.Where(s => ids.Contains(s.Id)).ToList();
        }
        if (students.Count == 0) throw new MasterDataException("Es ist kein Schüler ausgewählt.");

        var allowed = request.AllowedFields.Distinct(StringComparer.Ordinal).ToList();
        var label = string.IsNullOrWhiteSpace(request.Label) ? "Auftrag" : request.Label.Trim();

        var issued = new IssuedAssignment
        {
            AssignmentId = Guid.NewGuid(),
            TeacherId = teacher.Id,
            TeacherName = teacher.DisplayName,
            ClassName = string.Join(", ", classes.Select(c => c.Name)),
            ClassNames = classes.Select(c => c.Name).ToList(),
            Label = label,
            StudentIds = students.Select(s => s.Id).ToList(),
            AllowedFields = allowed,
            IssuedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            Password = password,
        };
        foreach (var student in students)
        {
            var existing = school.Reports.FirstOrDefault(r => r.StudentId == student.Id);
            issued.Baseline[student.Id] = existing?.Fields
                .Where(f => FieldScope.IsAllowed(f.Key, allowed))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, FieldValue>(StringComparer.Ordinal);
        }

        school.Assignments.Add(issued);
        school.Audit.Add(new AuditEntry(now, "Auftrag erstellt",
            $"{teacher.DisplayName} · {issued.ClassName} · {label} · {students.Count} Schüler · {issued.AssignmentId:N}"));
        return Build(school, issued);
    }

    /// <summary>
    /// Erzeugt die Auftragsdatei (erneut) aus dem gespeicherten Datensatz. Enthält den aktuellen Ausgangsstand,
    /// also auch bereits übernommene Werte dieser Lehrkraft.
    /// </summary>
    public static AssignmentFile Build(SchoolFile school, IssuedAssignment issued)
    {
        var ids = issued.StudentIds.ToHashSet();
        var students = school.Students.Where(s => ids.Contains(s.Id)).ToList();
        var classNames = issued.Classes().ToList();
        var grades = school.Classes.Where(c => classNames.Contains(c.Name)).ToDictionary(c => c.Name, c => c.Grade);
        return new AssignmentFile
        {
            AssignmentId = issued.AssignmentId,
            SchoolFileId = school.SchoolFileId,
            Term = school.Term,
            SchoolName = school.School.Name,
            TeacherId = issued.TeacherId,
            TeacherName = issued.TeacherName,
            ClassName = issued.ClassName,
            ClassNames = classNames,
            ClassGrades = grades,
            Label = issued.Label,
            IssuedUtc = issued.IssuedUtc,
            ExpiresUtc = issued.ExpiresUtc,
            AllowedFields = [.. issued.AllowedFields],
            Students = students,
            Catalogs = CatalogsFor(school, students, issued.AllowedFields),
            Reports = students.Select(s => new StudentReport
            {
                StudentId = s.Id,
                Fields = issued.Baseline.TryGetValue(s.Id, out var b)
                    ? new Dictionary<string, FieldValue>(b, StringComparer.Ordinal)
                    : new Dictionary<string, FieldValue>(StringComparer.Ordinal),
            }).ToList(),
        };
    }

    public static string FileName(AssignmentFile a) =>
        $"Auftrag_{ClassPart(a.ClassNames, a.ClassName)}_{Safe(a.Label)}_{Safe(a.TeacherName)}{EnvelopeCodec.Extension(EnvelopeKind.Assignment)}";

    /// <summary>Klassen für Dateinamen: „3a“ oder „3a+3b“.</summary>
    public static string ClassPart(IReadOnlyCollection<string> classNames, string fallback) =>
        classNames.Count > 0 ? string.Join("+", classNames.Select(Safe)) : Safe(fallback);

    /// <summary>Andere offene Aufträge, die dieselben Schüler und Felder abdecken (führt später zu Konflikten).</summary>
    public static List<AssignmentOverlap> FindOverlaps(SchoolFile school, IReadOnlyCollection<string> classNames, IReadOnlyCollection<Guid> studentIds, IReadOnlyCollection<string> allowed)
    {
        var students = school.Students.Where(s => studentIds.Contains(s.Id)).ToList();
        if (students.Count == 0) return [];
        var keys = CatalogsFor(school, students, allowed).SelectMany(c => c.FieldKeys()).Where(k => FieldScope.IsAllowed(k, allowed)).ToHashSet();
        var result = new List<AssignmentOverlap>();
        foreach (var other in school.Assignments.Where(a => !a.Revoked && a.Classes().Any(classNames.Contains)))
        {
            var sharedStudents = other.StudentIds.Count(studentIds.Contains);
            var sharedFields = keys.Count(k => FieldScope.IsAllowed(k, other.AllowedFields));
            if (sharedStudents > 0 && sharedFields > 0) result.Add(new AssignmentOverlap(other, sharedStudents, sharedFields));
        }
        return result;
    }

    /// <summary>Gut lesbares Zufallskennwort (ohne verwechselbare Zeichen), z. B. <c>K7mP-q4Rx-9tWe-h3Ns</c>.</summary>
    public static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        Span<char> chars = stackalloc char[16];
        for (var i = 0; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return $"{chars[..4]}-{chars[4..8]}-{chars[8..12]}-{chars[12..]}";
    }

    /// <summary>
    /// Kataloge der beteiligten Jahrgänge und Bildungsgänge, gekürzt auf freigegebene Abschnitte und Kriterien.
    /// </summary>
    internal static List<Catalog> CatalogsFor(SchoolFile school, List<Student> students, IReadOnlyCollection<string> allowed)
    {
        var gradeOf = school.Classes.ToDictionary(c => c.Name, c => c.Grade);
        var needed = students.Select(s => (Grade: gradeOf.GetValueOrDefault(s.ClassName), s.Track)).ToHashSet();
        return school.Catalogs
            .Where(c => needed.Contains((c.Grade, c.Track)))
            .Select(c => new Catalog
            {
                Id = c.Id, Title = c.Title, Grade = c.Grade, Track = c.Track, RatingScale = [.. c.RatingScale],
                Sections = c.Sections
                    .Where(s => s.FieldKeys().Any(k => FieldScope.IsAllowed(k, allowed)))
                    .Select(s => s.Kind == FieldKind.Rating ? s.Copy(i => FieldScope.IsAllowed(s.ItemKey(i.Id), allowed)) : s.Copy())
                    .ToList(),
            })
            .Where(c => c.Sections.Count > 0)
            .ToList();
    }

    private static string Safe(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) || c is '/' or '\\' ? '_' : c));
}

/// <summary>Erzeugt die Rückgabe aus einem bearbeiteten Arbeitsauftrag (Lehrkraftseite).</summary>
public static class ResponseBuilder
{
    public static ResponseFile Create(AssignmentFile assignment, DateTimeOffset now)
    {
        var allowedStudents = assignment.Students.Select(s => s.Id).ToHashSet();
        var values = new Dictionary<Guid, Dictionary<string, string>>();
        foreach (var report in assignment.Reports.Where(r => allowedStudents.Contains(r.StudentId)))
        {
            var fields = report.Fields
                .Where(f => FieldScope.IsAllowed(f.Key, assignment.AllowedFields))
                .ToDictionary(f => f.Key, f => f.Value.Value, StringComparer.Ordinal);
            if (fields.Count > 0) values[report.StudentId] = fields;
        }
        return new ResponseFile
        {
            AssignmentId = assignment.AssignmentId,
            SchoolFileId = assignment.SchoolFileId,
            TeacherId = assignment.TeacherId,
            CreatedUtc = now,
            Values = values,
        };
    }

    /// <summary>Verweis im Dateikopf, damit die Verwaltung den Auftrag (und sein Kennwort) findet.</summary>
    public static string Reference(Guid assignmentId) => assignmentId.ToString("N");
}
