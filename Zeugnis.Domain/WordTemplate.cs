namespace Zeugnis.Domain;

/// <summary>
/// Word-Vorlage (.docx) für eine Jahrgangsstufe und einen Bildungsgang. Liegt ausschließlich in der Schuldatei,
/// damit die Anwendung selbst keine Schul- oder Layoutdaten enthält.
/// </summary>
public sealed class WordTemplate
{
    public string FileName { get; set; } = "";
    public int Grade { get; set; }
    public StudentTrack Track { get; set; }
    public byte[] Content { get; set; } = [];
    public DateTimeOffset UploadedUtc { get; set; }
}
