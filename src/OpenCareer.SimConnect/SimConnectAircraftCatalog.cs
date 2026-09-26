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
    internal const uint FirstRequestId = 0x4F430010;
    internal const uint RequestId = FirstRequestId;
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
}
