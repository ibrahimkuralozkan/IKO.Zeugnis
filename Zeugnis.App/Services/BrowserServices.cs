using System.Reflection;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Zeugnis.Exchange;

namespace Zeugnis.App.Services;

/// <summary>AES-GCM/PBKDF2 über Web Crypto, da .NET im Browser diese Verfahren nicht bereitstellt.</summary>
public sealed class WebCryptoAeadCipher(IJSRuntime js) : IAeadCipher
{
    public async Task<byte[]> EncryptAsync(byte[] plain, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad)
    {
        await EnsureSecureAsync();
        return await js.InvokeAsync<byte[]>("zeugnis.encrypt", plain, password, salt, nonce, iterations, aad);
    }

    public async Task<byte[]> DecryptAsync(byte[] cipherAndTag, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad)
    {
        await EnsureSecureAsync();
        try
        {
            return await js.InvokeAsync<byte[]>("zeugnis.decrypt", cipherAndTag, password, salt, nonce, iterations, aad);
        }
        catch (JSException)
        {
            throw new ExchangeSecurityException("Das Kennwort ist falsch oder die Datei wurde verändert.");
        }
    }

    private async Task EnsureSecureAsync()
    {
        if (!await js.InvokeAsync<bool>("zeugnis.isSecure"))
            throw new ExchangeSecurityException("Verschlüsselung ist nur über HTTPS oder localhost verfügbar.");
    }
}

public sealed class FileService(IJSRuntime js)
{
    /// <summary>Obergrenze je Datei. Schuldateien mit Word-Vorlagen bleiben deutlich darunter.</summary>
    public const long MaxFileBytes = 50 * 1024 * 1024;

    public async Task<byte[]> ReadAsync(IBrowserFile file)
    {
        if (file.Size > MaxFileBytes) throw new ExchangeSecurityException("Die Datei ist zu groß.");
        await using var stream = file.OpenReadStream(MaxFileBytes);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public ValueTask DownloadAsync(byte[] bytes, string fileName, string mime = "application/octet-stream") =>
        js.InvokeVoidAsync("zeugnis.download", bytes, fileName, mime);

    /// <summary>
    /// Textdatei lesen: zuerst UTF-8; enthält sie ungültige Bytes (typisch für Excel-„CSV (Trennzeichen-getrennt)“),
    /// dann Windows-1252/Latin-1, damit Umlaute erhalten bleiben.
    /// </summary>
    public static string DecodeText(byte[] bytes)
    {
        try { return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (System.Text.DecoderFallbackException) { return System.Text.Encoding.Latin1.GetString(bytes); }
    }

    public ValueTask SetUnloadGuardAsync(bool active) => js.InvokeVoidAsync("zeugnis.setUnloadGuard", active);

    public static string SafeFileName(string value) =>
        string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) || char.IsWhiteSpace(c) || c == '/' ? '_' : c));
}

public static class AppInfo
{
    public static string Version { get; } =
        (typeof(SchoolFile).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?")
        .Split('+')[0];
}
