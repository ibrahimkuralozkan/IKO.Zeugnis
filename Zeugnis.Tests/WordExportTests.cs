using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using Zeugnis.Domain;
using Zeugnis.Exchange;
using Zeugnis.Export;

namespace Zeugnis.Tests;

public class WordExportTests
{
    private static readonly DateOnly Today = new(2026, 6, 30);
    private const string Unchecked = "☐"; // ☐

    /// <summary>Minimale .docx mit Textfeldern, einem Ankreuzfeld, einem Blockfeld und einem unbekannten Tag.</summary>
    private static byte[] Template()
    {
        const string w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var document = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document xmlns:w="{w}" xmlns:w14="{TemplateFiller.W14Namespace}" xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006" mc:Ignorable="w14">
              <w:body>
                <w:p><w:sdt><w:sdtPr><w:tag w:val="student.name"/></w:sdtPr><w:sdtContent><w:r><w:t>Name</w:t></w:r></w:sdtContent></w:sdt></w:p>
                <w:p><w:sdt><w:sdtPr><w:tag w:val="r:Deutsch.Lesen.03#2"/><w14:checkbox><w14:checked w14:val="0"/><w14:checkedState w14:val="2612" w14:font="MS Gothic"/><w14:uncheckedState w14:val="2610" w14:font="MS Gothic"/></w14:checkbox></w:sdtPr><w:sdtContent><w:r><w:t>{Unchecked}</w:t></w:r></w:sdtContent></w:sdt></w:p>
                <w:p><w:sdt><w:sdtPr><w:tag w:val="g:Sport"/></w:sdtPr><w:sdtContent><w:r><w:t>-</w:t></w:r></w:sdtContent></w:sdt></w:p>
                <w:sdt><w:sdtPr><w:tag w:val="t:Allgemein"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Text</w:t></w:r></w:p></w:sdtContent></w:sdt>
                <w:p><w:sdt><w:sdtPr><w:tag w:val="unbekannt.tag"/></w:sdtPr><w:sdtContent><w:r><w:t>bleibt</w:t></w:r></w:sdtContent></w:sdt></w:p>
              </w:body>
            </w:document>
            """;
        const string contentTypes = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """;
        const string rels = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """;
        return Zip(new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = contentTypes,
            ["_rels/.rels"] = rels,
            ["word/document.xml"] = document,
        });
    }

    private static byte[] Zip(Dictionary<string, string> files)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, text) in files)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(Encoding.UTF8.GetBytes(text));
            }
        return ms.ToArray();
    }

    private static string DocumentXml(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx), false);
        return doc.MainDocumentPart!.Document!.OuterXml;
    }

    private static SchoolFile School()
    {
        var f = new SchoolFile
        {
            Term = new Term("2025/26", 2),
            School = new SchoolInfo { Name = "Testschule", Town = "Testort" },
            Classes = [new SchoolClass { Name = "3a", Grade = 3 }],
        };
        f.Students.Add(new Student
        {
            FirstName = "Anna", LastName = "Muster", ClassName = "3a", Track = StudentTrack.Regular,
            DateOfBirth = new DateOnly(2017, 3, 5),
        });
        f.Students.Add(new Student { FirstName = "Ben", LastName = "Beispiel", ClassName = "3a", Track = StudentTrack.Regular });
        return f;
    }

    [Fact]
    public void Tags_listet_alle_Tags_ohne_Duplikate()
    {
        var tags = TemplateFiller.Tags(Template());
        Assert.Equal(["student.name", "r:Deutsch.Lesen.03#2", "g:Sport", "t:Allgemein", "unbekannt.tag"], tags);
    }

    [Fact]
    public void Validate_lehnt_keine_docx_ab()
    {
        Assert.Throws<InvalidDataException>(() => TemplateFiller.Validate(Encoding.UTF8.GetBytes("kein Word")));
        TemplateFiller.Validate(Template());
    }

    [Fact]
    public void Fill_setzt_Text_Ankreuzfeld_und_Blockfeld()
    {
        var f = School();
        var student = f.Students[0];
        var now = DateTimeOffset.UtcNow;
        f.ReportFor(student.Id).Set("r:Deutsch.Lesen.03", "2", "Verwaltung", now);
        f.ReportFor(student.Id).Set("g:Sport", "2", "Verwaltung", now);
        f.ReportFor(student.Id).Set("t:Allgemein", "Sehr engagiert.", "Verwaltung", now);

        var xml = DocumentXml(TemplateFiller.Fill(Template(), ReportExport.ValuesFor(f, student, Today)));

        Assert.Contains("Anna Muster", xml);
        Assert.Contains("☒", xml);                 // angekreuzt
        Assert.DoesNotContain(Unchecked, xml);
        Assert.Contains(">gut</w:t>", xml);              // Note als Wort
        Assert.DoesNotContain("w:val=\"student.name\"", xml); // gefüllte Felder sind normaler Text
        Assert.Contains("w:val=\"unbekannt.tag\"", xml);      // unbekannte bleiben als Feld
        Assert.Contains("Sehr engagiert.", xml);
        Assert.Contains(">bleibt</w:t>", xml);           // unbekanntes Tag unverändert
    }

    [Fact]
    public void Ankreuzfeld_bleibt_leer_bei_anderem_Wert()
    {
        var f = School();
        var student = f.Students[0];
        f.ReportFor(student.Id).Set("r:Deutsch.Lesen.03", "3", "Verwaltung", DateTimeOffset.UtcNow);

        var xml = DocumentXml(TemplateFiller.Fill(Template(), ReportExport.ValuesFor(f, student, Today)));
        Assert.Contains(Unchecked, xml);
    }

    /// <summary>Vorlage mit Textmarkierung „●“, Auswahltext, Formatierung, Zeilenumbruch und Kopfzeile.</summary>
    private static byte[] RichTemplate()
    {
        const string ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        static string Sdt(string tag, string inner) =>
            $"<w:sdt><w:sdtPr><w:tag w:val=\"{tag}\"/></w:sdtPr><w:sdtContent>{inner}</w:sdtContent></w:sdt>";
        const string boldRun = "<w:r><w:rPr><w:b/></w:rPr><w:t>[Text]</w:t></w:r>";
        var body = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:document {ns}><w:body>
              <w:p>{Sdt("r:Sport.01#1", "<w:r><w:t>x</w:t></w:r>")}{Sdt("r:Sport.01#2", "<w:r><w:t>x</w:t></w:r>")}</w:p>
              <w:p>{Sdt("s:Versetzung", "<w:r><w:t>Versetzung</w:t></w:r>")}</w:p>
              <w:p>{Sdt("t:Allgemein", boldRun)}</w:p>
              <w:sectPr><w:headerReference w:type="default" r:id="rIdH"/></w:sectPr>
            </w:body></w:document>
            """;
        var header = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <w:hdr {ns}><w:p>{Sdt("student.header", "<w:r><w:t>Kopf</w:t></w:r>")}</w:p></w:hdr>
            """;
        return Zip(new Dictionary<string, string>
        {
            ["[Content_Types].xml"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
                  <Override PartName="/word/header1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"/>
                </Types>
                """,
            ["_rels/.rels"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
                </Relationships>
                """,
            ["word/_rels/document.xml.rels"] = """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rIdH" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/header" Target="header1.xml"/>
                </Relationships>
                """,
            ["word/document.xml"] = body,
            ["word/header1.xml"] = header,
        });
    }

    [Fact]
    public void Markierung_Auswahltext_Formatierung_und_Kopfzeile()
    {
        var f = School();
        f.Catalogs.Add(new Catalog
        {
            Grade = 3, Track = StudentTrack.Regular,
            Sections = [new CatalogSection { Key = "Versetzung", Kind = FieldKind.Selection,
                Items = [new CatalogItem { Id = "versetzt", Text = "{Name} wird in Klasse 4 versetzt ({Schuljahresende})." }] }],
        });
        var s = f.Students[0];
        var now = DateTimeOffset.UtcNow;
        f.ReportFor(s.Id).Set("r:Sport.01", "2", "Verwaltung", now);
        f.ReportFor(s.Id).Set("s:Versetzung", "versetzt", "Verwaltung", now);
        f.ReportFor(s.Id).Set("t:Allgemein", "Zeile eins\nZeile zwei", "Verwaltung", now);

        var result = TemplateFiller.Fill(RichTemplate(), ReportExport.ValuesFor(f, s, Today));
        using var doc = WordprocessingDocument.Open(new MemoryStream(result), false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var paragraphs = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();

        Assert.Equal(TemplateFiller.Mark, paragraphs[0].InnerText);   // nur Spalte 2 markiert
        Assert.Equal("Anna Muster wird in Klasse 4 versetzt (2026).", paragraphs[1].InnerText);
        var run = paragraphs[2].Descendants<DocumentFormat.OpenXml.Wordprocessing.Run>().Single();
        Assert.NotNull(run.RunProperties?.Bold);                      // Formatierung bleibt
        Assert.Single(run.Elements<DocumentFormat.OpenXml.Wordprocessing.Break>());
        Assert.Equal("Zeile einsZeile zwei", paragraphs[2].InnerText);
        Assert.Equal("Anna Muster, Klasse 3a, Schuljahr 2025/26, 2. Halbjahr",
            doc.MainDocumentPart.HeaderParts.Single().Header!.InnerText);
    }

    [Fact]
    public void Stammdaten_werden_eingesetzt()
    {
        var f = School();
        var values = ReportExport.ValuesFor(f, f.Students[0], Today);
        Assert.Equal("05.03.2017", values("student.birthdate"));
        Assert.Equal("3a", values("class.name"));
        Assert.Equal("Testschule", values("school.name"));
        Assert.Equal("30.06.2026", values("date"));
        Assert.Equal("", values("g:Sport"));       // bekannter Schlüssel ohne Wert
        Assert.Null(values("unbekannt.tag"));       // unbekanntes Tag
    }

    [Fact]
    public void Klassenangaben_Fehlzeiten_und_Schulform()
    {
        var f = School();
        MasterData.SetClassReportDates(f, "3a", new DateOnly(2027, 6, 20), new DateOnly(2027, 6, 25), new DateOnly(2027, 8, 11), "08:00");
        var s = f.Students[0];
        var empty = ReportExport.ValuesFor(f, s, Today);
        Assert.Equal("", empty("student.fehlstunden"));
        Assert.Equal("", empty("student.verspaetungen"));
        Assert.Equal("", empty("student.schulform.auswahl")); // Katalog ohne Schulform-Auswahl

        f.Catalogs.Add(new Catalog
        {
            Grade = 3, Track = StudentTrack.Regular,
            Sections = [new CatalogSection { Key = "Schulform", Kind = FieldKind.Selection,
                Items = [new CatalogItem { Id = "realschule", Text = "Realschule" }, new CatalogItem { Id = "gymnasium", Text = "Gymnasium" }] }],
        });
        var now = DateTimeOffset.UtcNow;
        f.ReportFor(s.Id).Set("n:Fehlstunden", "12", "Verwaltung", now);
        f.ReportFor(s.Id).Set("n:Fehlstunden-unentschuldigt", "2", "Verwaltung", now);
        f.ReportFor(s.Id).Set("n:Verspaetungen", "3", "Verwaltung", now);
        f.ReportFor(s.Id).Set("s:Schulform", "gymnasium", "Verwaltung", now);
        var v = ReportExport.ValuesFor(f, s, Today);

        Assert.Equal("Das Zeugnis wird aufgrund des Konferenzbeschlusses vom 20.06.2027 erteilt.", v("class.konferenz"));
        Assert.Equal("Testort, den 25.06.2027", v("class.ortdatum"));
        Assert.Equal("Wiederbeginn des Unterrichts am Mittwoch, den 11.08.2027 um 08:00 Uhr.", v("class.wiederbeginn"));
        Assert.Equal("Fehlstunden insgesamt: 12, davon unentschuldigt: 2", v("student.fehlstunden"));
        Assert.Equal("Anzahl der Verspätungen: 3", v("student.verspaetungen"));
        Assert.StartsWith("☐ der Hauptschule   ☐ der Realschule   ☒ des Gymnasiums", v("student.schulform.auswahl"));
        Assert.Contains("am 20.06.2027 beschlossen, dass Anna Muster", v("student.schulform.einleitung"));
        Assert.Equal("n:Fehlstunden", FieldKey.Number("Fehlstunden").ToString());
    }

    [Fact]
    public void Explizit_eingetragene_Null_Fehlzeiten_werden_ausgegeben()
    {
        var f = School();
        var s = f.Students[0];
        var now = DateTimeOffset.UtcNow;
        f.ReportFor(s.Id).Set("n:Fehlstunden", "0", "Verwaltung", now);
        f.ReportFor(s.Id).Set("n:Fehlstunden-unentschuldigt", "0", "Verwaltung", now);

        var values = ReportExport.ValuesFor(f, s, Today);

        Assert.Equal("Fehlstunden insgesamt: 0, davon unentschuldigt: 0", values("student.fehlstunden"));
    }

    [Fact]
    public void Faecherauswahl_aus_Katalogen_und_Word_Vorlage()
    {
        var f = School();
        var cat = new Catalog
        {
            Id = "K3", Grade = 3, Track = StudentTrack.Regular,
            Sections = [new CatalogSection { Key = "Mathe", Subject = "Mathematik", Kind = FieldKind.Grade },
                        new CatalogSection { Key = "Sport", Subject = "Sport", Kind = FieldKind.Grade }],
        };
        f.Catalogs.Add(cat);
        Assert.Equal(["Mathematik", "Sport"], SubjectOptions.For(f, cat).Select(o => o.Name)); // ohne Vorlage

        WordTemplates.Set(f, 3, StudentTrack.Regular, "v.docx", Template(), DateTimeOffset.UtcNow);
        var options = SubjectOptions.For(f, cat);
        // g:Sport ist schon im Katalog; r:Deutsch.Lesen.03 und t:Allgemein fehlen noch.
        Assert.Equal(["Mathematik", "Sport", "Allgemeine Bemerkungen", "Deutsch"], options.Select(o => o.Name));
        Assert.Equal([false, false, true, true], options.Select(o => o.FromTemplate));
    }

    [Fact]
    public void Ungueltige_Uhrzeit_wird_abgelehnt()
    {
        var f = School();
        Assert.Throws<MasterDataException>(() => MasterData.SetClassReportDates(f, "3a", null, null, null, "8 Uhr"));
        MasterData.SetClassReportDates(f, "3a", null, null, null, "08:00:00");
        Assert.Equal("08:00", f.Classes[0].ResumptionTime);
        MasterData.SetClassReportDates(f, "3a", null, null, null, "7:45");
        Assert.Equal("07:45", f.Classes[0].ResumptionTime);
    }

    [Fact]
    public void Klassen_ZIP_enthaelt_eine_Datei_je_Schueler()
    {
        var f = School();
        WordTemplates.Set(f, 3, StudentTrack.Regular, "vorlage.docx", Template(), DateTimeOffset.UtcNow);

        var zipBytes = ReportExport.ClassZip(f, "3a", Today);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Equal(["Beispiel_Ben.docx", "Muster_Anna.docx"], names);
    }

    [Fact]
    public void Ohne_passende_Vorlage_gibt_es_eine_verstaendliche_Meldung()
    {
        var f = School();
        var ex = Assert.Throws<InvalidOperationException>(() => ReportExport.Student(f, f.Students[0], Today));
        Assert.Contains("keine Word-Vorlage", ex.Message);
    }

    [Fact]
    public void Vorlage_ersetzt_die_vorherige_fuer_gleichen_Bildungsgang()
    {
        var f = School();
        WordTemplates.Set(f, 3, StudentTrack.Regular, "alt.docx", Template(), DateTimeOffset.UtcNow);
        WordTemplates.Set(f, 3, StudentTrack.Regular, "neu.docx", Template(), DateTimeOffset.UtcNow);
        Assert.Single(f.Templates);
        Assert.Equal("neu.docx", WordTemplates.For(f, 3, StudentTrack.Regular)!.FileName);
    }
}
