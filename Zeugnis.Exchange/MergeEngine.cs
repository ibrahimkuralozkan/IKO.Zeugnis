using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public enum MergeOutcome
{
    /// <summary>Lehrkraft hat geändert, Verwaltung nicht → wird übernommen.</summary>
    Apply,
    /// <summary>Beide haben dasselbe Feld unterschiedlich geändert → Entscheidung nötig.</summary>
    Conflict,
}

public enum ConflictChoice { KeepSchool, TakeIncoming }

public sealed record MergeItem(
    Guid StudentId,
    string StudentName,
    string FieldKey,
    MergeOutcome Outcome,
    string? BaselineValue,
    string? SchoolValue,
    string IncomingValue,
    string? SchoolChangedBy);

/// <summary>Vorschau einer Rückgabe. Es wird nichts geschrieben, bis <see cref="MergeEngine.Commit"/> aufgerufen wird.</summary>
public sealed class MergePreview
{
    public required ResponseFile Response { get; init; }
    public required IssuedAssignment Assignment { get; init; }
    public List<MergeItem> Items { get; } = [];
    /// <summary>Werte, die die Lehrkraft nicht verändert hat (werden ignoriert).</summary>
    public int UnchangedCount { get; set; }

    public IEnumerable<MergeItem> Applies => Items.Where(i => i.Outcome == MergeOutcome.Apply);
    public IEnumerable<MergeItem> Conflicts => Items.Where(i => i.Outcome == MergeOutcome.Conflict);
}

public sealed record MergeResult(int Applied, int ConflictsTakenIncoming, int ConflictsKeptSchool, int Unchanged);

/// <summary>
/// Führt Rückgaben in die Schuldatei zusammen.
/// Regeln:
/// 1. Nur Rückgaben zu einem in dieser Schuldatei ausgegebenen, nicht widerrufenen Auftrag.
/// 2. Jede Rückgabe nur einmal.
/// 3. Nur freigegebene Schüler und Felder – sonst wird die ganze Rückgabe abgelehnt.
/// 4. Unveränderte Werte werden ignoriert.
/// 5. Hat sich der Wert in der Schuldatei seit Ausgabe geändert und weicht ab → Konflikt.
/// </summary>
public static class MergeEngine
{
    public static MergePreview Preview(SchoolFile school, ResponseFile response, DateTimeOffset now)
    {
        if (response.FormatVersion != ResponseFile.CurrentFormatVersion)
            throw new ExchangeSecurityException("Diese Rückgabe-Version wird nicht unterstützt.");
        if (response.SchoolFileId != school.SchoolFileId)
            throw new ExchangeSecurityException("Die Rückgabe gehört zu einer anderen Schuldatei.");
        if (school.ProcessedResponseIds.Contains(response.ResponseId))
            throw new ExchangeSecurityException("Diese Rückgabe wurde bereits übernommen.");

        var assignment = school.Assignments.FirstOrDefault(a => a.AssignmentId == response.AssignmentId)
            ?? throw new ExchangeSecurityException("Der zugehörige Arbeitsauftrag ist in dieser Schuldatei nicht vorhanden. Wurde eine ältere Schuldatei geöffnet?");
        if (assignment.Revoked)
            throw new ExchangeSecurityException("Der zugehörige Arbeitsauftrag wurde widerrufen.");
        if (assignment.TeacherId != response.TeacherId)
            throw new ExchangeSecurityException("Die Rückgabe stammt nicht von der beauftragten Lehrkraft.");
        // Hinweis: Die Uhrzeit stammt vom Gerät der Lehrkraft. Der Ablauf ist ein organisatorischer
        // Stichtag, keine Sicherheitsgrenze; die Sicherheit beruht auf Kennwort, Auftragsbindung und Feldfreigabe.
        if (response.CreatedUtc > assignment.ExpiresUtc)
            throw new ExchangeSecurityException("Die Rückgabe wurde nach Ablauf des Arbeitsauftrags erstellt.");

        var allowedStudents = assignment.StudentIds.ToHashSet();
        foreach (var (studentId, fields) in response.Values)
        {
            if (!allowedStudents.Contains(studentId))
                throw new ExchangeSecurityException("Die Rückgabe enthält nicht freigegebene Schüler.");
            var bad = fields.Keys.FirstOrDefault(k => !FieldKey.TryParse(k, out _) || !FieldScope.IsAllowed(k, assignment.AllowedFields));
            if (bad is not null)
                throw new ExchangeSecurityException($"Die Rückgabe enthält ein nicht freigegebenes Feld ({bad}).");
        }

        var preview = new MergePreview { Response = response, Assignment = assignment };
        foreach (var (studentId, fields) in response.Values)
        {
            var name = school.Students.FirstOrDefault(s => s.Id == studentId)?.DisplayName ?? studentId.ToString();
            assignment.Baseline.TryGetValue(studentId, out var baseline);
            var current = school.Reports.FirstOrDefault(r => r.StudentId == studentId);

            foreach (var (key, incoming) in fields.OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                FieldValue? baseValue = null;
                baseline?.TryGetValue(key, out baseValue);
                FieldValue? schoolValue = null;
                current?.Fields.TryGetValue(key, out schoolValue);

                if (incoming == (baseValue?.Value ?? "") || incoming == schoolValue?.Value)
                {
                    preview.UnchangedCount++;
                    continue;
                }
                var schoolChanged = (schoolValue?.Revision ?? 0) != (baseValue?.Revision ?? 0);
                preview.Items.Add(new MergeItem(studentId, name, key,
                    schoolChanged ? MergeOutcome.Conflict : MergeOutcome.Apply,
                    baseValue?.Value, schoolValue?.Value, incoming, schoolChanged ? schoolValue?.ChangedBy : null));
            }
        }
        return preview;
    }

    /// <param name="choices">Entscheidung je Konflikt; fehlt sie, wird der Stand der Schuldatei behalten.</param>
    public static MergeResult Commit(SchoolFile school, MergePreview preview,
        IReadOnlyDictionary<(Guid StudentId, string FieldKey), ConflictChoice> choices, DateTimeOffset now)
    {
        if (school.ProcessedResponseIds.Contains(preview.Response.ResponseId))
            throw new ExchangeSecurityException("Diese Rückgabe wurde bereits übernommen.");

        int applied = 0, taken = 0, kept = 0;
        var by = preview.Assignment.TeacherName;
        foreach (var item in preview.Items)
        {
            if (item.Outcome == MergeOutcome.Conflict &&
                choices.GetValueOrDefault((item.StudentId, item.FieldKey), ConflictChoice.KeepSchool) == ConflictChoice.KeepSchool)
            {
                kept++;
                continue;
            }
            var report = school.ReportFor(item.StudentId);
            report.Set(item.FieldKey, item.IncomingValue, by, now);
            // Neuer Ausgangsstand: Eine spätere Korrektur-Rückgabe derselben Lehrkraft gilt nicht als Konflikt.
            if (!preview.Assignment.Baseline.TryGetValue(item.StudentId, out var baseline))
                preview.Assignment.Baseline[item.StudentId] = baseline = new(StringComparer.Ordinal);
            baseline[item.FieldKey] = report.Fields[item.FieldKey];
            if (item.Outcome == MergeOutcome.Conflict) taken++; else applied++;
        }

        school.ProcessedResponseIds.Add(preview.Response.ResponseId);
        school.Audit.Add(new AuditEntry(now, "Rückgabe übernommen",
            $"{by} · {preview.Assignment.ClassName} · {preview.Assignment.Label} · {applied} übernommen, " +
            $"{taken + kept} Konflikte ({taken} Lehrkraft / {kept} Verwaltung) · {preview.Response.ResponseId:N}"));
        return new MergeResult(applied, taken, kept, preview.UnchangedCount);
    }
}
