namespace OpenCareer.SimConnect;

internal sealed record SimConnectAircraftCatalogSnapshot(
    bool IsAvailable,
    IReadOnlyList<string> AircraftTitles)
{
    internal static SimConnectAircraftCatalogSnapshot Unavailable { get; } =
        new(false, Array.Empty<string>());
}

internal static class SimConnectAircraftCatalog
{
    internal const uint RequestId = 0x4F430010;
}
