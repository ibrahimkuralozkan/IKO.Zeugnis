using System.Text;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class EnvelopeCodecTests
{
    private readonly EnvelopeCodec _codec = new(new DotNetAeadCipher());

    /// <summary>Keine Datei, die die App erzeugt, darf Namen oder Bewertungen im Klartext enthalten.</summary>
    [Fact]
    public async Task Alle_drei_Dateien_enthalten_keine_Klartextdaten()
    {
        var school = TestData.School();
        school.ReportFor(school.Students[0].Id).Set("t:Allgemein", "Geheimer Bemerkungstext", "Verwaltung", TestData.Now);
        var assignment = TestData.SportAssignment(school);
        assignment.Reports.First().Set("g:Sport", "2", "Herr Muster", TestData.Now);
        var response = ResponseBuilder.Create(assignment, TestData.Now);

        var files = new[]
        {
            await _codec.SealAsync(school, EnvelopeKind.School, TestData.Password),
            await _codec.SealAsync(assignment, EnvelopeKind.Assignment, TestData.Password),
            await _codec.SealAsync(response, EnvelopeKind.Response, TestData.Password, ResponseBuilder.Reference(assignment.AssignmentId)),
        };

        // Lange Merkmale: zufälliger Geheimtext enthält sie praktisch nie zufällig.
        var secrets = new[] { "Testschule", "Frau Beispiel", "Herr Muster", "Geheimer Bemerkungstext", "bewegt sich sicher" };
        foreach (var bytes in files)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(bytes);
            var random = new[] { "Ciphertext", "Salt", "Nonce" };
            // Unverschlüsselter Kopf: nur Format, Version, Art, Verfahren und ggf. Auftragsnummer.
            var header = doc.RootElement.EnumerateObject()
                .Where(p => !random.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                .Select(p => p.Name).ToList();
            Assert.All(header, name => Assert.Contains(name, new[] { "Format", "Version", "Kind", "Ref", "Kdf", "Iterations" }, StringComparer.OrdinalIgnoreCase));

            var cipher = Convert.FromBase64String(doc.RootElement.EnumerateObject()
                .First(p => p.Name.Equals("Ciphertext", StringComparison.OrdinalIgnoreCase)).Value.GetString()!);
            foreach (var secret in secrets)
            {
                Assert.DoesNotContain(secret, System.Text.Encoding.UTF8.GetString(bytes));
                Assert.Equal(-1, cipher.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(secret)));
            }
        }
    }

    [Fact]
    public void Dateiendungen_zeigen_den_Empfaenger()
    {
        Assert.Equal(".schuldatei", EnvelopeCodec.Extension(EnvelopeKind.School));
        Assert.Equal(".an-lehrkraft", EnvelopeCodec.Extension(EnvelopeKind.Assignment));
        Assert.Equal(".an-verwaltung", EnvelopeCodec.Extension(EnvelopeKind.Response));
    }

    [Fact]
    public void Auftragsdatei_nennt_Klasse_Bereich_und_Lehrkraft()
    {
        var a = new AssignmentFile { ClassName = "3a", Label = "Deutsch", TeacherName = "Herr Ali" };
        Assert.Equal("Auftrag_3a_Deutsch_Herr_Ali.an-lehrkraft", AssignmentFactory.FileName(a));
    }

    [Fact]
    public async Task RoundTrip_PreservesSchoolFile()
    {
        var school = TestData.School();
        school.ReportFor(school.Students[0].Id).Set("t:Allgemein", "Äöü ß – Text", "Verwaltung", TestData.Now);

        var bytes = await _codec.SealAsync(school, EnvelopeKind.School, TestData.Password);
        var opened = await _codec.OpenAsync<SchoolFile>(bytes, EnvelopeKind.School, TestData.Password);

        Assert.Equal(school.SchoolFileId, opened.SchoolFileId);
        Assert.Equal(school.Term, opened.Term);
        Assert.Equal("Äöü ß – Text", opened.Reports.Single().Get("t:Allgemein"));
        Assert.Equal(Domain.StudentTrack.SE, opened.Students[1].Track);
    }

    [Fact]
    public async Task Ciphertext_DoesNotContainPlainNames()
    {
        var bytes = await _codec.SealAsync(TestData.School(), EnvelopeKind.School, TestData.Password);
        Assert.DoesNotContain("Anna", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task WrongPassword_IsRejected()
    {
        var bytes = await _codec.SealAsync(TestData.School(), EnvelopeKind.School, TestData.Password);
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.OpenAsync<Exchange.SchoolFile>(bytes, EnvelopeKind.School, "Falsches-Kennwort-1"));
    }

    [Fact]
    public async Task TamperedFile_IsRejected()
    {
        var bytes = await _codec.SealAsync(TestData.School(), EnvelopeKind.School, TestData.Password);
        var text = Encoding.UTF8.GetString(bytes);
        var start = text.IndexOf("\"Ciphertext\":\"", StringComparison.Ordinal) + 20;
        var tampered = text[..start] + (text[start] == 'A' ? 'B' : 'A') + text[(start + 1)..];
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.OpenAsync<SchoolFile>(Encoding.UTF8.GetBytes(tampered), EnvelopeKind.School, TestData.Password));
    }

    [Fact]
    public async Task WrongKind_IsRejected_EvenIfEnvelopeKindIsEdited()
    {
        var bytes = await _codec.SealAsync(new ResponseFile(), EnvelopeKind.Response, TestData.Password);
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.OpenAsync<SchoolFile>(bytes, EnvelopeKind.School, TestData.Password));

        // Auch eine umetikettierte Datei scheitert, weil die Dateiart authentifiziert ist.
        var relabeled = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("\"Response\"", "\"School\""));
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.OpenAsync<SchoolFile>(relabeled, EnvelopeKind.School, TestData.Password));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kurz")]
    [InlineData("elf-Zeichen")]
    public async Task ShortPassword_IsRejected(string password) =>
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.SealAsync(TestData.School(), EnvelopeKind.School, password));

    [Fact]
    public async Task Garbage_IsRejected() =>
        await Assert.ThrowsAsync<ExchangeSecurityException>(() =>
            _codec.OpenAsync<SchoolFile>("kein json"u8.ToArray(), EnvelopeKind.School, TestData.Password));
}
