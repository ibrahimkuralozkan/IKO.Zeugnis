using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zeugnis.Export;

/// <summary>
/// Füllt die Inhaltssteuerelemente (<c>w:sdt</c>) einer Word-Vorlage anhand ihres Tags –
/// im Hauptteil sowie in Kopf- und Fußzeilen.
/// <list type="bullet">
/// <item>Text: Tag = Schlüssel, z. B. <c>student.name</c>, <c>class.name</c>, <c>g:Sport</c>, <c>t:Allgemein</c>.</item>
/// <item>Ankreuzfeld oder Markierung: Tag = Schlüssel + „#“ + erwarteter Wert, z. B. <c>r:Deutsch.Lesen.03#2</c>.
/// Ein Word-Kontrollkästchen wird angekreuzt; ein Textfeld erhält „●“ (sonst bleibt es leer).</item>
/// </list>
/// Die Formatierung der Vorlage bleibt erhalten. Unbekannte Tags bleiben unverändert, damit Lücken auffallen.
/// Gefüllte Steuerelemente werden in normalen Text umgewandelt, damit im fertigen Zeugnis keine Feldnamen erscheinen.
/// </summary>
public static class TemplateFiller
{
    public const string W14Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";
    public const string Mark = "●";
    private const string Checked = "☒";   // ☒
    private const string Unchecked = "☐"; // ☐

    /// <summary>Prüft, ob die Bytes eine lesbare .docx-Datei mit Hauptdokument sind.</summary>
    public static void Validate(byte[] template)
    {
        bool ok;
        try
        {
            using var doc = WordprocessingDocument.Open(new MemoryStream(template), false);
            ok = doc.MainDocumentPart is not null;
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException)
        {
            ok = false;
        }
        if (!ok) throw new InvalidDataException("Die Datei ist keine gültige Word-Vorlage (.docx).");
    }

    /// <summary>Alle Tags der Vorlage (Hauptteil, Kopf- und Fußzeilen), in Dokumentreihenfolge, ohne Duplikate.</summary>
    public static IReadOnlyList<string> Tags(byte[] template)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(template), false);
        return Roots(doc)
            .SelectMany(r => r.Descendants<SdtElement>())
            .Select(TagOf)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Füllt die Vorlage; liefert die neuen .docx-Bytes.</summary>
    public static byte[] Fill(byte[] template, Func<string, string?> valueOf)
    {
        using var stream = new MemoryStream();
        stream.Write(template);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, true))
        {
            if (doc.MainDocumentPart?.Document?.Body is null)
                throw new InvalidDataException("Die Word-Vorlage hat kein Hauptdokument.");
            foreach (var root in Roots(doc))
                FillRoot(root, valueOf);
        }
        return stream.ToArray();
    }

    private static IEnumerable<OpenXmlElement> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart;
        if (main?.Document?.Body is { } body) yield return body;
        if (main is null) yield break;
        foreach (var h in main.HeaderParts) if (h.Header is { } header) yield return header;
        foreach (var f in main.FooterParts) if (f.Footer is { } footer) yield return footer;
    }

    private static void FillRoot(OpenXmlElement root, Func<string, string?> valueOf)
    {
        var filled = new List<SdtElement>();
        foreach (var sdt in root.Descendants<SdtElement>().ToList())
        {
            var tag = TagOf(sdt);
            if (tag is null) continue;

            var hash = tag.IndexOf('#');
            var key = hash >= 0 ? tag[..hash] : tag;
            var expected = hash >= 0 ? tag[(hash + 1)..] : null;

            var value = valueOf(key);
            if (value is null) continue; // unbekanntes Tag: unverändert lassen

            var match = expected is not null && value == expected;
            if (IsCheckBox(sdt)) SetCheckBox(sdt, match);
            else SetText(sdt, expected is null ? value : (match ? Mark : ""));
            filled.Add(sdt);
        }

        // Innerste zuerst, damit verschachtelte Steuerelemente ihren Inhalt behalten.
        foreach (var sdt in filled.AsEnumerable().Reverse())
            Unwrap(sdt);
    }

    private static string? TagOf(SdtElement sdt) =>
        sdt.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value is { Length: > 0 } tag ? tag : null;

    private static bool IsCheckBox(SdtElement sdt) =>
        sdt.SdtProperties?.Descendants().Any(e => e.LocalName == "checkbox" && e.NamespaceUri == W14Namespace) == true;

    private static void SetCheckBox(SdtElement sdt, bool isChecked)
    {
        var checkedElement = sdt.SdtProperties!.Descendants()
            .First(e => e.LocalName == "checked" && e.NamespaceUri == W14Namespace);
        checkedElement.SetAttribute(new OpenXmlAttribute("w14", "val", W14Namespace, isChecked ? "1" : "0"));

        var glyph = sdt.Descendants<Text>().FirstOrDefault();
        if (glyph is not null) glyph.Text = isChecked ? Checked : Unchecked;
    }

    /// <summary>
    /// Schreibt den Wert in den ersten Textlauf (Formatierung bleibt) und leert die übrigen.
    /// Zeilenumbrüche im Wert werden zu Word-Zeilenumbrüchen.
    /// </summary>
    private static void SetText(SdtElement sdt, string value)
    {
        var content = sdt.ChildElements.FirstOrDefault(e => e is SdtContentRun or SdtContentBlock or SdtContentCell);
        if (content is null) return;

        sdt.SdtProperties?.GetFirstChild<ShowingPlaceholder>()?.Remove();
        var texts = content.Descendants<Text>().ToList();
        Run run;
        if (texts.Count == 0)
        {
            run = new Run();
            if (content is SdtContentRun r) r.Append(run);
            else (content.Descendants<Paragraph>().FirstOrDefault() ?? content.AppendChild(new Paragraph())).Append(run);
        }
        else
        {
            run = texts[0].Ancestors<Run>().First();
            foreach (var t in texts.Skip(1)) t.Remove();
            foreach (var r in content.Descendants<Run>().Where(r => r != run && !r.Descendants<Text>().Any() && !r.Elements<FieldChar>().Any()).ToList())
                if (r.Elements().All(e => e is RunProperties)) r.Remove();
            foreach (var t in run.Elements().Where(e => e is not RunProperties).ToList()) t.Remove();
        }

        // Platzhalter-Formatierung (meist grau) entfernen, eigene Formatierung behalten.
        run.RunProperties?.RunStyle?.Remove();

        var lines = value.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
    }

    /// <summary>Ersetzt das Steuerelement durch seinen Inhalt.</summary>
    private static void Unwrap(SdtElement sdt)
    {
        if (sdt.Parent is null) return;
        var content = sdt.ChildElements.FirstOrDefault(e => e is SdtContentRun or SdtContentBlock or SdtContentCell or SdtContentRow);
        if (content is null) return;
        foreach (var child in content.ChildElements.ToList())
            sdt.InsertBeforeSelf(child.CloneNode(true));
        sdt.Remove();
    }
}
