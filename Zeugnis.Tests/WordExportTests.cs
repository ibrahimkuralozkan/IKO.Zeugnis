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
        Assert.Contains("w14:val=\"1\"", xml);
        Assert.DoesNotContain(Unchecked, xml);
        Assert.Contains(">2</w:t>", xml);                // Note
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
        Assert.Contains("w14:val=\"0\"", xml);
        Assert.Contains(Unchecked, xml);
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
