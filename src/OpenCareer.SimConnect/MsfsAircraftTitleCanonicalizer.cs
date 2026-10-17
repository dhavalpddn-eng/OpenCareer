using OpenCareer.Domain.Aircraft;

namespace OpenCareer.SimConnect;

internal static class MsfsAircraftTitleCanonicalizer
{
    private static readonly IReadOnlyDictionary<string, string> CanonicalTitles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["C172SP Classic Passengers"] =
                AircraftCanonicalIdentity.Cessna172SkyhawkTitle
        };

    public static string Resolve(string aircraftTitle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aircraftTitle);

        string normalized = aircraftTitle.Trim();

        return CanonicalTitles.TryGetValue(normalized, out string? canonical)
            ? canonical
            : normalized;
    }
}
