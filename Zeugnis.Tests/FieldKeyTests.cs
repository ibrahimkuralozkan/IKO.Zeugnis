using Zeugnis.Domain;

namespace Zeugnis.Tests;

public class FieldKeyTests
{
    [Theory]
    [InlineData("r:Deutsch.Lesen.03", FieldKind.Rating, "Deutsch.Lesen.03")]
    [InlineData("t:Allgemein", FieldKind.Text, "Allgemein")]
    [InlineData("g:Mathematik", FieldKind.Grade, "Mathematik")]
    [InlineData("s:Versetzung", FieldKind.Selection, "Versetzung")]
    public void Parse_RoundTrips(string text, FieldKind kind, string path)
    {
        var key = FieldKey.Parse(text);
        Assert.Equal(kind, key.Kind);
        Assert.Equal(path, key.Path);
        Assert.Equal(text, key.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x:Foo")]
    [InlineData("t:")]
    [InlineData("t:mit Leerzeichen")]
    [InlineData("tAllgemein")]
    public void TryParse_RejectsInvalid(string? text) => Assert.False(FieldKey.TryParse(text, out _));

    [Theory]
    [InlineData("r:Sport.01", true)]
    [InlineData("g:Sport", true)]
    [InlineData("r:Sportunterricht.01", false)]
    [InlineData("t:Allgemein", false)]
    public void Scope_MatchesPrefixAndExact(string key, bool expected) =>
        Assert.Equal(expected, FieldScope.IsAllowed(key, ["r:Sport.*", "g:Sport"]));

    [Theory]
    [InlineData("r:Sport.*", true)]
    [InlineData("t:*", true)]
    [InlineData("g:Sport", true)]
    [InlineData("r:Sport*", false)]
    [InlineData("*", false)]
    [InlineData("q:*", false)]
    public void Pattern_Validation(string pattern, bool expected) =>
        Assert.Equal(expected, FieldScope.IsValidPattern(pattern));
}
