using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>Ein Bereich, wie er beim Bewerten angezeigt wird (nur freigegebene Kriterien).</summary>
public sealed record GradingSection(CatalogSection Section, IReadOnlyList<CatalogItem> Items, IReadOnlyList<string> Keys);

/// <summary>Ein einzelnes auszufüllendes Feld mit lesbarer Bezeichnung und aktuellem Status.</summary>
public sealed record GradingFieldStatus(string Key, string Label, bool IsFilled);

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
    private readonly Action<FieldAuditEntry>? _onFieldChanged;

    public GradingContext(
        IReadOnlyList<Student> students,
        Func<Student, Catalog?> catalogFor,
        Func<Guid, StudentReport> reportFor,
        IReadOnlyList<string>? allowedFields,
        string editorName,
        Action<FieldAuditEntry>? onFieldChanged = null)
    {
        Students = students;
        _catalogFor = catalogFor;
        _reportFor = reportFor;
        AllowedFields = allowedFields;
        EditorName = editorName;
        _onFieldChanged = onFieldChanged;
    }

    public IReadOnlyList<Student> Students { get; }
    /// <summary>Halbjahr, für Platzhalter wie {Schuljahresende}.</summary>
    public Term? Term { get; init; }
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
        var report = ReportFor(s);
        foreach (var section in catalog.Sections)
        {
            // Abhängige Bereiche (z. B. Religion bei Nichtteilnahme) erscheinen nicht und zählen nicht zum Fortschritt.
            if (!SectionCondition.IsVisible(section.VisibleWhen, report.Get)) continue;
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

    /// <summary>Wer die (nicht leeren) Werte dieser Felder eingetragen hat – ohne die bearbeitende Person selbst.</summary>
    public IReadOnlyList<string> AuthorsOf(Student s, IEnumerable<string> keys)
    {
        var fields = ReportFor(s).Fields;
        return keys
            .Select(k => fields.TryGetValue(k, out var v) && v.Value.Length > 0 ? v.ChangedBy : null)
            .OfType<string>()
            .Where(by => by.Length > 0 && by != EditorName)
            .Distinct()
            .ToList();
    }

    /// <summary>Setzt einen Wert. Liefert <c>false</c>, wenn das Feld nicht freigegeben ist oder sich nichts ändert.</summary>
    public bool Set(Student s, string key, string value, DateTimeOffset now)
    {
        if (!IsAllowed(key) || !Students.Contains(s)) return false;
        var report = ReportFor(s);
        var oldValue = report.Get(key) ?? "";
        if (!report.Set(key, value, EditorName, now)) return false;
        _onFieldChanged?.Invoke(AuditEntry(s, key, oldValue, value, now));
        return true;
    }

    private FieldAuditEntry AuditEntry(Student student, string key, string oldValue, string newValue, DateTimeOffset now)
    {
        var (label, format) = Describe(student, key);
        return new FieldAuditEntry(
            Guid.NewGuid(), now, student.Id, student.DisplayName, student.ClassName,
            key, label, oldValue, newValue, format(oldValue), format(newValue));
    }

    private (string Label, Func<string?, string> Format) Describe(Student student, string key)
    {
        var catalog = CatalogFor(student);
        if (!FieldKey.TryParse(key, out var fieldKey) || catalog is null)
            return (key, Empty);
        if (fieldKey.Kind == FieldKind.Rating)
        {
            var dot = fieldKey.Path.LastIndexOf('.');
            var section = dot < 0 ? null : catalog.Sections.FirstOrDefault(s =>
                s.Kind == FieldKind.Rating && s.Key == fieldKey.Path[..dot]);
            var item = dot < 0 ? null : section?.Items.FirstOrDefault(i => i.Id == fieldKey.Path[(dot + 1)..]);
            return ($"{section?.Title ?? fieldKey.Path}: {item?.Text ?? key}", value =>
                int.TryParse(value, out var position) && position >= 1 && position <= catalog.RatingScale.Count
                    ? catalog.RatingScale[position - 1]
                    : Empty(value));
        }
        var matching = catalog.Sections.FirstOrDefault(s => s.Kind == fieldKey.Kind && s.Key == fieldKey.Path);
        var title = matching?.Title ?? fieldKey.Path;
        return fieldKey.Kind switch
        {
            FieldKind.Grade => ($"{title} (Note)", value =>
                string.IsNullOrEmpty(value) ? "(leer)" : $"{value} – {ReportText.GradeWord(value)}"),
            FieldKind.Selection => (title, value =>
                matching?.Items.FirstOrDefault(i => i.Id == value)?.Text ?? Empty(value)),
            FieldKind.Date => (title, value =>
                string.IsNullOrEmpty(value) ? "(leer)" : ReportText.DateText(value)),
            _ => (title, Empty),
        };
    }

    private static string Empty(string? value) => string.IsNullOrEmpty(value) ? "(leer)" : value;

    public Progress ProgressOf(Student s)
    {
        var fields = FieldStatusesFor(s);
        return new Progress(fields.Count(f => f.IsFilled), fields.Count);
    }

    /// <summary>Alle sichtbaren und freigegebenen Einzelfelder in Katalogreihenfolge.</summary>
    public IReadOnlyList<GradingFieldStatus> FieldStatusesFor(Student s)
    {
        var report = ReportFor(s);
        var result = new List<GradingFieldStatus>();
        foreach (var group in SectionsFor(s))
        {
            var subject = string.IsNullOrWhiteSpace(group.Section.Subject) ? "Allgemein" : group.Section.Subject;
            if (group.Section.Kind == FieldKind.Rating)
            {
                for (var i = 0; i < group.Keys.Count; i++)
                    result.Add(new GradingFieldStatus(group.Keys[i],
                        $"{subject}: {group.Section.Title} – {group.Items[i].Text}",
                        (report.Get(group.Keys[i]) ?? "").Length > 0));
            }
            else
            {
                var kind = group.Section.Kind switch
                {
                    FieldKind.Grade => "Note", FieldKind.Text => "Text", FieldKind.Selection => "Auswahl",
                    FieldKind.Number => "Zahl", FieldKind.Date => "Datum", _ => group.Section.Kind.ToString(),
                };
                result.Add(new GradingFieldStatus(group.Keys[0], $"{subject}: {group.Section.Title} ({kind})",
                    (report.Get(group.Keys[0]) ?? "").Length > 0));
            }
        }
        return result;
    }

    public Progress TotalProgress() => Students.Aggregate(new Progress(0, 0), (sum, s) => sum + ProgressOf(s));

    /// <summary>Nur Schüler, bei denen mindestens ein aktuell sichtbares/freigegebenes Feld fehlt.</summary>
    public IReadOnlyList<Student> IncompleteStudents() =>
        Students.Where(s => !ProgressOf(s).IsComplete).ToList();

    // ---------- Fabriken ----------

    /// <summary>Lehrkraft: bearbeitet die Kopie im Arbeitsauftrag.</summary>
    public static GradingContext ForAssignment(AssignmentFile a)
    {
        var reports = a.Reports.ToDictionary(r => r.StudentId);
        return new GradingContext(
            a.Students.OrderBy(s => s.ClassName, StringComparer.Ordinal).ThenBy(s => s.LastName).ThenBy(s => s.FirstName).ToList(),
            // Ältere Aufträge (eine Klasse) kennen keine Jahrgangszuordnung: dann nur nach Bildungsgang.
            s => a.ClassGrades.TryGetValue(s.ClassName, out var grade)
                ? a.Catalogs.FirstOrDefault(c => c.Grade == grade && c.Track == s.Track)
                : a.Catalogs.FirstOrDefault(c => c.Track == s.Track),
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
            a.TeacherName,
            a.TeacherAudit.Add)
        { Term = a.Term };
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
            editorName,
            entry => f.Audit.Add(new AuditEntry(
                entry.Utc,
                "Zeugnisfeld geändert",
                $"{entry.ClassName} · {entry.StudentName} · {entry.FieldLabel}: {entry.OldDisplay} → {entry.NewDisplay}",
                entry.StudentId,
                entry.StudentName,
                entry.ClassName,
                entry.FieldKey,
                entry.FieldLabel,
                entry.OldValue,
                entry.NewValue,
                entry.OldDisplay,
                entry.NewDisplay)))
        { Term = f.Term };
    }
}
