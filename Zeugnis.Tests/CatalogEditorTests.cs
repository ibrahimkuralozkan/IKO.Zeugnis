using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class CatalogEditorTests
{
    private static readonly DateTimeOffset Now = TestData.Now;

    private static (SchoolFile File, Catalog Catalog) Setup()
    {
        var f = new SchoolFile { Term = new Term("2026/27", 1) };
        MasterData.AddClass(f, "3a", 3, null, Now);
        MasterData.AddClass(f, "4a", 4, null, Now);
        return (f, CatalogEditor.Create(f, 3, StudentTrack.Regular, Now));
    }

    [Theory]
    [InlineData("Deutsch – Lesen", "Deutsch-Lesen")]
    [InlineData("Sachunterricht", "Sachunterricht")]
    [InlineData("Arbeits- und Sozialverhalten", "Arbeits-und-Sozialverhalten")]
    [InlineData("Größe & Maße", "Groesse-Masse")]
    [InlineData("Français", "Francais")]
    [InlineData("1. Fremdsprache", "1-Fremdsprache")]
    [InlineData("–", "Bereich")]
    public void Slug_IsStableAndPatternSafe(string title, string expected) =>
        Assert.Equal(expected, CatalogEditor.Slug(title));

    [Fact]
    public void OneCatalogPerGradeAndTrack()
    {
        var (f, _) = Setup();
        Assert.Throws<MasterDataException>(() => CatalogEditor.Create(f, 3, StudentTrack.Regular, Now));
        CatalogEditor.Create(f, 3, StudentTrack.SE, Now);
        Assert.Throws<MasterDataException>(() => CatalogEditor.Create(f, 5, StudentTrack.Regular, Now));
        Assert.Equal(["K3-Regular", "K3-SE"], f.Catalogs.Select(c => c.Id));
    }

    [Fact]
    public void SectionKeys_AreUniquePerKind_AndStableOnRename()
    {
        var (f, c) = Setup();
        var rating = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Rating, Now);
        var grade = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Grade, Now);
        var second = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Rating, Now);
        Assert.Equal("Sport", rating.Key);
        Assert.Equal("Sport", grade.Key);   // andere Feldart, kein Konflikt
        Assert.Equal("Sport-2", second.Key);

        CatalogEditor.UpdateSection(f, c.Id, FieldKind.Rating, "Sport", "Sport und Bewegung", "Sport");
        Assert.Equal("Sport", rating.Key);
        Assert.Equal("Sport und Bewegung", rating.Title);
    }

    [Fact]
    public void ItemIds_AreNeverReused()
    {
        var (f, c) = Setup();
        var s = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Rating, Now);
        var a = CatalogEditor.AddItem(f, c.Id, FieldKind.Rating, s.Key, "läuft ausdauernd");
        var b = CatalogEditor.AddItem(f, c.Id, FieldKind.Rating, s.Key, "fängt sicher");
        Assert.Equal(["01", "02"], s.Items.Select(i => i.Id));

        CatalogEditor.RemoveItem(f, c.Id, FieldKind.Rating, s.Key, b.Id, Now);
        var next = CatalogEditor.AddItem(f, c.Id, FieldKind.Rating, s.Key, "springt weit");
        Assert.Equal("03", next.Id);
        Assert.Equal(["r:Sport.01", "r:Sport.03"], s.FieldKeys());
        _ = a;
    }

    [Fact]
    public void MoveSectionAndItem_ChangesOrderOnly()
    {
        var (f, c) = Setup();
        CatalogEditor.AddSection(f, c.Id, "Deutsch", "Deutsch", FieldKind.Rating, Now);
        CatalogEditor.AddSection(f, c.Id, "Mathematik", "Mathematik", FieldKind.Rating, Now);
        CatalogEditor.MoveSection(f, c.Id, FieldKind.Rating, "Mathematik", -1);
        Assert.Equal(["Mathematik", "Deutsch"], c.Sections.Select(s => s.Key));
        CatalogEditor.MoveSection(f, c.Id, FieldKind.Rating, "Mathematik", -1); // bleibt oben
        Assert.Equal("Mathematik", c.Sections[0].Key);
    }

    [Fact]
    public void RemovingSection_DeletesOnlyValuesOfThisGradeAndTrack()
    {
        var (f, c) = Setup();
        var s = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Grade, Now);
        var third = MasterData.AddStudent(f, new Student { FirstName = "A", LastName = "Drei", ClassName = "3a" }, Now);
        var fourth = MasterData.AddStudent(f, new Student { FirstName = "B", LastName = "Vier", ClassName = "4a" }, Now);
        f.ReportFor(third.Id).Set("g:Sport", "2", "V", Now);
        f.ReportFor(fourth.Id).Set("g:Sport", "1", "V", Now);

        Assert.Equal(1, CatalogEditor.CountSectionValues(f, c.Id, FieldKind.Grade, s.Key));
        CatalogEditor.RemoveSection(f, c.Id, FieldKind.Grade, s.Key, Now);

        Assert.Null(f.ReportFor(third.Id).Get("g:Sport"));
        Assert.Equal("1", f.ReportFor(fourth.Id).Get("g:Sport")); // Klasse 4 unberührt
    }

    [Fact]
    public void SectionInOpenAssignment_CannotBeRemoved()
    {
        var f = TestData.School();
        f.Catalogs[0].Id = Catalog.IdFor(3, StudentTrack.Regular);
        TestData.SportAssignment(f);
        var ex = Assert.Throws<MasterDataException>(() =>
            CatalogEditor.RemoveSection(f, f.Catalogs[0].Id, FieldKind.Grade, "Sport", Now));
        Assert.Contains("Herr Muster", ex.Message);
    }

    [Fact]
    public void Selection_KeepsAtLeastTwoOptions_AndCountsUse()
    {
        var (f, c) = Setup();
        var s = CatalogEditor.AddSection(f, c.Id, "Versetzung", "", FieldKind.Selection, Now);
        CatalogEditor.UpdateItem(f, c.Id, FieldKind.Selection, s.Key, "01", "wird versetzt");
        var no = CatalogEditor.AddItem(f, c.Id, FieldKind.Selection, s.Key, "wird nicht versetzt");
        Assert.Throws<MasterDataException>(() => CatalogEditor.RemoveItem(f, c.Id, FieldKind.Selection, s.Key, no.Id, Now));

        var st = MasterData.AddStudent(f, new Student { FirstName = "A", LastName = "Drei", ClassName = "3a" }, Now);
        f.ReportFor(st.Id).Set("s:Versetzung", no.Id, "V", Now);
        Assert.Equal(1, CatalogEditor.CountItemValues(f, c.Id, FieldKind.Selection, s.Key, no.Id));
        Assert.Equal(0, CatalogEditor.CountItemValues(f, c.Id, FieldKind.Selection, s.Key, "01"));
    }

    [Fact]
    public void Scale_CannotDropStepsInUse()
    {
        var (f, c) = Setup();
        var s = CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Rating, Now);
        var item = CatalogEditor.AddItem(f, c.Id, FieldKind.Rating, s.Key, "läuft");
        var st = MasterData.AddStudent(f, new Student { FirstName = "A", LastName = "Drei", ClassName = "3a" }, Now);
        f.ReportFor(st.Id).Set(s.ItemKey(item.Id), "4", "V", Now);

        Assert.Throws<MasterDataException>(() => CatalogEditor.SetScale(f, c.Id, ["gut", "mittel", "schwach"]));
        CatalogEditor.SetScale(f, c.Id, ["sehr sicher", "sicher", "teilweise", "noch nicht", "nicht"]);
        Assert.Equal(5, c.RatingScale.Count);
        Assert.Throws<MasterDataException>(() => CatalogEditor.SetScale(f, c.Id, ["nur eine"]));
    }

    [Fact]
    public void Copy_KeepsKeys_ForOtherGrade()
    {
        var (f, c) = Setup();
        CatalogEditor.AddSection(f, c.Id, "Sport", "Sport", FieldKind.Grade, Now);
        var copy = CatalogEditor.Copy(f, c.Id, 4, StudentTrack.Regular, Now);
        Assert.Equal("Klasse 4", copy.Title);
        Assert.Equal(c.FieldKeys(), copy.FieldKeys());
        copy.Sections[0].Title = "geändert";
        Assert.Equal("Sport", c.Sections[0].Title); // echte Kopie
    }

    [Fact]
    public void Transfer_RoundTrip_SkipsExisting()
    {
        var (f, c) = Setup();
        var s = CatalogEditor.AddSection(f, c.Id, "Deutsch – Lesen", "Deutsch", FieldKind.Rating, Now);
        CatalogEditor.AddItem(f, c.Id, FieldKind.Rating, s.Key, "liest flüssig");
        CatalogEditor.Create(f, 1, StudentTrack.Regular, Now);
        var bytes = CatalogTransfer.Export(f.Catalogs, Now);

        var target = new SchoolFile { Term = new Term("2027/28", 1) };
        CatalogEditor.Create(target, 1, StudentTrack.Regular, Now);
        var result = CatalogTransfer.Import(target, bytes, Now);

        Assert.Equal(["Klasse 3"], result.Imported);
        Assert.Equal(["Klasse 1"], result.Skipped);
        Assert.Equal("liest flüssig", CatalogEditor.For(target, 3, StudentTrack.Regular)!.Sections[0].Items[0].Text);
        Assert.Throws<MasterDataException>(() => CatalogTransfer.Import(target, "{}"u8.ToArray(), Now));
    }
}
