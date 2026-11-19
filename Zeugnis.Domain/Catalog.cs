namespace Zeugnis.Domain;

/// <summary>
/// Bewertungskatalog für eine Jahrgangsstufe und einen Bildungsgang.
/// Wird mit der Schuldatei verteilt (nicht mit der App), damit jede Schule eigene Kataloge pflegen kann.
/// </summary>
public sealed class Catalog
{
    /// <summary>Stufen der Ankreuzbewertung, vom besten zum schwächsten Wert. Gespeichert wird die Position (1, 2, …).</summary>
    public static readonly string[] DefaultScale =
        ["trifft voll zu", "trifft weitgehend zu", "trifft teilweise zu", "trifft nicht zu"];

    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Grade { get; set; }
    public StudentTrack Track { get; set; }
    public List<string> RatingScale { get; set; } = [.. DefaultScale];
    public List<CatalogSection> Sections { get; set; } = [];

    /// <summary>Alle Feldschlüssel, die dieser Katalog erzeugt.</summary>
    public IEnumerable<string> FieldKeys() => Sections.SelectMany(s => s.FieldKeys());

    public static string IdFor(int grade, StudentTrack track) => $"K{grade}-{track}";
}

/// <summary>Ein Bereich, z. B. „Deutsch – Lesen“ oder „Arbeitsverhalten“.</summary>
public sealed class CatalogSection
{
    /// <summary>Stabiler Schlüssel ohne Punkt, z. B. <c>Deutsch-Lesen</c>. Ändert sich nie, auch wenn der Titel geändert wird.</summary>
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Fach, zu dem der Bereich gehört (für die Vergabe an Fachlehrkräfte), z. B. „Deutsch“.</summary>
    public string Subject { get; set; } = "";
    public FieldKind Kind { get; set; }
    /// <summary>Kriterien bei <see cref="FieldKind.Rating"/>; Auswahlmöglichkeiten bei <see cref="FieldKind.Selection"/>.</summary>
    public List<CatalogItem> Items { get; set; } = [];
    /// <summary>Zähler für neue Kriterien, damit gelöschte Kennungen nicht erneut vergeben werden.</summary>
    public int NextItemNumber { get; set; } = 1;

    public IEnumerable<string> FieldKeys() => Kind == FieldKind.Rating
        ? Items.Select(i => ItemKey(i.Id))
        : [new FieldKey(Kind, Key).ToString()];

    public string ItemKey(string itemId) => new FieldKey(FieldKind.Rating, $"{Key}.{itemId}").ToString();
}

public sealed class CatalogItem
{
    /// <summary>Stabile Kennung innerhalb des Bereichs („01“, „02“ …). Wird nie wiederverwendet.</summary>
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}
