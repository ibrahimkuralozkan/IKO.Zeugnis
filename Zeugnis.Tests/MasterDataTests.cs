using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class MasterDataTests
{
    private static readonly DateTimeOffset Now = TestData.Now;
    private static DateOnly Dob(int age) => DateOnly.FromDateTime(DateTime.Today).AddYears(-age);

    private static SchoolFile Empty() => new() { Term = new Term("2026/27", 1), School = new SchoolInfo { Name = "Testschule" } };

    [Fact]
    public void Teacher_NamesAreUnique_IgnoringCaseAndSpaces()
    {
        var f = Empty();
        MasterData.AddTeacher(f, "Frau  Beispiel", Gender.Female, Now);
        Assert.Equal("Frau Beispiel", f.Teachers[0].DisplayName);
        Assert.Throws<MasterDataException>(() => MasterData.AddTeacher(f, "frau beispiel ", Gender.Female, Now));
        Assert.Throws<MasterDataException>(() => MasterData.AddTeacher(f, "  ", Gender.Female, Now));
    }

    [Fact]
    public void ClassTeacher_CannotBeRemoved()
    {
        var f = Empty();
        var t = MasterData.AddTeacher(f, "Frau Beispiel", Gender.Female, Now);
        MasterData.AddClass(f, "3a", 3, t.Id, Now);
        Assert.Throws<MasterDataException>(() => MasterData.RemoveTeacher(f, t.Id, Now));

        MasterData.UpdateClass(f, "3a", "3a", 3, null, Now);
        MasterData.RemoveTeacher(f, t.Id, Now);
        Assert.Empty(f.Teachers);
    }

    [Fact]
    public void Classes_AreSortedNaturally_AndValidated()
    {
        var f = Empty();
        MasterData.AddClass(f, "4b", 4, null, Now);
        MasterData.AddClass(f, "1a", 1, null, Now);
        MasterData.AddClass(f, "4a", 4, null, Now);
        Assert.Equal(["1a", "4a", "4b"], f.Classes.Select(c => c.Name));
        Assert.Throws<MasterDataException>(() => MasterData.AddClass(f, "4A", 4, null, Now));
        Assert.Throws<MasterDataException>(() => MasterData.AddClass(f, "5a", 5, null, Now));
        Assert.Throws<MasterDataException>(() => MasterData.AddClass(f, "a/b", 2, null, Now));
    }

    [Fact]
    public void RenamingClass_MovesStudents()
    {
        var f = Empty();
        MasterData.AddClass(f, "3a", 3, null, Now);
        MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a" }, Now);
        MasterData.UpdateClass(f, "3a", "3c", 3, null, Now);
        Assert.Equal("3c", f.Students[0].ClassName);
    }

    [Fact]
    public void ClassWithStudents_CannotBeRemoved()
    {
        var f = Empty();
        MasterData.AddClass(f, "3a", 3, null, Now);
        MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a" }, Now);
        Assert.Throws<MasterDataException>(() => MasterData.RemoveClass(f, "3a", Now));
    }

    [Fact]
    public void Student_RequiresExistingClass_AndPlausibleBirthDate_AndNoDuplicate()
    {
        var f = Empty();
        MasterData.AddClass(f, "3a", 3, null, Now);
        Assert.Throws<MasterDataException>(() =>
            MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "9z" }, Now));
        Assert.Throws<MasterDataException>(() =>
            MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a", DateOfBirth = Dob(40) }, Now));

        MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a", DateOfBirth = Dob(8) }, Now);
        Assert.Throws<MasterDataException>(() =>
            MasterData.AddStudent(f, new Student { FirstName = "anna", LastName = "TEST", ClassName = "3a", DateOfBirth = Dob(8) }, Now));
        // Gleicher Name, anderes Geburtsdatum ist erlaubt.
        MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a", DateOfBirth = Dob(9) }, Now);
        Assert.Equal(2, f.Students.Count);
    }

    [Fact]
    public void StudentInOpenAssignment_IsProtected()
    {
        var f = TestData.School();
        var assignment = TestData.SportAssignment(f);
        var anna = assignment.Students[0];
        Assert.Throws<MasterDataException>(() => MasterData.RemoveStudent(f, anna.Id, Now));
        Assert.Throws<MasterDataException>(() => MasterData.RemoveTeacher(f, assignment.TeacherId, Now));
    }

    [Fact]
    public void RemovingStudent_RemovesReport()
    {
        var f = Empty();
        MasterData.AddClass(f, "3a", 3, null, Now);
        var s = MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a" }, Now);
        f.ReportFor(s.Id).Set("g:Sport", "2", "Verwaltung", Now);
        Assert.True(MasterData.HasReportData(f, s.Id));
        MasterData.RemoveStudent(f, s.Id, Now);
        Assert.Empty(f.Reports);
    }
}

public class StudentCsvImportTests
{
    private static SchoolFile School()
    {
        var f = new SchoolFile { Term = new Term("2026/27", 1) };
        MasterData.AddClass(f, "3a", 3, null, TestData.Now);
        return f;
    }

    [Fact]
    public void Parses_SemicolonCsv_WithBom_Quotes_AndAnyColumnOrder()
    {
        var csv = "﻿Klasse;Nachname;Vorname;Geburtsdatum;Geschlecht;Bildungsgang\r\n" +
                  "3a;\"Müller; Jr.\";Anna;01.02.2018;w;SE\r\n" +
                  "3a;Test;Ben;2018-03-04;m;\r\n";
        var p = StudentCsvImport.Parse(csv, School());

        Assert.Null(p.FatalError);
        Assert.Empty(p.Notes);
        Assert.Equal(2, p.NewRows.Count());
        var anna = p.Rows[0].Student!;
        Assert.Equal("Müller; Jr.", anna.LastName);
        Assert.Equal(new DateOnly(2018, 2, 1), anna.DateOfBirth);
        Assert.Equal(Gender.Female, anna.Gender);
        Assert.Equal(StudentTrack.SE, anna.Track);
        Assert.Equal(StudentTrack.Regular, p.Rows[1].Student!.Track);
    }

    [Fact]
    public void Parses_CommaCsv_AndNotesMissingOptionalColumns()
    {
        var p = StudentCsvImport.Parse("Vorname,Nachname,Klasse\nAnna,Test,3A\n", School());
        Assert.Single(p.NewRows);
        Assert.Equal("3a", p.Rows[0].Student!.ClassName); // auf vorhandene Schreibweise normalisiert
        Assert.Equal(3, p.Notes.Count);
    }

    [Fact]
    public void MissingRequiredColumn_IsFatal()
    {
        var p = StudentCsvImport.Parse("Vorname;Nachname\nAnna;Test\n", School());
        Assert.Contains("Klasse", p.FatalError);
    }

    [Fact]
    public void BadRows_AreReportedWithLineNumbers()
    {
        var csv = "Vorname;Nachname;Klasse;Geburtsdatum;Geschlecht\n" +
                  ";Test;3a;;\n" +
                  "Anna;Test;9z;;\n" +
                  "Ben;Test;3a;31.02.2018;\n" +
                  "Cem;Test;3a;;x\n";
        var p = StudentCsvImport.Parse(csv, School());
        Assert.All(p.Rows, r => Assert.Equal(ImportRowStatus.Error, r.Status));
        Assert.Equal([2, 3, 4, 5], p.Rows.Select(r => r.Line));
        Assert.Contains("Vorname fehlt", p.Rows[0].Message);
        Assert.Contains("9z", p.Rows[1].Message);
        Assert.Contains("unlesbar", p.Rows[2].Message);
        Assert.Contains("Geschlecht", p.Rows[3].Message);
    }

    [Fact]
    public void Duplicates_InSchoolAndInFile_AreSkipped()
    {
        var f = School();
        MasterData.AddStudent(f, new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a", DateOfBirth = new DateOnly(2018, 2, 1) }, TestData.Now);
        var csv = "Vorname;Nachname;Klasse;Geburtsdatum\nAnna;Test;3a;01.02.2018\nBen;Test;3a;\nben;test;3a;\n";
        var p = StudentCsvImport.Parse(csv, f);
        Assert.Equal([ImportRowStatus.Duplicate, ImportRowStatus.New, ImportRowStatus.Duplicate], p.Rows.Select(r => r.Status));

        Assert.Equal(1, StudentCsvImport.Apply(f, p, TestData.Now));
        Assert.Equal(2, f.Students.Count);
    }

    [Fact]
    public void MissingClasses_CanBeCreated()
    {
        var f = School();
        var p = StudentCsvImport.Parse("Vorname;Nachname;Klasse\nAnna;Test;2b\nBen;Test;2b\n", f, createMissingClasses: true);
        Assert.Equal(["2b"], p.MissingClasses);
        Assert.Equal(2, StudentCsvImport.Apply(f, p, TestData.Now));
        Assert.Equal(2, f.Classes.Single(c => c.Name == "2b").Grade);
    }
}
