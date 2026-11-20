using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class MergeEngineTests
{
    private static readonly Dictionary<(Guid, string), ConflictChoice> NoChoices = [];

    private static ResponseFile Respond(AssignmentFile assignment, Action<AssignmentFile> edit, DateTimeOffset? at = null)
    {
        edit(assignment);
        return ResponseBuilder.Create(assignment, at ?? TestData.Now.AddHours(1));
    }

    private static void Fill(AssignmentFile a, int student, string key, string value) =>
        a.Reports[student].Set(key, value, a.TeacherName, TestData.Now);

    [Fact]
    public void Assignment_ContainsOnlyAllowedSections()
    {
        var school = TestData.School();
        school.ReportFor(school.Students[0].Id).Set("t:Allgemein", "geheim", "Verwaltung", TestData.Now);

        var assignment = TestData.SportAssignment(school);

        Assert.Equal(2, assignment.Students.Count);
        Assert.All(assignment.Catalogs.SelectMany(c => c.Sections), s => Assert.Equal("Sport", s.Key));
        Assert.All(assignment.Reports, r => Assert.Empty(r.Fields)); // Allgemein-Text wird nicht mitgegeben
        Assert.Single(school.Assignments);
    }

    [Fact]
    public void TeacherChanges_AreApplied()
    {
        var school = TestData.School();
        var response = Respond(TestData.SportAssignment(school), a =>
        {
            Fill(a, 0, "r:Sport.01", "2");
            Fill(a, 0, "g:Sport", "1");
        });

        var preview = MergeEngine.Preview(school, response, TestData.Now.AddDays(1));
        Assert.Equal(2, preview.Applies.Count());
        Assert.Empty(preview.Conflicts);

        var result = MergeEngine.Commit(school, preview, NoChoices, TestData.Now.AddDays(1));
        Assert.Equal(2, result.Applied);
        var report = school.Reports.Single(r => r.StudentId == school.Students[0].Id);
        Assert.Equal("1", report.Get("g:Sport"));
        Assert.Equal("Herr Muster", report.Fields["g:Sport"].ChangedBy);
    }

    [Fact]
    public void SameResponse_CannotBeImportedTwice()
    {
        var school = TestData.School();
        var response = Respond(TestData.SportAssignment(school), a => Fill(a, 0, "g:Sport", "2"));
        MergeEngine.Commit(school, MergeEngine.Preview(school, response, TestData.Now), NoChoices, TestData.Now);

        Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(school, response, TestData.Now));
    }

    [Fact]
    public void Commit_RejectsPreviewWhenSchoolValueChangedMeanwhile()
    {
        var school = TestData.School();
        var anna = school.Students[0].Id;
        var firstAssignment = TestData.SportAssignment(school);
        var secondAssignment = TestData.SportAssignment(school);
        var firstResponse = Respond(firstAssignment, a => Fill(a, 0, "g:Sport", "2"));
        var secondResponse = Respond(secondAssignment, a => Fill(a, 0, "g:Sport", "4"));
        var firstPreview = MergeEngine.Preview(school, firstResponse, TestData.Now);
        var secondPreview = MergeEngine.Preview(school, secondResponse, TestData.Now);

        MergeEngine.Commit(school, firstPreview, NoChoices, TestData.Now);

        var ex = Assert.Throws<ExchangeSecurityException>(() =>
            MergeEngine.Commit(school, secondPreview, NoChoices, TestData.Now));
        Assert.Contains("seit der Vorschau geändert", ex.Message);
        Assert.Equal("2", school.ReportFor(anna).Get("g:Sport"));
    }

    [Fact]
    public void Commit_RejectsPreviewWhenAssignmentWasClosedMeanwhile()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var response = Respond(assignment, a => Fill(a, 0, "g:Sport", "2"));
        var preview = MergeEngine.Preview(school, response, TestData.Now);
        school.Assignments.Single().Revoked = true;

        var ex = Assert.Throws<ExchangeSecurityException>(() =>
            MergeEngine.Commit(school, preview, NoChoices, TestData.Now));
        Assert.Contains("inzwischen abgeschlossen", ex.Message);
    }

    [Fact]
    public void FieldOutsideScope_RejectsWholeResponse()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var response = ResponseBuilder.Create(assignment, TestData.Now);
        response.Values[assignment.Students[0].Id] = new() { ["t:Allgemein"] = "eingeschmuggelt" };

        var ex = Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(school, response, TestData.Now));
        Assert.Contains("t:Allgemein", ex.Message);
    }

    [Fact]
    public void ForeignStudent_RejectsWholeResponse()
    {
        var school = TestData.School();
        var response = ResponseBuilder.Create(TestData.SportAssignment(school), TestData.Now);
        response.Values[Guid.NewGuid()] = new() { ["g:Sport"] = "1" };

        Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(school, response, TestData.Now));
    }

    [Fact]
    public void ResponseForOtherSchoolFile_IsRejected()
    {
        var school = TestData.School();
        var response = Respond(TestData.SportAssignment(school), a => Fill(a, 0, "g:Sport", "2"));
        Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(TestData.School(), response, TestData.Now));
    }

    [Fact]
    public void ResponseAfterExpiry_IsRejected()
    {
        var school = TestData.School();
        var response = Respond(TestData.SportAssignment(school), a => Fill(a, 0, "g:Sport", "2"), TestData.Now.AddDays(30));
        Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(school, response, TestData.Now.AddDays(30)));
    }

    [Fact]
    public void BothChangedSameField_IsConflict_AndKeepsSchoolByDefault()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var anna = school.Students[0].Id;
        school.ReportFor(anna).Set("g:Sport", "3", "Verwaltung", TestData.Now); // Verwaltung ändert nach Ausgabe
        var response = Respond(assignment, a => Fill(a, 0, "g:Sport", "1"));

        var preview = MergeEngine.Preview(school, response, TestData.Now);
        var conflict = Assert.Single(preview.Conflicts);
        Assert.Equal("3", conflict.SchoolValue);
        Assert.Equal("1", conflict.IncomingValue);
        Assert.Equal("Verwaltung", conflict.SchoolChangedBy);

        var result = MergeEngine.Commit(school, preview, NoChoices, TestData.Now);
        Assert.Equal(1, result.ConflictsKeptSchool);
        Assert.Equal("3", school.ReportFor(anna).Get("g:Sport"));
    }

    [Fact]
    public void Conflict_CanBeResolvedToIncoming()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var anna = school.Students[0].Id;
        school.ReportFor(anna).Set("g:Sport", "3", "Verwaltung", TestData.Now);
        var response = Respond(assignment, a => Fill(a, 0, "g:Sport", "1"));

        var preview = MergeEngine.Preview(school, response, TestData.Now);
        MergeEngine.Commit(school, preview,
            new Dictionary<(Guid, string), ConflictChoice> { [(anna, "g:Sport")] = ConflictChoice.TakeIncoming }, TestData.Now);

        Assert.Equal("1", school.ReportFor(anna).Get("g:Sport"));
    }

    [Fact]
    public void SchoolChangedOtherField_IsNotAConflict()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var anna = school.Students[0].Id;
        school.ReportFor(anna).Set("t:Allgemein", "Text", "Verwaltung", TestData.Now);
        var response = Respond(assignment, a => Fill(a, 0, "g:Sport", "1"));

        var preview = MergeEngine.Preview(school, response, TestData.Now);
        Assert.Single(preview.Applies);
        Assert.Empty(preview.Conflicts);
    }

    [Fact]
    public void UnchangedValues_AreIgnored()
    {
        var school = TestData.School();
        var anna = school.Students[0].Id;
        school.ReportFor(anna).Set("g:Sport", "2", "Verwaltung", TestData.Now);
        var response = Respond(TestData.SportAssignment(school), _ => { });

        var preview = MergeEngine.Preview(school, response, TestData.Now);
        Assert.Empty(preview.Items);
        Assert.Equal(1, preview.UnchangedCount);
    }

    [Fact]
    public void CorrectionResponse_FromSameTeacher_IsNotAConflict()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var anna = school.Students[0].Id;

        var first = Respond(assignment, a => Fill(a, 0, "g:Sport", "2"));
        MergeEngine.Commit(school, MergeEngine.Preview(school, first, TestData.Now), NoChoices, TestData.Now);

        var second = Respond(assignment, a => Fill(a, 0, "g:Sport", "1"));
        var preview = MergeEngine.Preview(school, second, TestData.Now);
        Assert.Single(preview.Applies);
        MergeEngine.Commit(school, preview, NoChoices, TestData.Now);
        Assert.Equal("1", school.ReportFor(anna).Get("g:Sport"));
    }

    [Fact]
    public async Task FullCycle_ThroughEncryptedFiles()
    {
        var codec = new EnvelopeCodec(new DotNetAeadCipher());
        var school = TestData.School();
        var assignmentBytes = await codec.SealAsync(TestData.SportAssignment(school), EnvelopeKind.Assignment, TestData.Password);

        var teacherCopy = await codec.OpenAsync<AssignmentFile>(assignmentBytes, EnvelopeKind.Assignment, TestData.Password);
        Fill(teacherCopy, 1, "g:Sport", "2");
        var responseBytes = await codec.SealAsync(ResponseBuilder.Create(teacherCopy, TestData.Now), EnvelopeKind.Response, TestData.Password);

        var response = await codec.OpenAsync<ResponseFile>(responseBytes, EnvelopeKind.Response, TestData.Password);
        var result = MergeEngine.Commit(school, MergeEngine.Preview(school, response, TestData.Now), NoChoices, TestData.Now);

        Assert.Equal(1, result.Applied);
        Assert.Equal("2", school.ReportFor(school.Students[1].Id).Get("g:Sport"));
    }
}
