using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>
/// Die Schuldatei (<c>.schuldatei</c>): vollständiger Stand der Verwaltung.
/// Liegt nur verschlüsselt vor; das Kennwort kennt nur die Verwaltung.
/// </summary>
public sealed class SchoolFile
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid SchoolFileId { get; set; } = Guid.NewGuid();
    /// <summary>Steigt bei jedem Speichern. Verhindert, dass versehentlich ein älterer Stand weiterbearbeitet wird.</summary>
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public Term Term { get; set; } = new("", 1);
    public SchoolInfo School { get; set; } = new();
    public List<Teacher> Teachers { get; set; } = [];
    public List<SchoolClass> Classes { get; set; } = [];
    public List<Student> Students { get; set; } = [];
    public List<Catalog> Catalogs { get; set; } = [];
    public List<StudentReport> Reports { get; set; } = [];
    /// <summary>Word-Vorlagen je Jahrgangsstufe und Bildungsgang.</summary>
    public List<WordTemplate> Templates { get; set; } = [];
    /// <summary>
    /// Ein Kennwort je Lehrkraft für dieses Halbjahr (gilt für alle ihre Aufträge). Liegt nur in der
    /// verschlüsselten Schuldatei, nicht in den Auftragsdateien.
    /// </summary>
    public Dictionary<Guid, string> TeacherPasswords { get; set; } = [];

    /// <summary>Ausgegebene Arbeitsaufträge mit Ausgangsstand (für Konflikterkennung).</summary>
    public List<IssuedAssignment> Assignments { get; set; } = [];
    /// <summary>Bereits zusammengeführte Rückgaben (Schutz gegen doppelten Import).</summary>
    public List<Guid> ProcessedResponseIds { get; set; } = [];
    public List<AuditEntry> Audit { get; set; } = [];

    public StudentReport ReportFor(Guid studentId)
    {
        var report = Reports.FirstOrDefault(r => r.StudentId == studentId);
        if (report is null) { report = new StudentReport { StudentId = studentId }; Reports.Add(report); }
        return report;
    }
}

/// <summary>Verwaltungsseitiger Datensatz zu einem ausgegebenen Arbeitsauftrag.</summary>
public sealed class IssuedAssignment
{
    public Guid AssignmentId { get; set; }
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = "";
    /// <summary>Anzeige, z. B. „3a“ oder „3a, 3b“.</summary>
    public string ClassName { get; set; } = "";
    /// <summary>Alle Klassen des Auftrags (ein Auftrag je Lehrkraft kann mehrere Klassen umfassen).</summary>
    public List<string> ClassNames { get; set; } = [];
    public string Label { get; set; } = "";
    public List<Guid> StudentIds { get; set; } = [];

    /// <summary>Klassen des Auftrags; ältere Schuldateien kennen nur <see cref="ClassName"/>.</summary>
    public IReadOnlyList<string> Classes() => ClassNames.Count > 0 ? ClassNames : [ClassName];
    public List<string> AllowedFields { get; set; } = [];
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    /// <summary>Abgeschlossen: keine weiteren Rückgaben; Stammdaten der Schüler wieder frei änderbar.</summary>
    public bool Revoked { get; set; }
    /// <summary>
    /// Kennwort des Auftrags. Liegt nur in der verschlüsselten Schuldatei, damit Rückgaben ohne Eingabe
    /// zugeordnet und geöffnet werden können und der Auftrag erneut erzeugt werden kann.
    /// </summary>
    public string Password { get; set; } = "";
    /// <summary>Werte zum Zeitpunkt der Ausgabe (bzw. der letzten Übernahme): StudentId → Feldschlüssel → Wert.</summary>
    public Dictionary<Guid, Dictionary<string, FieldValue>> Baseline { get; set; } = [];
    public List<ReturnRecord> Returns { get; set; } = [];
}

/// <summary>Protokoll einer übernommenen Rückgabe.</summary>
public sealed record ReturnRecord(
    Guid ResponseId, DateTimeOffset CreatedUtc, DateTimeOffset ImportedUtc,
    int Applied, int ConflictsTakenIncoming, int ConflictsKeptSchool, int Unchanged);

/// <summary>
/// Arbeitsauftrag (<c>.an-lehrkraft</c>) für eine Lehrkraft: nur ihre Schüler, nur ihre Felder.
/// </summary>
public sealed class AssignmentFile
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid AssignmentId { get; set; }
    public Guid SchoolFileId { get; set; }
    public Term Term { get; set; } = new("", 1);
    public string SchoolName { get; set; } = "";
    public Guid TeacherId { get; set; }
    public string TeacherName { get; set; } = "";
    /// <summary>Anzeige, z. B. „3a“ oder „3a, 3b“.</summary>
    public string ClassName { get; set; } = "";
    public List<string> ClassNames { get; set; } = [];
    /// <summary>Jahrgangsstufe je Klasse, damit jeder Schüler den richtigen Katalog erhält.</summary>
    public Dictionary<string, int> ClassGrades { get; set; } = [];
    public string Label { get; set; } = "";
    public DateTimeOffset IssuedUtc { get; set; }
    public DateTimeOffset ExpiresUtc { get; set; }
    public List<string> AllowedFields { get; set; } = [];
    public List<Student> Students { get; set; } = [];
    /// <summary>Nur die freigegebenen Abschnitte der benötigten Kataloge.</summary>
    public List<Catalog> Catalogs { get; set; } = [];
    /// <summary>Ausgangswerte der freigegebenen Felder.</summary>
    public List<StudentReport> Reports { get; set; } = [];
}

/// <summary>Rückgabe (<c>.an-verwaltung</c>) einer Lehrkraft zu genau einem Arbeitsauftrag.</summary>
public sealed class ResponseFile
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public Guid ResponseId { get; set; } = Guid.NewGuid();
    public Guid AssignmentId { get; set; }
    public Guid SchoolFileId { get; set; }
    public Guid TeacherId { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>StudentId → Feldschlüssel → Wert (nur Werte, keine Revisionen).</summary>
    public Dictionary<Guid, Dictionary<string, string>> Values { get; set; } = [];
}

public sealed record AuditEntry(DateTimeOffset Utc, string Action, string Detail);
