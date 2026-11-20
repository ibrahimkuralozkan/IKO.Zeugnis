using Zeugnis.Domain;

namespace Zeugnis.Exchange;

/// <summary>Verständliche Ablehnung einer Stammdatenänderung (Text wird direkt angezeigt).</summary>
public sealed class MasterDataException(string message) : Exception(message);

/// <summary>
/// Alle Änderungen an Schule, Lehrkräften, Klassen und Schülern laufen hierüber,
/// damit die Schuldatei in sich stimmig bleibt (eindeutige Namen, keine verwaisten Verweise).
/// </summary>
public static class MasterData
{
    // ---------- Lehrkräfte ----------

    public static Teacher AddTeacher(SchoolFile f, string displayName, Gender gender, DateTimeOffset now)
    {
        var name = Required(displayName, "Bitte den Namen der Lehrkraft angeben.");
        if (f.Teachers.Any(t => Same(t.DisplayName, name)))
            throw new MasterDataException($"Die Lehrkraft „{name}“ ist bereits vorhanden.");
        var teacher = new Teacher { DisplayName = name, Gender = gender };
        f.Teachers.Add(teacher);
        Log(f, now, "Lehrkraft angelegt", name);
        return teacher;
    }

    public static void UpdateTeacher(SchoolFile f, Guid id, string displayName, Gender gender, DateTimeOffset now)
    {
        var teacher = TeacherById(f, id);
        var name = Required(displayName, "Bitte den Namen der Lehrkraft angeben.");
        if (f.Teachers.Any(t => t.Id != id && Same(t.DisplayName, name)))
            throw new MasterDataException($"Die Lehrkraft „{name}“ ist bereits vorhanden.");
        if (teacher.DisplayName != name) Log(f, now, "Lehrkraft umbenannt", $"{teacher.DisplayName} → {name}");
        teacher.DisplayName = name;
        teacher.Gender = gender;
    }

    public static void RemoveTeacher(SchoolFile f, Guid id, DateTimeOffset now)
    {
        var teacher = TeacherById(f, id);
        var cls = f.Classes.FirstOrDefault(c => c.ClassTeacherId == id);
        if (cls is not null)
            throw new MasterDataException($"„{teacher.DisplayName}“ ist Klassenlehrkraft der {cls.Name}. Bitte zuerst dort ändern.");
        if (f.Assignments.Any(a => a.TeacherId == id && !a.Revoked))
            throw new MasterDataException($"„{teacher.DisplayName}“ hat noch offene Arbeitsaufträge.");
        f.Teachers.Remove(teacher);
        Log(f, now, "Lehrkraft entfernt", teacher.DisplayName);
    }

    // ---------- Klassen ----------

    public static SchoolClass AddClass(SchoolFile f, string name, int grade, Guid? classTeacherId, DateTimeOffset now)
    {
        var clean = ClassName(name);
        ValidateGrade(grade);
        if (f.Classes.Any(c => Same(c.Name, clean)))
            throw new MasterDataException($"Die Klasse „{clean}“ ist bereits vorhanden.");
        if (classTeacherId is { } tid) TeacherById(f, tid);
        var cls = new SchoolClass { Name = clean, Grade = grade, ClassTeacherId = classTeacherId };
        f.Classes.Add(cls);
        f.Classes.Sort((a, b) => NaturalCompare(a.Name, b.Name));
        Log(f, now, "Klasse angelegt", clean);
        return cls;
    }

    public static void UpdateClass(SchoolFile f, string currentName, string newName, int grade, Guid? classTeacherId, DateTimeOffset now)
    {
        var cls = ClassByName(f, currentName);
        var clean = ClassName(newName);
        ValidateGrade(grade);
        if (f.Classes.Any(c => c != cls && Same(c.Name, clean)))
            throw new MasterDataException($"Die Klasse „{clean}“ ist bereits vorhanden.");
        if (classTeacherId is { } tid) TeacherById(f, tid);
        if (grade != cls.Grade && f.Assignments.Any(a => a.Classes().Contains(cls.Name) && !a.Revoked))
            throw new MasterDataException("Die Jahrgangsstufe kann nicht geändert werden, solange Arbeitsaufträge dieser Klasse offen sind.");

        if (cls.Name != clean)
        {
            foreach (var s in f.Students.Where(s => s.ClassName == cls.Name)) s.ClassName = clean;
            foreach (var a in f.Assignments.Where(a => a.Classes().Contains(cls.Name)))
            {
                a.ClassNames = a.Classes().Select(n => n == cls.Name ? clean : n).ToList();
                a.ClassName = string.Join(", ", a.ClassNames);
            }
            Log(f, now, "Klasse umbenannt", $"{cls.Name} → {clean}");
            cls.Name = clean;
            f.Classes.Sort((a, b) => NaturalCompare(a.Name, b.Name));
        }
        cls.Grade = grade;
        cls.ClassTeacherId = classTeacherId;
    }

    /// <summary>Zeugnisangaben der Klasse: Konferenz, Ausgabedatum, Wiederbeginn des Unterrichts.</summary>
    public static void SetClassReportDates(SchoolFile f, string name, DateOnly? conference, DateOnly? report,
        DateOnly? resumption, string? resumptionTime)
    {
        var cls = ClassByName(f, name);
        var time = (resumptionTime ?? "").Trim();
        if (time.Length > 0)
        {
            // Browser liefern je nach Gerät „8:00“, „08:00“ oder „08:00:00“; gespeichert wird „08:00“.
            if (!TimeOnly.TryParseExact(time, ["H:mm", "HH:mm", "HH:mm:ss"], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var parsed))
                throw new MasterDataException("Bitte die Uhrzeit im Format 08:00 angeben.");
            time = parsed.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }
        cls.ConferenceDate = conference;
        cls.ReportDate = report;
        cls.ResumptionDate = resumption;
        cls.ResumptionTime = time;
    }

    public static void RemoveClass(SchoolFile f, string name, DateTimeOffset now)
    {
        var cls = ClassByName(f, name);
        var count = f.Students.Count(s => s.ClassName == cls.Name);
        if (count > 0)
            throw new MasterDataException($"Die Klasse {cls.Name} enthält noch {count} Schüler.");
        f.Classes.Remove(cls);
        Log(f, now, "Klasse entfernt", cls.Name);
    }

    // ---------- Schüler ----------

    public static Student AddStudent(SchoolFile f, Student input, DateTimeOffset now)
    {
        var student = new Student();
        Apply(f, student, input);
        f.Students.Add(student);
        Log(f, now, "Schüler angelegt", $"{student.DisplayName} · {student.ClassName}");
        return student;
    }

    public static void UpdateStudent(SchoolFile f, Guid id, Student input, DateTimeOffset now)
    {
        var student = StudentById(f, id);
        var oldClass = student.ClassName;
        if (input.ClassName != oldClass && f.Assignments.Any(a => !a.Revoked && a.StudentIds.Contains(id)))
            throw new MasterDataException("Der Schüler ist in einem offenen Arbeitsauftrag enthalten und kann nicht die Klasse wechseln.");
        Apply(f, student, input);
        if (oldClass != student.ClassName) Log(f, now, "Schüler versetzt", $"{student.DisplayName} · {oldClass} → {student.ClassName}");
    }

    /// <summary>Entfernt den Schüler samt Zeugniswerten.</summary>
    public static void RemoveStudent(SchoolFile f, Guid id, DateTimeOffset now)
    {
        var student = StudentById(f, id);
        if (f.Assignments.Any(a => !a.Revoked && a.StudentIds.Contains(id)))
            throw new MasterDataException("Der Schüler ist in einem offenen Arbeitsauftrag enthalten.");
        f.Students.Remove(student);
        f.Reports.RemoveAll(r => r.StudentId == id);
        Log(f, now, "Schüler entfernt", $"{student.DisplayName} · {student.ClassName}");
    }

    /// <summary>Hat der Schüler bereits eingetragene Zeugniswerte?</summary>
    public static bool HasReportData(SchoolFile f, Guid studentId) =>
        f.Reports.Any(r => r.StudentId == studentId && r.Fields.Values.Any(v => v.Value.Length > 0));

    public static bool IsDuplicate(SchoolFile f, Student candidate, Guid? ignoreId = null) =>
        f.Students.Any(s => s.Id != ignoreId &&
                            Same(s.FirstName, candidate.FirstName) &&
                            Same(s.LastName, candidate.LastName) &&
                            s.DateOfBirth == candidate.DateOfBirth);

    // ---------- Hilfen ----------

    /// <summary>Grundschulalter: zwischen 3 und 16 Jahre alt.</summary>
    public static bool IsPlausibleBirthDate(DateOnly dob)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return dob <= today.AddYears(-3) && dob >= today.AddYears(-16);
    }

    private static void Apply(SchoolFile f, Student target, Student input)
    {
        var first = Required(input.FirstName, "Bitte den Vornamen angeben.");
        var last = Required(input.LastName, "Bitte den Nachnamen angeben.");
        var cls = ClassByName(f, input.ClassName);
        if (input.DateOfBirth is { } dob && !IsPlausibleBirthDate(dob))
            throw new MasterDataException("Das Geburtsdatum ist nicht plausibel.");
        var probe = new Student { FirstName = first, LastName = last, DateOfBirth = input.DateOfBirth };
        if (IsDuplicate(f, probe, target.Id))
            throw new MasterDataException($"{last}, {first} mit diesem Geburtsdatum ist bereits vorhanden.");
        target.FirstName = first;
        target.LastName = last;
        target.DateOfBirth = input.DateOfBirth;
        target.Gender = input.Gender;
        target.Track = input.Track;
        target.ClassName = cls.Name;
    }

    public static Teacher TeacherById(SchoolFile f, Guid id) =>
        f.Teachers.FirstOrDefault(t => t.Id == id) ?? throw new MasterDataException("Die Lehrkraft ist nicht vorhanden.");

    public static SchoolClass ClassByName(SchoolFile f, string? name) =>
        f.Classes.FirstOrDefault(c => Same(c.Name, name?.Trim() ?? ""))
        ?? throw new MasterDataException(string.IsNullOrWhiteSpace(name) ? "Bitte eine Klasse wählen." : $"Die Klasse „{name.Trim()}“ ist nicht vorhanden.");

    private static Student StudentById(SchoolFile f, Guid id) =>
        f.Students.FirstOrDefault(s => s.Id == id) ?? throw new MasterDataException("Der Schüler ist nicht vorhanden.");

    private static string ClassName(string? name)
    {
        var clean = Required(name, "Bitte eine Klassenbezeichnung angeben.");
        if (clean.Length > 12 || clean.Any(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*'))
            throw new MasterDataException("Die Klassenbezeichnung ist ungültig (max. 12 Zeichen, z. B. „3a“).");
        return clean;
    }

    private static void ValidateGrade(int grade)
    {
        if (grade is < 1 or > 4) throw new MasterDataException("Die Jahrgangsstufe muss zwischen 1 und 4 liegen.");
    }

    private static string Required(string? value, string message)
    {
        var clean = string.Join(' ', (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (clean.Length == 0) throw new MasterDataException(message);
        if (clean.Length > 100) throw new MasterDataException("Der Text ist zu lang (max. 100 Zeichen).");
        return clean;
    }

    internal static bool Same(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>Sortiert „2a“ vor „10a“.</summary>
    public static int NaturalCompare(string a, string b)
    {
        static (int, string) Key(string s)
        {
            var digits = new string(s.TakeWhile(char.IsDigit).ToArray());
            return (digits.Length is > 0 and < 9 ? int.Parse(digits) : int.MaxValue, s[digits.Length..]);
        }
        var (na, ra) = Key(a);
        var (nb, rb) = Key(b);
        return na != nb ? na.CompareTo(nb) : string.Compare(ra, rb, StringComparison.CurrentCultureIgnoreCase);
    }

    private static void Log(SchoolFile f, DateTimeOffset now, string action, string detail) =>
        f.Audit.Add(new AuditEntry(now, action, detail));
}
