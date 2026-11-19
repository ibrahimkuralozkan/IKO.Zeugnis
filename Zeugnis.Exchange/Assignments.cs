using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public sealed record AssignmentRequest(
    Guid TeacherId,
    string ClassName,
    string Label,
    IReadOnlyCollection<string> AllowedFields,
    DateTimeOffset ExpiresUtc,
    IReadOnlyCollection<Guid>? StudentIds = null);

/// <summary>Erzeugt Arbeitsaufträge aus der Schuldatei (Verwaltungsseite).</summary>
public static class AssignmentFactory
{
    public static AssignmentFile Create(SchoolFile school, AssignmentRequest request, DateTimeOffset now)
    {
        var teacher = school.Teachers.FirstOrDefault(t => t.Id == request.TeacherId)
            ?? throw new InvalidOperationException("Die Lehrkraft ist in der Schuldatei nicht vorhanden.");
        var schoolClass = school.Classes.FirstOrDefault(c => c.Name == request.ClassName)
            ?? throw new InvalidOperationException($"Die Klasse '{request.ClassName}' ist nicht vorhanden.");
        if (request.AllowedFields.Count == 0)
            throw new InvalidOperationException("Es wurde kein Bereich freigegeben.");
        var invalid = request.AllowedFields.FirstOrDefault(p => !FieldScope.IsValidPattern(p));
        if (invalid is not null) throw new InvalidOperationException($"Ungültige Freigabe: '{invalid}'.");
        if (request.ExpiresUtc <= now)
            throw new InvalidOperationException("Das Ablaufdatum muss in der Zukunft liegen.");

        var students = school.Students.Where(s => s.ClassName == schoolClass.Name).ToList();
        if (request.StudentIds is { Count: > 0 } ids)
        {
            var unknown = ids.Except(students.Select(s => s.Id)).ToList();
            if (unknown.Count > 0) throw new InvalidOperationException("Einige Schüler gehören nicht zu dieser Klasse.");
            students = students.Where(s => ids.Contains(s.Id)).ToList();
        }
        if (students.Count == 0) throw new InvalidOperationException("Die Klasse enthält keine Schüler.");

        var allowed = request.AllowedFields.Distinct(StringComparer.Ordinal).ToList();
        var catalogs = CatalogsFor(school, schoolClass.Grade, students, allowed);

        var baseline = new Dictionary<Guid, Dictionary<string, FieldValue>>();
        var reports = new List<StudentReport>();
        foreach (var student in students)
        {
            var existing = school.Reports.FirstOrDefault(r => r.StudentId == student.Id);
            var fields = existing?.Fields
                .Where(f => FieldScope.IsAllowed(f.Key, allowed))
                .ToDictionary(f => f.Key, f => f.Value, StringComparer.Ordinal)
                ?? new Dictionary<string, FieldValue>(StringComparer.Ordinal);
            baseline[student.Id] = new Dictionary<string, FieldValue>(fields, StringComparer.Ordinal);
            reports.Add(new StudentReport { StudentId = student.Id, Fields = new(fields, StringComparer.Ordinal) });
        }

        var assignment = new AssignmentFile
        {
            AssignmentId = Guid.NewGuid(),
            SchoolFileId = school.SchoolFileId,
            Term = school.Term,
            SchoolName = school.School.Name,
            TeacherId = teacher.Id,
            TeacherName = teacher.DisplayName,
            ClassName = schoolClass.Name,
            Label = request.Label,
            IssuedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            AllowedFields = allowed,
            Students = students,
            Catalogs = catalogs,
            Reports = reports,
        };

        school.Assignments.Add(new IssuedAssignment
        {
            AssignmentId = assignment.AssignmentId,
            TeacherId = teacher.Id,
            TeacherName = teacher.DisplayName,
            ClassName = schoolClass.Name,
            Label = request.Label,
            StudentIds = students.Select(s => s.Id).ToList(),
            AllowedFields = allowed,
            IssuedUtc = now,
            ExpiresUtc = request.ExpiresUtc,
            Baseline = baseline,
        });
        school.Audit.Add(new AuditEntry(now, "Auftrag erstellt",
            $"{teacher.DisplayName} · {schoolClass.Name} · {request.Label} · {students.Count} Schüler · {assignment.AssignmentId:N}"));
        return assignment;
    }

    /// <summary>Kataloge der beteiligten Bildungsgänge, gekürzt auf freigegebene Abschnitte.</summary>
    private static List<Catalog> CatalogsFor(SchoolFile school, int grade, List<Student> students, List<string> allowed)
    {
        var tracks = students.Select(s => s.Track).Distinct().ToHashSet();
        return school.Catalogs
            .Where(c => c.Grade == grade && tracks.Contains(c.Track))
            .Select(c => new Catalog
            {
                Id = c.Id, Title = c.Title, Grade = c.Grade, Track = c.Track,
                Sections = c.Sections.Where(s => s.FieldKeys().Any(k => FieldScope.IsAllowed(k, allowed))).ToList(),
            })
            .Where(c => c.Sections.Count > 0)
            .ToList();
    }
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
}
