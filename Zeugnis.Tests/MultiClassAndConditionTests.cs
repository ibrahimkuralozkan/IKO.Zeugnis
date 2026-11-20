using Zeugnis.Domain;
using Zeugnis.Exchange;
using Zeugnis.Export;

namespace Zeugnis.Tests;

public class MultiClassAndConditionTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    /// <summary>Klasse 3a (Jg. 3) und 4a (Jg. 4), je ein Sport-Katalog mit unterschiedlichem Kriterium.</summary>
    private static SchoolFile TwoGrades()
    {
        var f = TestData.School();
        f.Classes.Add(new SchoolClass { Name = "4a", Grade = 4 });
        f.Students.Add(new Student { FirstName = "Cem", LastName = "Test", ClassName = "4a" });
        f.Catalogs.Add(new Catalog
        {
            Id = "K4", Grade = 4, Track = StudentTrack.Regular,
            Sections = [new CatalogSection { Key = "Sport", Subject = "Sport", Kind = FieldKind.Rating,
                Items = [new CatalogItem { Id = "02", Text = "schwimmt sicher" }] }],
        });
        return f;
    }

    [Fact]
    public void Ein_Auftrag_fuer_mehrere_Klassen_mit_passendem_Katalog_je_Jahrgang()
    {
        var f = TwoGrades();
        var file = AssignmentFactory.Create(f,
            new AssignmentRequest(TestData.Sport(f).Id, ["4a", "3a"], "Sport", ["r:Sport.*"], Now.AddDays(7)), Now);

        Assert.Equal(["3a", "4a"], file.ClassNames);              // Reihenfolge der Schuldatei
        Assert.Equal("3a, 4a", file.ClassName);
        Assert.Equal(3, file.Students.Count);
        Assert.Equal("Auftrag_3a+4a_Sport_Herr_Muster.an-lehrkraft", AssignmentFactory.FileName(file));

        var ctx = GradingContext.ForAssignment(file);
        var cem = ctx.Students.Single(s => s.FirstName == "Cem");
        var anna = ctx.Students.Single(s => s.FirstName == "Anna");
        Assert.Equal(["r:Sport.02"], ctx.SectionsFor(cem).SelectMany(x => x.Keys));
        Assert.Equal(["r:Sport.01"], ctx.SectionsFor(anna).SelectMany(x => x.Keys));
        Assert.Equal("3a", ctx.Students[0].ClassName);             // nach Klasse sortiert
    }

    [Fact]
    public void Lehrkraft_hat_ein_Kennwort_fuer_alle_Auftraege_des_Halbjahres()
    {
        var f = TwoGrades();
        var sport = TestData.Sport(f).Id;
        AssignmentFactory.Create(f, new AssignmentRequest(sport, ["3a"], "Sport", ["r:Sport.*"], Now.AddDays(7)), Now);
        AssignmentFactory.Create(f, new AssignmentRequest(sport, ["4a"], "Sport", ["r:Sport.*"], Now.AddDays(7)), Now);
        var first = f.Assignments[0].Password;
        Assert.Equal(first, f.Assignments[1].Password);
        Assert.Equal(first, f.TeacherPasswords[sport]);

        var other = f.Teachers.First(t => t.Id != sport).Id;
        AssignmentFactory.Create(f, new AssignmentRequest(other, ["3a"], "Allgemein", ["t:Allgemein"], Now.AddDays(7)), Now);
        Assert.NotEqual(first, f.Assignments[2].Password);        // jede Lehrkraft hat ihr eigenes

        var renewed = MasterData.RenewTeacherPassword(f, sport, Now);
        Assert.NotEqual(first, renewed);
        Assert.Equal(first, f.Assignments[0].Password);           // ausgegebene Aufträge behalten ihr Kennwort
        AssignmentFactory.Create(f, new AssignmentRequest(sport, ["3a"], "Sport 2", ["g:Sport"], Now.AddDays(7)), Now);
        Assert.Equal(renewed, f.Assignments[3].Password);

        f.Assignments.RemoveAll(a => a.TeacherId == other);
        f.Classes.ForEach(c => c.ClassTeacherId = null);
        MasterData.RemoveTeacher(f, other, Now);
        Assert.False(f.TeacherPasswords.ContainsKey(other));
    }

    [Fact]
    public void Klasse_umbenennen_aendert_alle_Klassen_des_Auftrags()
    {
        var f = TwoGrades();
        AssignmentFactory.Create(f, new AssignmentRequest(TestData.Sport(f).Id, ["3a", "4a"], "Sport", ["r:Sport.*"], Now.AddDays(7)), Now);
        MasterData.UpdateClass(f, "4a", "4b", 4, null, Now);
        Assert.Equal(["3a", "4b"], f.Assignments[0].ClassNames);
        Assert.Equal("3a, 4b", f.Assignments[0].ClassName);
        Assert.Throws<MasterDataException>(() => MasterData.UpdateClass(f, "4b", "4b", 3, null, Now)); // Jahrgang gesperrt
    }

    [Fact]
    public void Ruecklauf_zeigt_fehlende_ueberfaellige_und_vollstaendige_Auftraege()
    {
        var f = TestData.School();
        TestData.SportAssignment(f); // r:Sport.*, g:Sport – nur Anna hat einen Katalog (Ben ist SE): 2 Felder
        var a = f.Assignments[0];

        Assert.Equal(ReturnState.Missing, AssignmentStatus.Of(f, a, Now).State);
        Assert.Equal(ReturnState.Overdue, AssignmentStatus.Of(f, a, Now.AddDays(30)).State);

        foreach (var s in f.Students)
        {
            f.ReportFor(s.Id).Set("r:Sport.01", "1", "x", Now);
            f.ReportFor(s.Id).Set("g:Sport", "2", "x", Now);
        }
        a.Returns.Add(new ReturnRecord(Guid.NewGuid(), Now, Now, 4, 0, 0, 0));
        var status = AssignmentStatus.Of(f, a, Now);
        Assert.Equal(ReturnState.Complete, status.State);
        Assert.Equal(new Progress(2, 2), status.Fields);

        a.Revoked = true;
        Assert.Equal(ReturnState.Closed, AssignmentStatus.Of(f, a, Now).State);
    }

    /// <summary>Religion mit Teilnahme-Schalter und Deutschförderung mit Lückensatz.</summary>
    private static SchoolFile WithConditions()
    {
        var f = TestData.School();
        f.Students[0].Gender = Gender.Female;
        f.Catalogs[0].Sections.AddRange(
        [
            new CatalogSection { Key = "Teilnahme-Religion", Subject = "Religion", Kind = FieldKind.Selection,
                Items = [new CatalogItem { Id = "ja", Text = "nimmt teil", ReportText = "" },
                         new CatalogItem { Id = "nein", Text = "nimmt nicht teil", ReportText = "{Vorname} hat nicht am Religionsunterricht teilgenommen." }] },
            new CatalogSection { Key = "Religion", Subject = "Religion", Kind = FieldKind.Grade, VisibleWhen = "s:Teilnahme-Religion!=nein" },
            new CatalogSection { Key = "Religion", Subject = "Religion", Kind = FieldKind.Text, VisibleWhen = "s:Teilnahme-Religion!=nein" },
            new CatalogSection { Key = "Deutschfoerderung", Subject = "Deutschförderung", Kind = FieldKind.Selection,
                Items = [new CatalogItem { Id = "nein", Text = "nein", ReportText = "" },
                         new CatalogItem { Id = "ja", Text = "ja", ReportText = "{Vorname} besucht seit dem {d:Deutschfoerderung-Seit} … {ErSie} hat mit {n:Deutschfoerderung-Stunden} Stunden teilgenommen." }] },
            new CatalogSection { Key = "Deutschfoerderung-Seit", Subject = "Deutschförderung", Kind = FieldKind.Date, VisibleWhen = "s:Deutschfoerderung=ja" },
            new CatalogSection { Key = "Deutschfoerderung-Stunden", Subject = "Deutschförderung", Kind = FieldKind.Number, VisibleWhen = "s:Deutschfoerderung=ja" },
        ]);
        return f;
    }

    [Fact]
    public void Abhaengige_Bereiche_erscheinen_nur_bei_erfuellter_Bedingung()
    {
        var f = WithConditions();
        var ctx = GradingContext.ForSchoolClass(f, "3a");
        var anna = ctx.Students.Single(s => s.FirstName == "Anna");
        string[] Keys() => ctx.SectionsFor(anna).SelectMany(x => x.Keys).ToArray();

        Assert.Contains("g:Religion", Keys());                    // Standard: nimmt teil
        Assert.DoesNotContain("n:Deutschfoerderung-Stunden", Keys());

        ctx.Set(anna, "s:Teilnahme-Religion", "nein", Now);
        ctx.Set(anna, "s:Deutschfoerderung", "ja", Now);
        Assert.DoesNotContain("g:Religion", Keys());
        Assert.DoesNotContain("t:Religion", Keys());
        Assert.Contains("d:Deutschfoerderung-Seit", Keys());
        Assert.Contains("n:Deutschfoerderung-Stunden", Keys());
    }

    [Fact]
    public void Zeugnis_nutzt_Zeugnistext_Luecken_und_Ersatzwert()
    {
        var f = WithConditions();
        var anna = f.Students[0];
        var r = f.ReportFor(anna.Id);
        r.Set("g:Religion", "2", "x", Now);
        r.Set("t:Religion", "Eigene Bemerkung.", "x", Now);
        r.Set("s:Deutschfoerderung", "ja", "x", Now);
        r.Set("n:Deutschfoerderung-Stunden", "4", "x", Now);
        var today = new DateOnly(2027, 1, 30);

        var v = ReportExport.ValuesFor(f, anna, today);
        Assert.Equal("gut", v("g:Religion"));
        Assert.Equal("Eigene Bemerkung.", v("t:Religion||s:Teilnahme-Religion"));
        Assert.Equal("Anna besucht seit dem ____ … Sie hat mit 4 Stunden teilgenommen.", v("s:Deutschfoerderung"));

        r.Set("d:Deutschfoerderung-Seit", "2024-03-15", "x", Now);
        r.Set("s:Teilnahme-Religion", "nein", "x", Now);
        v = ReportExport.ValuesFor(f, anna, today);
        Assert.Equal("", v("g:Religion"));                         // ausgeblendet → leer
        Assert.Equal("Anna hat nicht am Religionsunterricht teilgenommen.", v("t:Religion||s:Teilnahme-Religion"));
        Assert.StartsWith("Anna besucht seit dem 15.03.2024", v("s:Deutschfoerderung"));
        Assert.Equal("15.03.2024", v("d:Deutschfoerderung-Seit"));

        r.Set("s:Deutschfoerderung", "nein", "x", Now);
        Assert.Equal("", ReportExport.ValuesFor(f, anna, today)("s:Deutschfoerderung"));
    }

    [Fact]
    public void Bedingungen_werden_beim_Katalogimport_geprueft_und_mitkopiert()
    {
        var f = WithConditions();
        var bytes = CatalogTransfer.Export(f.Catalogs, Now);
        var target = new SchoolFile();
        CatalogTransfer.Import(target, bytes, Now);
        var sec = target.Catalogs[0].Sections.Single(s => s.Key == "Deutschfoerderung-Stunden");
        Assert.Equal("s:Deutschfoerderung=ja", sec.VisibleWhen);
        Assert.Equal("", target.Catalogs[0].Sections.Single(s => s.Key == "Deutschfoerderung").Items[0].ReportText);

        f.Catalogs[0].Sections[^1].VisibleWhen = "kein Schlüssel";
        Assert.Throws<MasterDataException>(() => CatalogTransfer.Import(new SchoolFile(), CatalogTransfer.Export(f.Catalogs, Now), Now));
        Assert.Equal("d:Tag", FieldKey.Parse("d:Tag").ToString());
    }
}
