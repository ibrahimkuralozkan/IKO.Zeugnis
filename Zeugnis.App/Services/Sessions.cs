using Zeugnis.Domain;
using Zeugnis.Exchange;

namespace Zeugnis.App.Services;

/// <summary>
/// Geöffnete Schuldatei der Verwaltung. Lebt nur im Arbeitsspeicher dieses Tabs;
/// es wird nichts im Browser-Speicher abgelegt.
/// </summary>
public sealed class SchoolSession(EnvelopeCodec codec)
{
    private string? _password;

    public SchoolFile? File { get; private set; }
    public bool IsOpen => File is not null;
    /// <summary>Ungespeicherte Änderungen seit dem letzten Speichern.</summary>
    public bool IsDirty { get; private set; }

    public event Action? Changed;

    public void CreateNew(SchoolInfo school, Term term, string password)
    {
        EnvelopeCodec.ValidatePassword(password);
        if (!term.IsValid) throw new ExchangeSecurityException("Bitte Schuljahr im Format 2026/27 und Halbjahr 1 oder 2 angeben.");
        if (string.IsNullOrWhiteSpace(school.Name)) throw new ExchangeSecurityException("Bitte den Schulnamen angeben.");
        var now = DateTimeOffset.UtcNow;
        File = new SchoolFile { Term = term, School = school, CreatedUtc = now, UpdatedUtc = now, Revision = 0 };
        File.Audit.Add(new AuditEntry(now, "Schuldatei erstellt", $"{school.Name} · {term}"));
        _password = password;
        IsDirty = true;
        Changed?.Invoke();
    }

    public async Task OpenAsync(byte[] bytes, string password)
    {
        var file = await codec.OpenAsync<SchoolFile>(bytes, EnvelopeKind.School, password);
        if (file.FormatVersion != SchoolFile.CurrentFormatVersion || file.SchoolFileId == Guid.Empty || !file.Term.IsValid)
            throw new ExchangeSecurityException("Die Schuldatei ist unvollständig oder hat eine nicht unterstützte Version.");
        File = file;
        _password = password;
        IsDirty = false;
        Changed?.Invoke();
    }

    public void MarkDirty() { IsDirty = true; Changed?.Invoke(); }

    /// <summary>Erhöht die Revision und liefert die verschlüsselte Datei samt Dateinamen.</summary>
    public async Task<(byte[] Bytes, string FileName)> SaveAsync()
    {
        if (File is null || _password is null) throw new InvalidOperationException("Keine Schuldatei geöffnet.");
        File.Revision++;
        File.UpdatedUtc = DateTimeOffset.UtcNow;
        try
        {
            var bytes = await codec.SealAsync(File, EnvelopeKind.School, _password);
            IsDirty = false;
            Changed?.Invoke();
            var name = $"Schuldatei_{FileService.SafeFileName(File.Term.SchoolYear.Replace('/', '-'))}_HJ{File.Term.HalfYear}_R{File.Revision}{EnvelopeCodec.Extension(EnvelopeKind.School)}";
            return (bytes, name);
        }
        catch
        {
            File.Revision--;
            throw;
        }
    }

    public void Close()
    {
        File = null;
        _password = null;
        IsDirty = false;
        Changed?.Invoke();
    }
}

/// <summary>Geöffneter Arbeitsauftrag einer Lehrkraft (nur im Arbeitsspeicher).</summary>
public sealed class TeacherSession(EnvelopeCodec codec)
{
    private string? _password;

    public AssignmentFile? Assignment { get; private set; }
    public bool IsOpen => Assignment is not null;
    public event Action? Changed;

    public async Task OpenAsync(byte[] bytes, string password)
    {
        var assignment = await codec.OpenAsync<AssignmentFile>(bytes, EnvelopeKind.Assignment, password);
        if (assignment.FormatVersion != AssignmentFile.CurrentFormatVersion || assignment.AssignmentId == Guid.Empty)
            throw new ExchangeSecurityException("Der Arbeitsauftrag hat eine nicht unterstützte Version.");
        Assignment = assignment;
        _password = password;
        Changed?.Invoke();
    }

    public async Task<(byte[] Bytes, string FileName)> CreateResponseAsync()
    {
        if (Assignment is null || _password is null) throw new InvalidOperationException("Kein Arbeitsauftrag geöffnet.");
        var response = ResponseBuilder.Create(Assignment, DateTimeOffset.UtcNow);
        var bytes = await codec.SealAsync(response, EnvelopeKind.Response, _password);
        var name = $"Rueckgabe_{FileService.SafeFileName(Assignment.ClassName)}_{FileService.SafeFileName(Assignment.Label)}_{DateTime.Now:yyyyMMdd-HHmm}{EnvelopeCodec.Extension(EnvelopeKind.Response)}";
        return (bytes, name);
    }

    public void Close()
    {
        Assignment = null;
        _password = null;
        Changed?.Invoke();
    }
}
