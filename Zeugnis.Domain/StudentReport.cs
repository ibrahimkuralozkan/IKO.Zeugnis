namespace Zeugnis.Domain;

/// <summary>
/// Ein Feldwert mit Revision. Die Revision steigt bei jeder Änderung in der Schuldatei
/// und ist die Grundlage der Konflikterkennung beim Zusammenführen.
/// </summary>
public sealed record FieldValue(string Value, long Revision, string ChangedBy, DateTimeOffset ChangedUtc);

/// <summary>Alle Zeugniswerte eines Schülers im aktuellen Halbjahr.</summary>
public sealed class StudentReport
{
    public Guid StudentId { get; set; }
    /// <summary>Schlüssel = <see cref="FieldKey"/> als String.</summary>
    public Dictionary<string, FieldValue> Fields { get; set; } = new(StringComparer.Ordinal);

    public string? Get(string key) => Fields.TryGetValue(key, out var v) ? v.Value : null;

    /// <summary>Setzt einen Wert und erhöht die Revision. Gleicher Wert ändert nichts.</summary>
    public bool Set(string key, string value, string changedBy, DateTimeOffset now)
    {
        if (!FieldKey.TryParse(key, out _)) throw new ArgumentException($"Ungültiger Feldschlüssel: '{key}'.", nameof(key));
        Fields.TryGetValue(key, out var current);
        if (current is not null && current.Value == value) return false;
        Fields[key] = new FieldValue(value, (current?.Revision ?? 0) + 1, changedBy, now);
        return true;
    }
}
