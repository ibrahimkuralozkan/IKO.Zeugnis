namespace Zeugnis.Domain;

/// <summary>
/// Bewertungskatalog für eine Jahrgangsstufe und einen Bildungsgang.
/// Wird mit der Schuldatei verteilt (nicht mit der App), damit jede Schule eigene Kataloge pflegen kann.
/// </summary>
public sealed class Catalog
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Grade { get; set; }
    public StudentTrack Track { get; set; }
    public List<CatalogSection> Sections { get; set; } = [];

    /// <summary>Alle Feldschlüssel, die dieser Katalog erzeugt.</summary>
    public IEnumerable<string> FieldKeys() => Sections.SelectMany(s => s.FieldKeys());
}

/// <summary>Ein Bereich, z. B. „Deutsch – Lesen“ oder „Arbeitsverhalten“.</summary>
public sealed class CatalogSection
{
    /// <summary>Stabiler Pfad, z. B. <c>Deutsch.Lesen</c>.</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Fach, zu dem der Bereich gehört (für die Vergabe an Fachlehrkräfte), z. B. „Deutsch“.</summary>
    public string Subject { get; set; } = "";
    public FieldKind Kind { get; set; }
    /// <summary>Kriterien bei <see cref="FieldKind.Rating"/>; Auswahlmöglichkeiten bei <see cref="FieldKind.Selection"/>.</summary>
    public List<CatalogItem> Items { get; set; } = [];

    public IEnumerable<string> FieldKeys() => Kind == FieldKind.Rating
        ? Items.Select(i => new FieldKey(FieldKind.Rating, $"{Key}.{i.Id}").ToString())
        : [new FieldKey(Kind, Key).ToString()];
}

public sealed class CatalogItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
