namespace Zeugnis.Domain;

/// <summary>Art eines Zeugnisfeldes.</summary>
public enum FieldKind
{
    /// <summary>Ankreuz-Bewertung (1..5) eines Katalogkriteriums.</summary>
    Rating,
    /// <summary>Freitext (z. B. Bemerkungen, Allgemein).</summary>
    Text,
    /// <summary>Note / Gesamtnote eines Fachs.</summary>
    Grade,
    /// <summary>Auswahlwert (z. B. Versetzungsentscheidung).</summary>
    Selection,
    /// <summary>Ganze Zahl ≥ 0 (z. B. Fehlstunden).</summary>
    Number,
}

/// <summary>
/// Eindeutiger, stabiler Schlüssel eines Zeugnisfeldes, z. B. <c>r:Deutsch.Lesen.03</c>,
/// <c>t:Allgemein</c>, <c>g:Mathematik</c> oder <c>s:Versetzung</c>.
/// Die String-Form ist Teil des Dateiformats und darf sich nicht ändern.
/// </summary>
public readonly record struct FieldKey
{
    private static readonly Dictionary<FieldKind, char> Prefix = new()
    {
        [FieldKind.Rating] = 'r',
        [FieldKind.Text] = 't',
        [FieldKind.Grade] = 'g',
        [FieldKind.Selection] = 's',
        [FieldKind.Number] = 'n',
    };

    public FieldKind Kind { get; }
    public string Path { get; }

    public FieldKey(FieldKind kind, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Any(c => char.IsWhiteSpace(c) || c is ':' or '*'))
            throw new ArgumentException($"Ungültiger Feldpfad: '{path}'.", nameof(path));
        Kind = kind;
        Path = path;
    }

    public static FieldKey Rating(string path) => new(FieldKind.Rating, path);
    public static FieldKey Text(string path) => new(FieldKind.Text, path);
    public static FieldKey Grade(string path) => new(FieldKind.Grade, path);
    public static FieldKey Selection(string path) => new(FieldKind.Selection, path);
    public static FieldKey Number(string path) => new(FieldKind.Number, path);

    public override string ToString() => $"{Prefix[Kind]}:{Path}";

    public static FieldKey Parse(string value)
    {
        if (!TryParse(value, out var key))
            throw new FormatException($"Ungültiger Feldschlüssel: '{value}'.");
        return key;
    }

    public static bool TryParse(string? value, out FieldKey key)
    {
        key = default;
        if (value is null || value.Length < 3 || value[1] != ':') return false;
        var kind = Prefix.FirstOrDefault(p => p.Value == value[0]);
        if (kind.Value == default) return false;
        try { key = new FieldKey(kind.Key, value[2..]); return true; }
        catch (ArgumentException) { return false; }
    }
}

/// <summary>
/// Freigabemuster für Felder: entweder ein exakter Schlüssel (<c>g:Sport</c>)
/// oder ein Präfix mit Stern (<c>r:Deutsch.*</c>, <c>t:*</c>).
/// </summary>
public static class FieldScope
{
    public static bool IsAllowed(string key, IEnumerable<string> patterns) =>
        patterns.Any(p => p.EndsWith('*') ? key.StartsWith(p[..^1], StringComparison.Ordinal) : key == p);

    /// <summary>Gültig: exakter Schlüssel, oder Präfix mit Stern am Ende von „:“ oder „.“.</summary>
    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (!pattern.EndsWith('*')) return FieldKey.TryParse(pattern, out _);
        var prefix = pattern[..^1];
        return (prefix.EndsWith(':') || prefix.EndsWith('.')) && FieldKey.TryParse(prefix + "x", out _);
    }
}
