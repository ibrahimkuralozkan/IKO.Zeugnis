using System.Text;
using Zeugnis.Exchange;

namespace Zeugnis.Tests;

public class EnvelopeCodecTests
{
    private readonly EnvelopeCodec _codec = new(new DotNetAeadCipher());

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
