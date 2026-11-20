using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public enum ReturnState { Missing, Overdue, Partial, Complete, Closed }

/// <summary>Rücklauf eines Auftrags: Rückgaben und wie viele der freigegebenen Felder in der Schuldatei gefüllt sind.</summary>
public sealed record AssignmentProgress(IssuedAssignment Assignment, Progress Fields, ReturnRecord? LastReturn, ReturnState State);

public static class AssignmentStatus
{
    public static AssignmentProgress Of(SchoolFile f, IssuedAssignment a, DateTimeOffset now)
    {
        var ids = a.StudentIds.ToHashSet();
        var gradeOf = f.Classes.ToDictionary(c => c.Name, c => c.Grade);
        var context = new GradingContext(
            f.Students.Where(s => ids.Contains(s.Id)).ToList(),
            s => CatalogEditor.For(f, gradeOf.GetValueOrDefault(s.ClassName), s.Track),
            f.ReportFor,
            a.AllowedFields,
            "Verwaltung") { Term = f.Term };
        var fields = context.TotalProgress();
        var last = a.Returns.LastOrDefault();
        var state = a.Revoked ? ReturnState.Closed
            : last is null ? (a.ExpiresUtc < now ? ReturnState.Overdue : ReturnState.Missing)
            : fields.IsComplete ? ReturnState.Complete
            : ReturnState.Partial;
        return new AssignmentProgress(a, fields, last, state);
    }

    public static List<AssignmentProgress> All(SchoolFile f, DateTimeOffset now) =>
        f.Assignments.Select(a => Of(f, a, now)).ToList();
}
