using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Export;

/// <summary>Ein wählbares Fach; <see cref="FromTemplate"/> = nur aus der Word-Vorlage abgeleitet.</summary>
public sealed record SubjectOption(string Name, bool FromTemplate);

/// <summary>
/// Fächer für die Auswahl im Katalog-Editor: alle Fächer der Kataloge dieser Schuldatei, ergänzt um Fächer,
/// die die Word-Vorlage des Jahrgangs/Bildungsgangs verwendet, für die es im Katalog noch keinen Bereich gibt.
/// </summary>
public static class SubjectOptions
{
    public static List<SubjectOption> For(SchoolFile f, Catalog catalog)
    {
        var known = f.Catalogs.SelectMany(c => c.Sections)
            .Select(s => s.Subject.Trim())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var fromTemplate = new List<string>();
        if (WordTemplates.For(f, catalog.Grade, catalog.Track) is { } template)
        {
            var existing = catalog.FieldKeys().ToHashSet(StringComparer.Ordinal);
            try
            {
                foreach (var key in TemplateKeys(template.Content).Where(k => !existing.Contains(k)))
                {
                    var subject = Guess(FieldKey.Parse(key).Path);
                    if (subject.Length > 0 && !known.Concat(fromTemplate).Contains(subject, StringComparer.OrdinalIgnoreCase))
                        fromTemplate.Add(subject);
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { /* unlesbare Vorlage: nur bekannte Fächer */ }
        }

        return known.OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).Select(s => new SubjectOption(s, false))
            .Concat(fromTemplate.OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).Select(s => new SubjectOption(s, true)))
            .ToList();
    }

    /// <summary>Feldschlüssel einer Vorlage: ohne „#Wert“, „a||b“ aufgeteilt, nur gültige Schlüssel.</summary>
    public static IEnumerable<string> TemplateKeys(byte[] template) =>
        TemplateFiller.Tags(template)
            .Select(t => t.Split('#')[0])
            .SelectMany(t => t.Split("||"))
            .Select(t => t.Trim())
            .Where(t => FieldKey.TryParse(t, out _))
            .Distinct(StringComparer.Ordinal);

    /// <summary>Feldschlüssel-Anfänge, deren Fach anders heißt (z. B. „Teilnahme-Religion“ → Religion).</summary>
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SE"] = "Deutsch",
        ["Fehlstunden"] = "Fehlzeiten",
        ["Verspaetungen"] = "Fehlzeiten",
        ["Deutschfoerderung"] = "Deutschförderung",
        ["Schulform"] = "Schulformempfehlung",
        ["Allgemein"] = "Allgemeine Bemerkungen",
    };

    /// <summary>„Deutsch-Lesen.03“ → „Deutsch“, „Teilnahme-Religion“ → „Religion“, „SE-Lesen“ → „Deutsch“.</summary>
    private static string Guess(string path)
    {
        var parts = path.Split('-', '.');
        var head = parts[0].Equals("Teilnahme", StringComparison.OrdinalIgnoreCase) && parts.Length > 1 ? parts[1] : parts[0];
        if (Known.TryGetValue(head, out var mapped)) return mapped;
        return head.Length > 1 && char.IsLetter(head[0]) ? head : "";
    }
}
