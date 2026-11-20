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
    /// <summary>
    /// Optional: Bereich nur zeigen (und im Zeugnis füllen), wenn ein anderes Feld einen Wert hat,
    /// z. B. <c>s:Deutschfoerderung=ja</c> oder <c>s:Teilnahme-Religion!=nein</c>. Siehe <see cref="SectionCondition"/>.
    /// </summary>
    public string? VisibleWhen { get; set; }

    public IEnumerable<string> FieldKeys() => Kind == FieldKind.Rating
        ? Items.Select(i => ItemKey(i.Id))
        : [new FieldKey(Kind, Key).ToString()];

    public string ItemKey(string itemId) => new FieldKey(FieldKind.Rating, $"{Key}.{itemId}").ToString();

    /// <summary>Tiefe Kopie, optional nur mit ausgewählten Einträgen.</summary>
    public CatalogSection Copy(Func<CatalogItem, bool>? keepItem = null) => new()
    {
        Key = Key, Title = Title, Subject = Subject, Kind = Kind, NextItemNumber = NextItemNumber, VisibleWhen = VisibleWhen,
        Items = Items.Where(i => keepItem?.Invoke(i) ?? true)
            .Select(i => new CatalogItem { Id = i.Id, Text = i.Text, ReportText = i.ReportText }).ToList(),
    };
}

public sealed class CatalogItem
{
    /// <summary>Stabile Kennung innerhalb des Bereichs („01“, „02“ …). Wird nie wiederverwendet.</summary>
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>
    /// Nur bei Auswahl: Text im Zeugnis, falls er von der Beschriftung abweicht (leer = nichts drucken).
    /// <c>null</c> = <see cref="Text"/> wird gedruckt. Platzhalter wie <c>{Vorname}</c> oder <c>{n:Stunden}</c> sind erlaubt.
    /// </summary>
    public string? ReportText { get; set; }
}

/// <summary>Bedingung „Feld=Wert“ bzw. „Feld!=Wert“ für <see cref="CatalogSection.VisibleWhen"/>.</summary>
public static class SectionCondition
{
    public static bool IsValid(string? condition) => Parse(condition) is not null;

    /// <summary>Keine Bedingung = sichtbar. Ein leeres Feld zählt als leerer Wert.</summary>
    public static bool IsVisible(string? condition, Func<string, string?> valueOf)
    {
        if (string.IsNullOrWhiteSpace(condition)) return true;
        if (Parse(condition) is not var (key, negate, expected)) return true;
        var equal = (valueOf(key) ?? "") == expected;
        return negate ? !equal : equal;
    }

    /// <summary>Feldschlüssel, von dem die Bedingung abhängt.</summary>
    public static string? KeyOf(string? condition) => Parse(condition)?.Key;

    private static (string Key, bool Negate, string Value)? Parse(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition)) return null;
        var negate = condition.Contains("!=");
        var parts = condition.Split(negate ? "!=" : "=", 2);
        if (parts.Length != 2 || !FieldKey.TryParse(parts[0].Trim(), out var key)) return null;
        return (key.ToString(), negate, parts[1].Trim());
    }
}
