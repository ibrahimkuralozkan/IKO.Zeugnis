using Microsoft.JSInterop;
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

public enum DraftState { None, Pending, Saved, Failed }

/// <summary>
/// Geöffneter Arbeitsauftrag einer Lehrkraft.
/// Änderungen werden nach kurzer Pause als verschlüsselter Entwurf im Gerätespeicher abgelegt
/// (gleiches Kennwort wie der Auftrag), damit nach einem Neuladen der Seite nichts verloren geht.
/// </summary>
public sealed class TeacherSession(EnvelopeCodec codec, IJSRuntime js)
{
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1.5);

    private string? _password;
    private CancellationTokenSource? _pending;

    public AssignmentFile? Assignment { get; private set; }
    public GradingContext? Grading { get; private set; }
    public bool IsOpen => Assignment is not null;
    public DraftState Draft { get; private set; }
    public DateTimeOffset? DraftSavedUtc { get; private set; }
    /// <summary>Zeitpunkt des wiederhergestellten Entwurfs, falls beim Öffnen einer gefunden wurde.</summary>
    public DateTimeOffset? RestoredFromUtc { get; private set; }
    public DateTimeOffset? LastResponseUtc { get; private set; }
    /// <summary>Änderungen seit der letzten Rückgabe.</summary>
    public bool ChangedSinceResponse { get; private set; }

    public event Action? Changed;

    private sealed class LocalDraft
    {
        public DateTimeOffset SavedUtc { get; set; }
        public AssignmentFile Assignment { get; set; } = new();
    }

    public async Task OpenAsync(byte[] bytes, string password)
    {
        var assignment = await codec.OpenAsync<AssignmentFile>(bytes, EnvelopeKind.Assignment, password);
        if (assignment.FormatVersion != AssignmentFile.CurrentFormatVersion || assignment.AssignmentId == Guid.Empty)
            throw new ExchangeSecurityException("Der Arbeitsauftrag hat eine nicht unterstützte Version.");

        RestoredFromUtc = null;
        var stored = await js.InvokeAsync<string?>("zeugnis.draft.get", assignment.AssignmentId.ToString("N"));
        if (stored is not null)
        {
            try
            {
                var draft = await codec.OpenAsync<LocalDraft>(Convert.FromBase64String(stored), EnvelopeKind.Assignment, password);
                if (draft.Assignment.AssignmentId == assignment.AssignmentId)
                {
                    assignment.Reports = draft.Assignment.Reports;
                    RestoredFromUtc = draft.SavedUtc;
                }
            }
            catch (Exception ex) when (ex is ExchangeSecurityException or FormatException) { /* unlesbarer Entwurf wird ignoriert */ }
        }

        Assignment = assignment;
        Grading = GradingContext.ForAssignment(assignment);
        _password = password;
        Draft = RestoredFromUtc is null ? DraftState.None : DraftState.Saved;
        DraftSavedUtc = RestoredFromUtc;
        LastResponseUtc = null;
        ChangedSinceResponse = RestoredFromUtc is not null;
        Changed?.Invoke();
    }

    /// <summary>Nach jeder Eingabe aufrufen: plant das Speichern des Entwurfs.</summary>
    public void MarkChanged()
    {
        ChangedSinceResponse = true;
        Draft = DraftState.Pending;
        _pending?.Cancel();
        var cts = _pending = new CancellationTokenSource();
        _ = SaveDraftLaterAsync(cts.Token);
        Changed?.Invoke();
    }

    private async Task SaveDraftLaterAsync(CancellationToken token)
    {
        try { await Task.Delay(AutosaveDelay, token); }
        catch (TaskCanceledException) { return; }
        await SaveDraftAsync();
    }

    public async Task SaveDraftAsync()
    {
        if (Assignment is null || _password is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            var bytes = await codec.SealAsync(new LocalDraft { SavedUtc = now, Assignment = Assignment }, EnvelopeKind.Assignment, _password);
            var ok = await js.InvokeAsync<bool>("zeugnis.draft.set", Assignment.AssignmentId.ToString("N"), Convert.ToBase64String(bytes));
            Draft = ok ? DraftState.Saved : DraftState.Failed;
            if (ok) DraftSavedUtc = now;
        }
        catch (Exception) { Draft = DraftState.Failed; }
        Changed?.Invoke();
    }

    public async Task DiscardDraftAsync()
    {
        if (Assignment is null) return;
        _pending?.Cancel();
        await js.InvokeVoidAsync("zeugnis.draft.remove", Assignment.AssignmentId.ToString("N"));
        Draft = DraftState.None;
        DraftSavedUtc = null;
        Changed?.Invoke();
    }

    public async Task<(byte[] Bytes, string FileName)> CreateResponseAsync()
    {
        if (Assignment is null || _password is null) throw new InvalidOperationException("Kein Arbeitsauftrag geöffnet.");
        var now = DateTimeOffset.UtcNow;
        var response = ResponseBuilder.Create(Assignment, now);
        var bytes = await codec.SealAsync(response, EnvelopeKind.Response, _password, ResponseBuilder.Reference(Assignment.AssignmentId));
        var name = $"Rueckgabe_{AssignmentFactory.ClassPart(Assignment.ClassNames, Assignment.ClassName)}_{FileService.SafeFileName(Assignment.Label)}_{FileService.SafeFileName(Assignment.TeacherName)}_{DateTime.Now:yyyyMMdd-HHmm}{EnvelopeCodec.Extension(EnvelopeKind.Response)}";
        LastResponseUtc = now;
        ChangedSinceResponse = false;
        Changed?.Invoke();
        return (bytes, name);
    }

    /// <summary>Schließt den Auftrag; ein ausstehender Entwurf wird vorher noch gespeichert.</summary>
    public async Task CloseAsync()
    {
        if (Draft == DraftState.Pending)
        {
            _pending?.Cancel();
            await SaveDraftAsync();
        }
        Assignment = null;
        Grading = null;
        _password = null;
        Draft = DraftState.None;
        RestoredFromUtc = null;
        Changed?.Invoke();
    }
}
