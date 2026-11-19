using System.Text.Json;
using Zeugnis.Domain;

namespace Zeugnis.Exchange;

public sealed record CatalogImportResult(List<string> Imported, List<string> Skipped);

/// <summary>
/// Kataloge als eigene Datei (<c>.zkatalog</c>) weitergeben, z. B. ins nächste Halbjahr oder an eine andere Schule.
/// Kataloge enthalten keine personenbezogenen Daten und werden deshalb unverschlüsselt (JSON) gespeichert.
/// </summary>
public static class CatalogTransfer
{
    public const string Format = "IKO.Zeugnis.Kataloge";
    public const int Version = 1;
    public const string Extension = ".zkatalog";

    private sealed class CatalogFile
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public DateTimeOffset ExportedUtc { get; set; }
        public List<Catalog> Catalogs { get; set; } = [];
    }

    public static byte[] Export(IEnumerable<Catalog> catalogs, DateTimeOffset now) =>
        JsonSerializer.SerializeToUtf8Bytes(new CatalogFile
        {
            Format = Format, Version = Version, ExportedUtc = now,
            Catalogs = catalogs.Select(CatalogEditor.CloneInternal).ToList(),
        }, new JsonSerializerOptions(EnvelopeCodec.Json) { WriteIndented = true });

    /// <summary>Übernimmt Kataloge; vorhandene Jahrgang/Bildungsgang-Kombinationen werden nicht überschrieben.</summary>
    public static CatalogImportResult Import(SchoolFile f, byte[] bytes, DateTimeOffset now)
    {
        CatalogFile file;
        try { file = JsonSerializer.Deserialize<CatalogFile>(bytes, EnvelopeCodec.Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new MasterDataException("Die Datei ist keine gültige Katalogdatei."); }
        if (file.Format != Format || file.Version != Version)
            throw new MasterDataException("Die Datei ist keine gültige Katalogdatei.");

        var imported = new List<string>();
        var skipped = new List<string>();
        foreach (var c in file.Catalogs)
        {
            Validate(c);
            if (CatalogEditor.For(f, c.Grade, c.Track) is not null) { skipped.Add(c.Title); continue; }
            var copy = CatalogEditor.CloneInternal(c);
            copy.Id = Catalog.IdFor(c.Grade, c.Track);
            f.Catalogs.Add(copy);
            imported.Add(c.Title);
        }
        f.Catalogs.Sort((a, b) => a.Grade != b.Grade ? a.Grade.CompareTo(b.Grade) : a.Track.CompareTo(b.Track));
        if (imported.Count > 0) f.Audit.Add(new AuditEntry(now, "Kataloge importiert", string.Join(", ", imported)));
        return new CatalogImportResult(imported, skipped);
    }

    private static void Validate(Catalog c)
    {
        if (c.Grade is < 1 or > 4 || c.RatingScale.Count is < 2 or > CatalogEditor.MaxScaleSteps)
            throw new MasterDataException($"Katalog „{c.Title}“ ist ungültig.");
        foreach (var s in c.Sections)
        {
            if (s.Key.Length is 0 or > 60 || !s.Key.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'))
                throw new MasterDataException($"Katalog „{c.Title}“ enthält einen ungültigen Bereich.");
            if (s.Items.Select(i => i.Id).Distinct().Count() != s.Items.Count || s.Items.Any(i => i.Id.Length == 0 || i.Id.Contains('.')))
                throw new MasterDataException($"Katalog „{c.Title}“ enthält doppelte oder ungültige Einträge.");
        }
        if (c.Sections.GroupBy(s => (s.Kind, s.Key)).Any(g => g.Count() > 1))
            throw new MasterDataException($"Katalog „{c.Title}“ enthält doppelte Bereiche.");
    }
}
