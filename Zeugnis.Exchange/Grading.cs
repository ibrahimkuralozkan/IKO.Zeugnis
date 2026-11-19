using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>Ein Bereich, wie er beim Bewerten angezeigt wird (nur freigegebene Kriterien).</summary>
public sealed record GradingSection(CatalogSection Section, IReadOnlyList<CatalogItem> Items, IReadOnlyList<string> Keys);

public readonly record struct Progress(int Filled, int Total)
{
    public bool IsComplete => Total > 0 && Filled >= Total;
    public static Progress operator +(Progress a, Progress b) => new(a.Filled + b.Filled, a.Total + b.Total);
}

/// <summary>
/// Gemeinsame Grundlage der Bewertungsansicht für Lehrkraft (Arbeitsauftrag) und Verwaltung (Schuldatei):
/// welche Schüler, welche Bereiche, welche Felder dürfen bearbeitet werden, und wie weit ist die Arbeit.
/// </summary>
public sealed class GradingContext
{
    private readonly Func<Student, Catalog?> _catalogFor;
    private readonly Func<Guid, StudentReport> _reportFor;

    public GradingContext(
        IReadOnlyList<Student> students,
        Func<Student, Catalog?> catalogFor,
        Func<Guid, StudentReport> reportFor,
        IReadOnlyList<string>? allowedFields,
        string editorName)
    {
        Students = students;
        _catalogFor = catalogFor;
        _reportFor = reportFor;
        AllowedFields = allowedFields;
        EditorName = editorName;
    }

    public IReadOnlyList<Student> Students { get; }
    /// <summary><c>null</c> = alle Felder (Verwaltung).</summary>
    public IReadOnlyList<string>? AllowedFields { get; }
    public string EditorName { get; }

    public Catalog? CatalogFor(Student s) => _catalogFor(s);
    public StudentReport ReportFor(Student s) => _reportFor(s.Id);

    public bool IsAllowed(string key) => AllowedFields is null || FieldScope.IsAllowed(key, AllowedFields);

    public IReadOnlyList<GradingSection> SectionsFor(Student s)
    {
        var catalog = CatalogFor(s);
        if (catalog is null) return [];
        var result = new List<GradingSection>();
        foreach (var section in catalog.Sections)
        {
            if (section.Kind == FieldKind.Rating)
            {
                var items = section.Items.Where(i => IsAllowed(section.ItemKey(i.Id))).ToList();
                if (items.Count > 0) result.Add(new GradingSection(section, items, items.Select(i => section.ItemKey(i.Id)).ToList()));
            }
            else
            {
                var key = new FieldKey(section.Kind, section.Key).ToString();
                if (IsAllowed(key)) result.Add(new GradingSection(section, section.Items, [key]));
            }
        }
        return result;
    }

    public string Get(Student s, string key) => ReportFor(s).Get(key) ?? "";

    /// <summary>Setzt einen Wert. Liefert <c>false</c>, wenn das Feld nicht freigegeben ist oder sich nichts ändert.</summary>
    public bool Set(Student s, string key, string value, DateTimeOffset now)
    {
        if (!IsAllowed(key) || !Students.Contains(s)) return false;
        return ReportFor(s).Set(key, value, EditorName, now);
    }

    public Progress ProgressOf(Student s)
    {
        var report = ReportFor(s);
        var keys = SectionsFor(s).SelectMany(x => x.Keys).ToList();
        return new Progress(keys.Count(k => (report.Get(k) ?? "").Length > 0), keys.Count);
    }

    public Progress TotalProgress() => Students.Aggregate(new Progress(0, 0), (sum, s) => sum + ProgressOf(s));

    // ---------- Fabriken ----------

    /// <summary>Lehrkraft: bearbeitet die Kopie im Arbeitsauftrag.</summary>
    public static GradingContext ForAssignment(AssignmentFile a)
    {
        var reports = a.Reports.ToDictionary(r => r.StudentId);
        return new GradingContext(
            a.Students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList(),
            s => a.Catalogs.FirstOrDefault(c => c.Track == s.Track),
            id =>
            {
                if (!reports.TryGetValue(id, out var r))
                {
                    r = new StudentReport { StudentId = id };
                    a.Reports.Add(r);
                    reports[id] = r;
                }
                return r;
            },
            a.AllowedFields,
            a.TeacherName);
    }

    /// <summary>Verwaltung: bearbeitet direkt die Schuldatei, alle Felder einer Klasse.</summary>
    public static GradingContext ForSchoolClass(SchoolFile f, string className, string editorName = "Verwaltung")
    {
        var cls = MasterData.ClassByName(f, className);
        return new GradingContext(
            f.Students.Where(s => s.ClassName == cls.Name).OrderBy(s => s.LastName).ThenBy(s => s.FirstName).ToList(),
            s => CatalogEditor.For(f, cls.Grade, s.Track),
            f.ReportFor,
            null,
            editorName);
    }
}
