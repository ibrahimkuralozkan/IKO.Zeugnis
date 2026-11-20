using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class GradingTests
{
    [Fact]
    public void Assignment_ShowsOnlyAllowedSections_AndCountsProgress()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var ctx = GradingContext.ForAssignment(assignment);
        var anna = ctx.Students.Single(s => s.FirstName == "Anna");

        var sections = ctx.SectionsFor(anna);
        Assert.Equal([FieldKind.Rating, FieldKind.Grade], sections.Select(s => s.Section.Kind));
        Assert.DoesNotContain(sections, s => s.Section.Key == "Allgemein");
        Assert.Equal(new Progress(0, 2), ctx.ProgressOf(anna));

        Assert.True(ctx.Set(anna, "g:Sport", "2", TestData.Now));
        Assert.True(ctx.Set(anna, "r:Sport.01", "1", TestData.Now));
        Assert.True(ctx.ProgressOf(anna).IsComplete);
        Assert.Equal(2, assignment.TeacherAudit.Count);
        var gradeAudit = assignment.TeacherAudit[0];
        Assert.Equal(anna.Id, gradeAudit.StudentId);
        Assert.Equal("3a", gradeAudit.ClassName);
        Assert.Equal("g:Sport", gradeAudit.FieldKey);
        Assert.Equal("(leer)", gradeAudit.OldDisplay);
        Assert.Equal("2 – gut", gradeAudit.NewDisplay);
        Assert.DoesNotContain(anna, ctx.IncompleteStudents());
    }

    [Fact]
    public void Set_RejectsFieldsOutsideScope_AndForeignStudents()
    {
        var school = TestData.School();
        var ctx = GradingContext.ForAssignment(TestData.SportAssignment(school));
        var anna = ctx.Students[0];

        Assert.False(ctx.Set(anna, "t:Allgemein", "nicht erlaubt", TestData.Now));
        Assert.False(ctx.Set(new Student { ClassName = "3a" }, "g:Sport", "1", TestData.Now));
        Assert.False(ctx.Set(anna, "g:Sport", "", TestData.Now)); // keine Änderung
    }

    [Fact]
    public void StudentWithoutCatalog_HasNoSections()
    {
        var school = TestData.School(); // nur Regel-Katalog vorhanden
        var ctx = GradingContext.ForAssignment(TestData.SportAssignment(school));
        var ben = ctx.Students.Single(s => s.Track == StudentTrack.SE);
        Assert.Empty(ctx.SectionsFor(ben));
        Assert.Equal(new Progress(0, 0), ctx.ProgressOf(ben));
    }

    [Fact]
    public void TeacherEdits_FlowIntoResponse()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var ctx = GradingContext.ForAssignment(assignment);
        var anna = ctx.Students.Single(s => s.FirstName == "Anna");
        ctx.Set(anna, "g:Sport", "1", TestData.Now);

        var response = ResponseBuilder.Create(assignment, TestData.Now);
        var preview = MergeEngine.Preview(school, response, TestData.Now);
        Assert.Equal("g:Sport", Assert.Single(preview.Applies).FieldKey);
    }

    [Fact]
    public void SchoolClass_AllowsAllFields_AndWritesIntoSchoolFile()
    {
        var school = TestData.School();
        var ctx = GradingContext.ForSchoolClass(school, "3a");
        var anna = ctx.Students.Single(s => s.FirstName == "Anna");

        Assert.Equal(3, ctx.SectionsFor(anna).Count);
        Assert.True(ctx.Set(anna, "t:Allgemein", "Anna arbeitet sorgfältig.", TestData.Now));
        var value = school.ReportFor(anna.Id).Fields["t:Allgemein"];
        Assert.Equal("Verwaltung", value.ChangedBy);
        Assert.Equal(1, value.Revision);
        var audit = Assert.Single(school.Audit);
        Assert.Equal("Zeugnisfeld geändert", audit.Action);
        Assert.Equal(anna.Id, audit.StudentId);
        Assert.Equal("3a", audit.ClassName);
        Assert.Equal("t:Allgemein", audit.FieldKey);
        Assert.Equal("(leer)", audit.OldDisplay);
        Assert.Equal("Anna arbeitet sorgfältig.", audit.NewDisplay);
    }
}
