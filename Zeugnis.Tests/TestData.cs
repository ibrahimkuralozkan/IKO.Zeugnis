using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

/// <summary>Ausschließlich erfundene Testdaten.</summary>
internal static class TestData
{
    public const string Password = "Test-Kennwort-123";
    public static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    public static SchoolFile School()
    {
        var klassenlehrerin = new Teacher { DisplayName = "Frau Beispiel", Gender = Gender.Female };
        var sportlehrer = new Teacher { DisplayName = "Herr Muster", Gender = Gender.Male };
        var school = new SchoolFile
        {
            Term = new Term("2026/27", 1),
            School = new SchoolInfo { Name = "Testschule" },
            Teachers = [klassenlehrerin, sportlehrer],
            Classes = [new SchoolClass { Name = "3a", Grade = 3, ClassTeacherId = klassenlehrerin.Id }],
            Students =
            [
                new Student { FirstName = "Anna", LastName = "Test", ClassName = "3a" },
                new Student { FirstName = "Ben", LastName = "Test", ClassName = "3a", Track = StudentTrack.SE },
            ],
            Catalogs =
            [
                new Catalog
                {
                    Id = "K3", Grade = 3, Track = StudentTrack.Regular,
                    Sections =
                    [
                        new CatalogSection { Key = "Sport", Subject = "Sport", Kind = FieldKind.Rating,
                            Items = [new CatalogItem { Id = "01", Text = "bewegt sich sicher" }] },
                        new CatalogSection { Key = "Sport", Subject = "Sport", Kind = FieldKind.Grade },
                        new CatalogSection { Key = "Allgemein", Kind = FieldKind.Text },
                    ],
                },
            ],
        };
        return school;
    }

    public static Teacher Sport(SchoolFile s) => s.Teachers[1];

    public static AssignmentFile SportAssignment(SchoolFile school) =>
        AssignmentFactory.Create(school,
            new AssignmentRequest(Sport(school).Id, ["3a"], "Sport", ["r:Sport.*", "g:Sport"], Now.AddDays(14)),
            Now);
}
