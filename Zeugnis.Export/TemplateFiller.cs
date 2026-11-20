using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Zeugnis.Export;

/// <summary>
/// Füllt die Inhaltssteuerelemente (<c>w:sdt</c>) einer Word-Vorlage anhand ihres Tags.
/// <list type="bullet">
/// <item>Text: Tag = Schlüssel, z. B. <c>student.name</c>, <c>class.name</c>, <c>g:Sport</c>, <c>t:Allgemein</c>.</item>
/// <item>Ankreuzfeld: Tag = Schlüssel + „#“ + erwarteter Wert, z. B. <c>r:Deutsch.Lesen.03#2</c>
/// (angekreuzt, wenn der Wert „2“ ist).</item>
/// </list>
/// Unbekannte Tags bleiben unverändert, damit Fehler in der Vorlage sichtbar bleiben.
/// </summary>
public static class TemplateFiller
{
    public const string W14Namespace = "http://schemas.microsoft.com/office/word/2010/wordml";
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

    /// <summary>Alle Tags des Hauptdokuments, in Dokumentreihenfolge, ohne Duplikate.</summary>
    public static IReadOnlyList<string> Tags(byte[] template)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(template), false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null) return [];
        return body.Descendants<SdtElement>()
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
            var body = doc.MainDocumentPart?.Document?.Body
                ?? throw new InvalidDataException("Die Word-Vorlage hat kein Hauptdokument.");
            foreach (var sdt in body.Descendants<SdtElement>().ToList())
            {
                var tag = TagOf(sdt);
                if (tag is null) continue;

                var hash = tag.IndexOf('#');
                var key = hash >= 0 ? tag[..hash] : tag;
                var expected = hash >= 0 ? tag[(hash + 1)..] : null;

                var value = valueOf(key);
                if (value is null) continue; // unbekanntes Tag: unverändert lassen

                if (IsCheckBox(sdt))
                    SetCheckBox(sdt, expected is not null && value == expected);
                else
                    SetText(sdt, value);
            }
        }
        return stream.ToArray();
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

    private static void SetText(SdtElement sdt, string value)
    {
        if (sdt.GetFirstChild<SdtContentRun>() is { } run)
        {
            run.RemoveAllChildren();
            run.Append(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve }));
        }
        else if (sdt.GetFirstChild<SdtContentBlock>() is { } block)
        {
            block.RemoveAllChildren();
            block.Append(new Paragraph(new Run(new Text(value) { Space = SpaceProcessingModeValues.Preserve })));
        }
    }
}
