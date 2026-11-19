using System.Globalization;
using System.Text;
using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>
/// Pflege der Bewertungskataloge in der Schuldatei.
/// Grundsätze: Schlüssel von Bereichen und Kriterien sind stabil (Titeländerungen ändern sie nicht),
/// gelöschte Kennungen werden nie neu vergeben, und Löschen mit vorhandenen Zeugniswerten
/// geschieht nur ausdrücklich (die Anzahl betroffener Werte kann vorher abgefragt werden).
/// </summary>
public static class CatalogEditor
{
    public const int MaxScaleSteps = 6;

    // ---------- Kataloge ----------

    public static Catalog Create(SchoolFile f, int grade, StudentTrack track, DateTimeOffset now)
    {
        EnsureFree(f, grade, track);
        var catalog = new Catalog { Id = Catalog.IdFor(grade, track), Grade = grade, Track = track, Title = DefaultTitle(grade, track) };
        Add(f, catalog);
        Log(f, now, "Katalog angelegt", catalog.Title);
        return catalog;
    }

    /// <summary>Kopiert einen Katalog auf eine andere Jahrgangsstufe bzw. einen anderen Bildungsgang (Schlüssel bleiben gleich).</summary>
    public static Catalog Copy(SchoolFile f, string sourceId, int grade, StudentTrack track, DateTimeOffset now)
    {
        var source = ById(f, sourceId);
        EnsureFree(f, grade, track);
        var copy = Clone(source);
        copy.Id = Catalog.IdFor(grade, track);
        copy.Grade = grade;
        copy.Track = track;
        copy.Title = DefaultTitle(grade, track);
        Add(f, copy);
        Log(f, now, "Katalog kopiert", $"{source.Title} → {copy.Title}");
        return copy;
    }

    public static void Remove(SchoolFile f, string id, DateTimeOffset now)
    {
        var catalog = ById(f, id);
        f.Catalogs.Remove(catalog);
        Log(f, now, "Katalog entfernt", catalog.Title);
    }

    public static void SetTitle(SchoolFile f, string id, string title)
    {
        ById(f, id).Title = Text(title, "Bitte einen Titel angeben.", 80);
    }

    public static void SetScale(SchoolFile f, string id, IReadOnlyList<string> labels)
    {
        var clean = labels.Select(l => l?.Trim() ?? "").ToList();
        if (clean.Count is < 2 or > MaxScaleSteps)
            throw new MasterDataException($"Die Bewertungsskala braucht 2 bis {MaxScaleSteps} Stufen.");
        if (clean.Any(l => l.Length is 0 or > 40))
            throw new MasterDataException("Jede Stufe braucht eine Bezeichnung (max. 40 Zeichen).");
        var catalog = ById(f, id);
        if (clean.Count < catalog.RatingScale.Count)
        {
            var used = UsedValues(f, catalog, catalog.Sections.Where(s => s.Kind == FieldKind.Rating).SelectMany(s => s.FieldKeys()))
                .Count(v => int.TryParse(v, out var n) && n > clean.Count);
            if (used > 0)
                throw new MasterDataException($"{used} Bewertungen nutzen eine Stufe, die wegfallen würde.");
        }
        catalog.RatingScale = clean;
    }

    // ---------- Bereiche ----------

    public static CatalogSection AddSection(SchoolFile f, string catalogId, string title, string subject, FieldKind kind, DateTimeOffset now)
    {
        var catalog = ById(f, catalogId);
        var cleanTitle = Text(title, "Bitte einen Titel für den Bereich angeben.", 80);
        var key = UniqueKey(catalog, Slug(cleanTitle), kind);
        var section = new CatalogSection { Key = key, Title = cleanTitle, Subject = (subject ?? "").Trim(), Kind = kind };
        if (kind == FieldKind.Selection)
        {
            section.Items.Add(new CatalogItem { Id = "01", Text = "Option 1" });
            section.NextItemNumber = 2;
        }
        catalog.Sections.Add(section);
        Log(f, now, "Bereich angelegt", $"{catalog.Title} · {cleanTitle}");
        return section;
    }

    public static void UpdateSection(SchoolFile f, string catalogId, FieldKind kind, string key, string title, string subject)
    {
        var section = Section(f, catalogId, kind, key);
        section.Title = Text(title, "Bitte einen Titel für den Bereich angeben.", 80);
        section.Subject = (subject ?? "").Trim();
    }

    public static void MoveSection(SchoolFile f, string catalogId, FieldKind kind, string key, int delta)
    {
        var catalog = ById(f, catalogId);
        Move(catalog.Sections, Section(f, catalogId, kind, key), delta);
    }

    /// <summary>Anzahl eingetragener Werte, die beim Löschen des Bereichs verloren gingen.</summary>
    public static int CountSectionValues(SchoolFile f, string catalogId, FieldKind kind, string key) =>
        UsedValues(f, ById(f, catalogId), Section(f, catalogId, kind, key).FieldKeys()).Count();

    public static void RemoveSection(SchoolFile f, string catalogId, FieldKind kind, string key, DateTimeOffset now)
    {
        var catalog = ById(f, catalogId);
        var section = Section(f, catalogId, kind, key);
        EnsureNoOpenAssignment(f, catalog, section.FieldKeys());
        var removed = RemoveValues(f, catalog, section.FieldKeys());
        catalog.Sections.Remove(section);
        Log(f, now, "Bereich entfernt", $"{catalog.Title} · {section.Title}{(removed > 0 ? $" · {removed} Werte gelöscht" : "")}");
    }

    // ---------- Kriterien / Auswahlmöglichkeiten ----------

    public static CatalogItem AddItem(SchoolFile f, string catalogId, FieldKind kind, string key, string text)
    {
        var section = Section(f, catalogId, kind, key);
        RequireItems(section);
        var number = Math.Max(section.NextItemNumber,
            section.Items.Select(i => int.TryParse(i.Id, out var n) ? n + 1 : 1).DefaultIfEmpty(1).Max());
        var item = new CatalogItem { Id = number.ToString("00", CultureInfo.InvariantCulture), Text = Text(text, "Bitte einen Text angeben.", 400) };
        section.Items.Add(item);
        section.NextItemNumber = number + 1;
        return item;
    }

    public static void UpdateItem(SchoolFile f, string catalogId, FieldKind kind, string key, string itemId, string text)
    {
        Item(Section(f, catalogId, kind, key), itemId).Text = Text(text, "Bitte einen Text angeben.", 400);
    }

    public static void MoveItem(SchoolFile f, string catalogId, FieldKind kind, string key, string itemId, int delta)
    {
        var section = Section(f, catalogId, kind, key);
        Move(section.Items, Item(section, itemId), delta);
    }

    public static int CountItemValues(SchoolFile f, string catalogId, FieldKind kind, string key, string itemId)
    {
        var catalog = ById(f, catalogId);
        var section = Section(f, catalogId, kind, key);
        return kind == FieldKind.Rating
            ? UsedValues(f, catalog, [section.ItemKey(itemId)]).Count()
            : UsedValues(f, catalog, section.FieldKeys()).Count(v => v == itemId);
    }

    public static void RemoveItem(SchoolFile f, string catalogId, FieldKind kind, string key, string itemId, DateTimeOffset now)
    {
        var catalog = ById(f, catalogId);
        var section = Section(f, catalogId, kind, key);
        var item = Item(section, itemId);
        if (kind == FieldKind.Selection && section.Items.Count <= 2)
            throw new MasterDataException("Eine Auswahl braucht mindestens zwei Möglichkeiten.");
        if (kind == FieldKind.Rating)
        {
            EnsureNoOpenAssignment(f, catalog, [section.ItemKey(itemId)]);
            RemoveValues(f, catalog, [section.ItemKey(itemId)]);
        }
        else
        {
            EnsureNoOpenAssignment(f, catalog, section.FieldKeys());
            RemoveValues(f, catalog, section.FieldKeys(), v => v == itemId);
        }
        section.Items.Remove(item);
        Log(f, now, "Eintrag entfernt", $"{catalog.Title} · {section.Title} · {item.Text}");
    }

    // ---------- Hilfen ----------

    public static string DefaultTitle(int grade, StudentTrack track) => track switch
    {
        StudentTrack.SE => $"Klasse {grade} – SE",
        StudentTrack.Support => $"Klasse {grade} – Förderbedarf",
        _ => $"Klasse {grade}",
    };

    public static Catalog ById(SchoolFile f, string id) =>
        f.Catalogs.FirstOrDefault(c => c.Id == id) ?? throw new MasterDataException("Der Katalog ist nicht vorhanden.");

    public static Catalog? For(SchoolFile f, int grade, StudentTrack track) =>
        f.Catalogs.FirstOrDefault(c => c.Grade == grade && c.Track == track);

    /// <summary>
    /// Stabiler Schlüssel aus einem Titel: Umlaute umschreiben, nur Buchstaben/Ziffern, Wortgrenzen als „-“.
    /// Kein Punkt, damit Freigabemuster wie <c>r:Deutsch.*</c> nur genau diesen Bereich treffen.
    /// </summary>
    public static string Slug(string title)
    {
        var sb = new StringBuilder();
        var dash = false;
        foreach (var ch in title.Trim())
        {
            var s = ch switch
            {
                'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'Ä' => "Ae", 'Ö' => "Oe", 'Ü' => "Ue", 'ß' => "ss",
                _ => char.IsAsciiLetterOrDigit(ch) ? ch.ToString() : null,
            };
            if (s is null)
            {
                var folded = RemoveDiacritics(ch);
                s = folded is { } c && char.IsAsciiLetterOrDigit(c) ? c.ToString() : null;
            }
            if (s is null) { dash = sb.Length > 0; continue; }
            if (dash) sb.Append('-');
            dash = false;
            sb.Append(s);
        }
        var result = sb.ToString();
        if (result.Length > 40) result = result[..40].TrimEnd('-');
        return result.Length > 0 ? result : "Bereich";
    }

    private static char? RemoveDiacritics(char ch)
    {
        var normalized = ch.ToString().Normalize(NormalizationForm.FormD);
        foreach (var c in normalized)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) return c;
        return null;
    }

    private static string UniqueKey(Catalog catalog, string baseKey, FieldKind kind)
    {
        var key = baseKey;
        for (var n = 2; catalog.Sections.Any(s => s.Kind == kind && string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase)); n++)
            key = $"{baseKey}-{n}";
        return key;
    }

    private static void Add(SchoolFile f, Catalog catalog)
    {
        f.Catalogs.Add(catalog);
        f.Catalogs.Sort((a, b) => a.Grade != b.Grade ? a.Grade.CompareTo(b.Grade) : a.Track.CompareTo(b.Track));
    }

    private static void EnsureFree(SchoolFile f, int grade, StudentTrack track)
    {
        if (grade is < 1 or > 4) throw new MasterDataException("Die Jahrgangsstufe muss zwischen 1 und 4 liegen.");
        if (For(f, grade, track) is { } existing)
            throw new MasterDataException($"„{existing.Title}“ ist bereits vorhanden.");
    }

    private static CatalogSection Section(SchoolFile f, string catalogId, FieldKind kind, string key) =>
        ById(f, catalogId).Sections.FirstOrDefault(s => s.Kind == kind && s.Key == key)
        ?? throw new MasterDataException("Der Bereich ist nicht vorhanden.");

    private static CatalogItem Item(CatalogSection section, string id) =>
        section.Items.FirstOrDefault(i => i.Id == id) ?? throw new MasterDataException("Der Eintrag ist nicht vorhanden.");

    private static void RequireItems(CatalogSection section)
    {
        if (section.Kind is not (FieldKind.Rating or FieldKind.Selection))
            throw new MasterDataException("Dieser Bereich hat keine Einträge.");
    }

    private static void Move<T>(List<T> list, T item, int delta)
    {
        var index = list.IndexOf(item);
        var target = Math.Clamp(index + delta, 0, list.Count - 1);
        if (index < 0 || target == index) return;
        list.RemoveAt(index);
        list.Insert(target, item);
    }

    /// <summary>Schüler, deren Werte zu diesem Katalog gehören (gleiche Jahrgangsstufe und gleicher Bildungsgang).</summary>
    private static HashSet<Guid> StudentsOf(SchoolFile f, Catalog catalog)
    {
        var classes = f.Classes.Where(c => c.Grade == catalog.Grade).Select(c => c.Name).ToHashSet();
        return f.Students.Where(s => s.Track == catalog.Track && classes.Contains(s.ClassName)).Select(s => s.Id).ToHashSet();
    }

    private static IEnumerable<string> UsedValues(SchoolFile f, Catalog catalog, IEnumerable<string> keys)
    {
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        var students = StudentsOf(f, catalog);
        return f.Reports
            .Where(r => students.Contains(r.StudentId))
            .SelectMany(r => r.Fields.Where(kv => keySet.Contains(kv.Key) && kv.Value.Value.Length > 0).Select(kv => kv.Value.Value));
    }

    private static int RemoveValues(SchoolFile f, Catalog catalog, IEnumerable<string> keys, Func<string, bool>? when = null)
    {
        var keySet = keys.ToHashSet(StringComparer.Ordinal);
        var students = StudentsOf(f, catalog);
        var removed = 0;
        foreach (var report in f.Reports.Where(r => students.Contains(r.StudentId)))
            foreach (var key in report.Fields.Keys.Where(keySet.Contains).ToList())
                if (when is null || when(report.Fields[key].Value))
                {
                    report.Fields.Remove(key);
                    removed++;
                }
        return removed;
    }

    private static void EnsureNoOpenAssignment(SchoolFile f, Catalog catalog, IEnumerable<string> keys)
    {
        var students = StudentsOf(f, catalog);
        var keyList = keys.ToList();
        var open = f.Assignments.FirstOrDefault(a => !a.Revoked &&
            a.StudentIds.Any(students.Contains) &&
            keyList.Any(k => FieldScope.IsAllowed(k, a.AllowedFields)));
        if (open is not null)
            throw new MasterDataException($"Der Bereich ist im offenen Arbeitsauftrag „{open.Label}“ ({open.TeacherName}, {open.ClassName}) enthalten.");
    }

    private static Catalog Clone(Catalog c) => new()
    {
        Id = c.Id, Title = c.Title, Grade = c.Grade, Track = c.Track, RatingScale = [.. c.RatingScale],
        Sections = c.Sections.Select(s => new CatalogSection
        {
            Key = s.Key, Title = s.Title, Subject = s.Subject, Kind = s.Kind, NextItemNumber = s.NextItemNumber,
            Items = s.Items.Select(i => new CatalogItem { Id = i.Id, Text = i.Text }).ToList(),
        }).ToList(),
    };

    internal static Catalog CloneInternal(Catalog c) => Clone(c);

    private static string Text(string? value, string message, int max)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length == 0) throw new MasterDataException(message);
        if (clean.Length > max) throw new MasterDataException($"Der Text ist zu lang (max. {max} Zeichen).");
        return clean;
    }

    private static void Log(SchoolFile f, DateTimeOffset now, string action, string detail) =>
        f.Audit.Add(new AuditEntry(now, action, detail));
}
