namespace Zeugnis.Domain;

/// <summary>Schuljahr und Halbjahr, z. B. 2025/26, 2. Halbjahr.</summary>
public sealed record Term(string SchoolYear, int HalfYear)
{
    public bool IsValid =>
        HalfYear is 1 or 2 &&
        System.Text.RegularExpressions.Regex.IsMatch(SchoolYear ?? "", @"^\d{4}/\d{2}$");

    public override string ToString() => $"{SchoolYear}, {HalfYear}. Halbjahr";
}

public sealed class SchoolInfo
{
    public string Name { get; set; } = "";
    public string AddressLine1 { get; set; } = "";
    public string AddressLine2 { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Town { get; set; } = "";
    public string LeftLogoDataUrl { get; set; } = "";
    public string RightLogoDataUrl { get; set; } = "";
}

public enum Gender { Unknown, Male, Female, Diverse }

/// <summary>Bildungsgang: Regelschüler, Sprachförderung/SE, Förderschwerpunkt.</summary>
public enum StudentTrack { Regular, SE, Support }

public sealed class Teacher
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Anzeigename inkl. Anrede, wie er im Zeugnis erscheint (z. B. „Frau Müller“).</summary>
    public string DisplayName { get; set; } = "";
    public Gender Gender { get; set; }
}

public sealed class SchoolClass
{
    /// <summary>Klassenbezeichnung, z. B. „3a“. Eindeutig innerhalb der Schuldatei.</summary>
    public string Name { get; set; } = "";
    /// <summary>Jahrgangsstufe 1–4.</summary>
    public int Grade { get; set; }
    public Guid? ClassTeacherId { get; set; }

    // Angaben, die für alle Zeugnisse der Klasse gelten (Halbjahr der Schuldatei).
    /// <summary>Datum der Zeugniskonferenz.</summary>
    public DateOnly? ConferenceDate { get; set; }
    /// <summary>Ausgabedatum des Zeugnisses (erscheint mit dem Schulort).</summary>
    public DateOnly? ReportDate { get; set; }
    /// <summary>Wiederbeginn des Unterrichts nach den Ferien.</summary>
    public DateOnly? ResumptionDate { get; set; }
    /// <summary>Uhrzeit des Wiederbeginns, z. B. „08:00“ (optional).</summary>
    public string ResumptionTime { get; set; } = "";
}

public sealed class Student
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly? DateOfBirth { get; set; }
    public Gender Gender { get; set; }
    public string ClassName { get; set; } = "";
    public StudentTrack Track { get; set; }

    public string DisplayName => $"{LastName}, {FirstName}";
}
