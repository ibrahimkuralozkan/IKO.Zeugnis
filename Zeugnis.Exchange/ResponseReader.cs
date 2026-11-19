using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public sealed record LoadedResponse(ResponseFile Response, IssuedAssignment Assignment, MergePreview Preview);

/// <summary>
/// Öffnet Rückgaben in der Verwaltung: Zuordnung über den Verweis im Dateikopf,
/// Entschlüsselung mit dem im Auftrag gespeicherten Kennwort, dann Vorschau.
/// </summary>
public static class ResponseReader
{
    public static async Task<LoadedResponse> LoadAsync(EnvelopeCodec codec, SchoolFile school, byte[] bytes, DateTimeOffset now)
    {
        var (kind, reference) = EnvelopeCodec.Peek(bytes);
        if (kind != EnvelopeKind.Response)
            throw new ExchangeSecurityException($"Das ist keine Rückgabe, sondern eine Datei vom Typ „{EnvelopeCodec.Describe(kind)}“.");
        var assignment = school.Assignments.FirstOrDefault(a => reference is not null && ResponseBuilder.Reference(a.AssignmentId) == reference)
            ?? throw new ExchangeSecurityException("Diese Rückgabe gehört zu keinem Auftrag dieser Schuldatei. Wurde die neueste Schuldatei geöffnet?");

        var response = await codec.OpenAsync<ResponseFile>(bytes, EnvelopeKind.Response, assignment.Password);
        if (response.AssignmentId != assignment.AssignmentId)
            throw new ExchangeSecurityException("Die Rückgabe passt nicht zum Auftrag.");
        return new LoadedResponse(response, assignment, MergeEngine.Preview(school, response, now));
    }
}

/// <summary>Lesbare Bezeichnungen und Werte für Vorschau und Konfliktanzeige.</summary>
public static class FieldLabels
{
    public static (string Field, Func<string?, string> Format) Describe(SchoolFile school, Guid studentId, string key)
    {
        var student = school.Students.FirstOrDefault(s => s.Id == studentId);
        var grade = school.Classes.FirstOrDefault(c => c.Name == student?.ClassName)?.Grade ?? 0;
        var catalog = student is null ? null : CatalogEditor.For(school, grade, student.Track);
        if (!FieldKey.TryParse(key, out var fk) || catalog is null) return (key, v => v ?? "–");

        if (fk.Kind == FieldKind.Rating)
        {
            var dot = fk.Path.LastIndexOf('.');
            var section = dot < 0 ? null : catalog.Sections.FirstOrDefault(s => s.Kind == FieldKind.Rating && s.Key == fk.Path[..dot]);
            var item = section?.Items.FirstOrDefault(i => i.Id == fk.Path[(dot + 1)..]);
            var scale = catalog.RatingScale;
            var sectionTitle = string.IsNullOrWhiteSpace(section?.Title) ? fk.Path[..Math.Max(dot, 0)] : section.Title;
            return ($"{sectionTitle}: {item?.Text ?? key}",
                v => int.TryParse(v, out var n) && n >= 1 && n <= scale.Count ? scale[n - 1] : Empty(v));
        }

        var sec = catalog.Sections.FirstOrDefault(s => s.Kind == fk.Kind && s.Key == fk.Path);
        var title = string.IsNullOrWhiteSpace(sec?.Title) ? fk.Path : sec.Title;
        return fk.Kind switch
        {
            FieldKind.Grade => ($"{title} (Note)", Empty),
            FieldKind.Selection => (title, v => sec?.Items.FirstOrDefault(i => i.Id == v)?.Text ?? Empty(v)),
            _ => (title, Empty),
        };
    }

    private static string Empty(string? v) => string.IsNullOrEmpty(v) ? "(leer)" : v;
}
