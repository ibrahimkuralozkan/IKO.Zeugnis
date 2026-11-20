using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>Verwaltung der Word-Vorlagen in der Schuldatei (eine je Jahrgangsstufe und Bildungsgang).</summary>
public static class WordTemplates
{
    public static WordTemplate? For(SchoolFile f, int grade, StudentTrack track) =>
        f.Templates.FirstOrDefault(t => t.Grade == grade && t.Track == track);

    public static void Set(SchoolFile f, int grade, StudentTrack track, string fileName, byte[] content, DateTimeOffset now)
    {
        if (content.Length == 0) throw new MasterDataException("Die Vorlage ist leer.");
        f.Templates.RemoveAll(t => t.Grade == grade && t.Track == track);
        f.Templates.Add(new WordTemplate
        {
            FileName = fileName, Grade = grade, Track = track, Content = content, UploadedUtc = now,
        });
        f.Audit.Add(new AuditEntry(now, "Vorlage hochgeladen", $"{fileName} (Klasse {grade}, {track})"));
        f.UpdatedUtc = now;
        f.Revision++;
    }
}
