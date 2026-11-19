using Microsoft.AspNetCore.Components;
using Zeugnis.App.Services;
using Zeugnis.Exchange;

namespace Zeugnis.App.Shared;

/// <summary>Grundlage aller Verwaltungsseiten: Zugriff auf die Schuldatei und einheitliche Fehleranzeige.</summary>
public abstract class AdminPageBase : ComponentBase, IDisposable
{
    [Inject] protected SchoolSession Session { get; set; } = default!;

    protected SchoolFile? File => Session.File;
    protected string? Error { get; set; }
    protected string? Success { get; set; }

    protected override void OnInitialized() => Session.Changed += OnSessionChanged;
    public virtual void Dispose() => Session.Changed -= OnSessionChanged;
    private void OnSessionChanged() => InvokeAsync(StateHasChanged);

    /// <summary>Führt eine Änderung aus; bei Erfolg wird die Schuldatei als geändert markiert.</summary>
    protected bool Change(Action<SchoolFile> action, string? success = null)
    {
        Error = null;
        Success = null;
        if (File is null) return false;
        try
        {
            action(File);
            Success = success;
            Session.MarkDirty();
            return true;
        }
        catch (MasterDataException ex)
        {
            Error = ex.Message;
            return false;
        }
    }

    protected void ClearMessages() { Error = null; Success = null; }
}
