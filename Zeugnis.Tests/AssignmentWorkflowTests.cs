using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class AssignmentWorkflowTests
{
    private readonly EnvelopeCodec _codec = new(new DotNetAeadCipher());

    [Fact]
    public void GeneratedPasswords_AreLongAndDistinct()
    {
        var a = AssignmentFactory.GeneratePassword();
        var b = AssignmentFactory.GeneratePassword();
        Assert.Equal(19, a.Length);
        Assert.NotEqual(a, b);
        Assert.DoesNotContain(a, c => "0O1lI".Contains(c));
    }

    [Fact]
    public void Create_StoresPasswordAndBaseline_AndBuildReproducesFile()
    {
        var school = TestData.School();
        var anna = school.Students[0].Id;
        school.ReportFor(anna).Set("g:Sport", "3", "Verwaltung", TestData.Now);

        var file = TestData.SportAssignment(school);
        var issued = school.Assignments.Single();
        Assert.False(string.IsNullOrEmpty(issued.Password));
        Assert.Equal("3", issued.Baseline[anna]["g:Sport"].Value);

        var rebuilt = AssignmentFactory.Build(school, issued);
        Assert.Equal(file.AssignmentId, rebuilt.AssignmentId);
        Assert.Equal(file.Students.Select(s => s.Id), rebuilt.Students.Select(s => s.Id));
        Assert.Equal("3", rebuilt.Reports.Single(r => r.StudentId == anna).Get("g:Sport"));
    }

    [Fact]
    public async Task Envelope_Reference_IsReadableButTamperProof()
    {
        var bytes = await _codec.SealAsync(new ResponseFile(), EnvelopeKind.Response, TestData.Password, "abc123");
        Assert.Equal((EnvelopeKind.Response, "abc123"), EnvelopeCodec.Peek(bytes));

        var forged = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("abc123", "xyz789"));
        Assert.Equal("xyz789", EnvelopeCodec.Peek(forged).Reference);
        await Assert.ThrowsAsync<ExchangeSecurityException>(() => _codec.OpenAsync<ResponseFile>(forged, EnvelopeKind.Response, TestData.Password));
    }

    [Fact]
    public async Task Verwaltung_OpensResponse_WithStoredPassword_AndLogsReturn()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var issued = school.Assignments.Single();

        // Lehrkraft
        var teacherBytes = await _codec.SealAsync(assignment, EnvelopeKind.Assignment, issued.Password);
        var teacherCopy = await _codec.OpenAsync<AssignmentFile>(teacherBytes, EnvelopeKind.Assignment, issued.Password);
        var ctx = GradingContext.ForAssignment(teacherCopy);
        ctx.Set(ctx.Students[0], "g:Sport", "1", TestData.Now);
        var teacherEntry = Assert.Single(teacherCopy.TeacherAudit);
        var responseBytes = await _codec.SealAsync(ResponseBuilder.Create(teacherCopy, TestData.Now), EnvelopeKind.Response,
            issued.Password, ResponseBuilder.Reference(teacherCopy.AssignmentId));

        // Verwaltung: kein Kennwort nötig
        var loaded = await ResponseReader.LoadAsync(_codec, school, responseBytes, TestData.Now);
        Assert.Same(issued, loaded.Assignment);
        var result = MergeEngine.Commit(school, loaded.Preview, new Dictionary<(Guid, string), ConflictChoice>(), TestData.Now);

        Assert.Equal(1, result.Applied);
        var record = Assert.Single(issued.Returns);
        Assert.Equal(loaded.Response.ResponseId, record.ResponseId);
        Assert.Equal(1, record.Applied);
        var importedAudit = Assert.Single(issued.TeacherAudit);
        Assert.Equal(teacherEntry.EntryId, importedAudit.EntryId);
        Assert.Equal("g:Sport", importedAudit.FieldKey);
        Assert.Equal("Test, Anna", importedAudit.StudentName);
        Assert.DoesNotContain(school.Audit, entry =>
            entry.Action == "Zeugnisfeld geändert" && entry.StudentId == importedAudit.StudentId);
    }

    [Fact]
    public void Response_RejectsTeacherAuditOutsideAssignmentScope()
    {
        var school = TestData.School();
        var assignment = TestData.SportAssignment(school);
        var response = ResponseBuilder.Create(assignment, TestData.Now);
        var student = assignment.Students[0];
        response.TeacherAudit.Add(new FieldAuditEntry(
            Guid.NewGuid(), TestData.Now, student.Id, student.DisplayName, student.ClassName,
            "t:Allgemein", "Allgemein", "", "eingeschmuggelt", "(leer)", "eingeschmuggelt"));

        Assert.Throws<ExchangeSecurityException>(() => MergeEngine.Preview(school, response, TestData.Now));
    }

    [Fact]
    public void OlderAssignmentWithoutTeacherAudit_OpensWithEmptyHistory()
    {
        var assignment = TestData.SportAssignment(TestData.School());
        var json = JsonNode.Parse(JsonSerializer.Serialize(assignment, EnvelopeCodec.Json))!.AsObject();
        Assert.True(json.Remove(nameof(AssignmentFile.TeacherAudit)));

        var reopened = JsonSerializer.Deserialize<AssignmentFile>(json, EnvelopeCodec.Json)!;

        Assert.Empty(reopened.TeacherAudit);
    }

    [Fact]
    public async Task ResponseFromOtherSchoolFile_IsRejectedWithHint()
    {
        var school = TestData.School();
        var bytes = await _codec.SealAsync(new ResponseFile(), EnvelopeKind.Response, TestData.Password, Guid.NewGuid().ToString("N"));
        var ex = await Assert.ThrowsAsync<ExchangeSecurityException>(() => ResponseReader.LoadAsync(_codec, school, bytes, TestData.Now));
        Assert.Contains("neueste Schuldatei", ex.Message);
    }

    [Fact]
    public async Task WrongFileType_IsRejected()
    {
        var school = TestData.School();
        var bytes = await _codec.SealAsync(TestData.SportAssignment(school), EnvelopeKind.Assignment, TestData.Password);
        var ex = await Assert.ThrowsAsync<ExchangeSecurityException>(() => ResponseReader.LoadAsync(_codec, school, bytes, TestData.Now));
        Assert.Contains("Arbeitsauftrag", ex.Message);
    }

    [Fact]
    public void Overlaps_AreDetected_ForSameStudentsAndFields()
    {
        var school = TestData.School();
        TestData.SportAssignment(school);
        var ids = school.Students.Select(s => s.Id).ToList();

        var same = AssignmentFactory.FindOverlaps(school, ["3a"], ids, ["g:Sport"]);
        var overlap = Assert.Single(same);
        Assert.Equal(2, overlap.Students);

        Assert.Empty(AssignmentFactory.FindOverlaps(school, ["3a"], ids, ["t:Allgemein"]));
        school.Assignments[0].Revoked = true;
        Assert.Empty(AssignmentFactory.FindOverlaps(school, ["3a"], ids, ["g:Sport"]));
    }

    [Fact]
    public void RatingAssignment_ContainsOnlyAllowedCriteria()
    {
        var school = TestData.School();
        var section = school.Catalogs[0].Sections[0];
        section.Items.Add(new CatalogItem { Id = "02", Text = "schwimmt" });
        var file = AssignmentFactory.Create(school,
            new AssignmentRequest(TestData.Sport(school).Id, ["3a"], "nur 01", ["r:Sport.01"], TestData.Now.AddDays(3)), TestData.Now);
        Assert.Equal(["01"], file.Catalogs.Single().Sections.Single().Items.Select(i => i.Id));
    }

    [Fact]
    public void CatalogElements_StayIndependentAndKeepUserOrder()
    {
        var school = TestData.School();
        var catalog = school.Catalogs[0];
        catalog.Sections =
        [
            new CatalogSection { Key = "Deutsch", Title = "Deutsch Gesamtnote", Subject = "Deutsch", Kind = FieldKind.Grade },
            new CatalogSection { Key = "Deutsch-Rechtschreiben", Title = "Rechtschreiben", Subject = "Deutsch", Kind = FieldKind.Rating },
            new CatalogSection { Key = "Deutsch-Lesen", Title = "Lesen", Subject = "Deutsch", Kind = FieldKind.Rating },
            new CatalogSection { Key = "Deutsch", Title = "Deutsch Bemerkungen", Subject = "Deutsch", Kind = FieldKind.Text },
            new CatalogSection { Key = "Mathematik", Title = "Mathematik Gesamtnote", Subject = "Mathematik", Kind = FieldKind.Grade },
        ];

        var elements = AssignmentFactory.ElementsFor(school, school.Students.Where(s => s.Track == StudentTrack.Regular));

        Assert.Equal(
            ["g:Deutsch", "r:Deutsch-Rechtschreiben.*", "r:Deutsch-Lesen.*", "t:Deutsch", "g:Mathematik"],
            elements.Select(e => e.Pattern));
        Assert.Equal([FieldKind.Grade, FieldKind.Rating, FieldKind.Rating, FieldKind.Text, FieldKind.Grade],
            elements.Select(e => e.Kind));
        Assert.All(elements.Take(4), e => Assert.Equal("Deutsch", e.Subject));
    }

    [Fact]
    public void FieldLabels_AreReadable()
    {
        var school = TestData.School();
        school.Catalogs[0].Id = Catalog.IdFor(3, StudentTrack.Regular);
        var anna = school.Students[0].Id;

        var (field, format) = FieldLabels.Describe(school, anna, "r:Sport.01");
        Assert.Equal("Sport: bewegt sich sicher", field);
        Assert.Equal("trifft voll zu", format("1"));
        Assert.Equal("(leer)", format(""));
        Assert.Equal("Sport (Note)", FieldLabels.Describe(school, anna, "g:Sport").Field);
    }
}
