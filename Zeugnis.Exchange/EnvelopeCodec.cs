using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zeugnis.Exchange;

public enum EnvelopeKind { School, Assignment, Response }

public sealed class ExchangeSecurityException(string message) : Exception(message);

/// <summary>
/// Rohe Kryptografie: PBKDF2-SHA256 → AES-256-GCM. Zwei Implementierungen:
/// .NET (Tests, Desktop) und Web Crypto (Browser/PWA, da .NET-WASM kein AES-GCM bietet).
/// Rückgabe von Encrypt = Ciphertext || 16-Byte-Tag.
/// </summary>
public interface IAeadCipher
{
    Task<byte[]> EncryptAsync(byte[] plain, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad);
    /// <exception cref="ExchangeSecurityException">Falsches Kennwort oder veränderte Daten.</exception>
    Task<byte[]> DecryptAsync(byte[] cipherAndTag, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad);
}

/// <summary>
/// Gemeinsames Dateiformat aller drei Dateiarten. Die Dateiart ist in die
/// authentifizierten Zusatzdaten eingebunden: Eine Rückgabe kann nicht als Schuldatei geöffnet werden.
/// </summary>
public sealed class EnvelopeCodec(IAeadCipher cipher)
{
    public const string Format = "IKO.Zeugnis";
    public const int Version = 1;
    public const int Iterations = 310_000;
    public const int MinPasswordLength = 12;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Extension(EnvelopeKind kind) => kind switch
    {
        EnvelopeKind.School => ".zschule",
        EnvelopeKind.Assignment => ".zauftrag",
        EnvelopeKind.Response => ".zantwort",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinPasswordLength)
            throw new ExchangeSecurityException($"Das Kennwort muss mindestens {MinPasswordLength} Zeichen lang sein.");
    }

    public async Task<byte[]> SealAsync<T>(T document, EnvelopeKind kind, string password)
    {
        ValidatePassword(password);
        var plain = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        try
        {
            var sealedBytes = await cipher.EncryptAsync(plain, password, salt, nonce, Iterations, Aad(kind));
            var envelope = new Envelope
            {
                Format = Format, Version = Version, Kind = kind, Kdf = "PBKDF2-SHA256", Iterations = Iterations,
                Salt = Convert.ToBase64String(salt), Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(sealedBytes),
            };
            return JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public async Task<T> OpenAsync<T>(byte[] bytes, EnvelopeKind kind, string password)
    {
        ValidatePassword(password);
        Envelope envelope;
        byte[] salt, nonce, sealedBytes;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json) ?? throw new JsonException();
            salt = Convert.FromBase64String(envelope.Salt);
            nonce = Convert.FromBase64String(envelope.Nonce);
            sealedBytes = Convert.FromBase64String(envelope.Ciphertext);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or NotSupportedException)
        {
            throw new ExchangeSecurityException("Die Datei ist beschädigt oder keine Zeugnis-Datei.");
        }

        if (envelope.Format != Format || envelope.Version != Version)
            throw new ExchangeSecurityException("Dieses Dateiformat wird nicht unterstützt.");
        if (envelope.Kind != kind)
            throw new ExchangeSecurityException($"Falsche Dateiart: erwartet {Describe(kind)}, erhalten {Describe(envelope.Kind)}.");
        if (envelope.Kdf != "PBKDF2-SHA256" || salt.Length != 16 || nonce.Length != 12 || sealedBytes.Length < 16 ||
            envelope.Iterations is < 100_000 or > 2_000_000)
            throw new ExchangeSecurityException("Die Sicherheitsparameter der Datei sind ungültig.");

        var plain = await cipher.DecryptAsync(sealedBytes, password, salt, nonce, envelope.Iterations, Aad(kind));
        try
        {
            return JsonSerializer.Deserialize<T>(plain, Json)
                ?? throw new ExchangeSecurityException("Der Inhalt der Datei ist ungültig.");
        }
        catch (JsonException) { throw new ExchangeSecurityException("Der Inhalt der Datei ist ungültig."); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    public static string Describe(EnvelopeKind kind) => kind switch
    {
        EnvelopeKind.School => "Schuldatei",
        EnvelopeKind.Assignment => "Arbeitsauftrag",
        EnvelopeKind.Response => "Rückgabe",
        _ => kind.ToString(),
    };

    private static byte[] Aad(EnvelopeKind kind) => Encoding.UTF8.GetBytes($"{Format}|{Version}|{kind}");

    private sealed class Envelope
    {
        public string Format { get; set; } = "";
        public int Version { get; set; }
        public EnvelopeKind Kind { get; set; }
        public string Kdf { get; set; } = "";
        public int Iterations { get; set; }
        public string Salt { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string Ciphertext { get; set; } = "";
    }
}

/// <summary>.NET-Implementierung (nicht im Browser verfügbar).</summary>
public sealed class DotNetAeadCipher : IAeadCipher
{
    public Task<byte[]> EncryptAsync(byte[] plain, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad)
    {
        var key = DeriveKey(password, salt, iterations);
        try
        {
            var output = new byte[plain.Length + 16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, output.AsSpan(0, plain.Length), output.AsSpan(plain.Length), aad);
            return Task.FromResult(output);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    public Task<byte[]> DecryptAsync(byte[] cipherAndTag, string password, byte[] salt, byte[] nonce, int iterations, byte[] aad)
    {
        var key = DeriveKey(password, salt, iterations);
        try
        {
            var length = cipherAndTag.Length - 16;
            var plain = new byte[length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipherAndTag.AsSpan(0, length), cipherAndTag.AsSpan(length), plain, aad);
            return Task.FromResult(plain);
        }
        catch (CryptographicException)
        {
            throw new ExchangeSecurityException("Das Kennwort ist falsch oder die Datei wurde verändert.");
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }

    private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, 32);
}
