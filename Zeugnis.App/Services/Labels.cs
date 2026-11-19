using Zeugnis.Domain;

namespace Zeugnis.App.Services;

/// <summary>Deutsche Anzeigetexte für Aufzählungen.</summary>
public static class Labels
{
    public static string Of(Gender g) => g switch
    {
        Gender.Male => "männlich",
        Gender.Female => "weiblich",
        Gender.Diverse => "divers",
        _ => "–",
    };

    public static string Of(StudentTrack t) => t switch
    {
        StudentTrack.SE => "SE",
        StudentTrack.Support => "Förderbedarf",
        _ => "Regel",
    };

    public static string Date(DateOnly? d) => d?.ToString("dd.MM.yyyy") ?? "–";

    public static readonly Gender[] Genders = [Gender.Unknown, Gender.Female, Gender.Male, Gender.Diverse];
    public static readonly StudentTrack[] Tracks = [StudentTrack.Regular, StudentTrack.SE, StudentTrack.Support];
}
