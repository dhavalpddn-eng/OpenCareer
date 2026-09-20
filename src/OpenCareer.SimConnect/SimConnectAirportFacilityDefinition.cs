using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

internal static class SimConnectAirportFacilityDefinition
{
    internal const uint DefinitionId = 0x4F430020;
    internal const uint FirstRequestId = 0x4F430100;
    internal static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(10);

    internal static readonly string[] Fields =
    [
        "OPEN AIRPORT",
        "NAME64",
        "ICAO",
        "OPEN RUNWAY",
        "LENGTH",
        "WIDTH",
        "SURFACE",
        "PRIMARY_NUMBER",
        "PRIMARY_DESIGNATOR",
        "SECONDARY_NUMBER",
        "SECONDARY_DESIGNATOR",
        "PRIMARY_CLOSED",
        "SECONDARY_CLOSED",
        "CLOSE RUNWAY",
        "CLOSE AIRPORT"
    ];
}

internal sealed record SimConnectAirportFacilitySnapshot(
    string Icao,
    string Name,
    IReadOnlyList<SimConnectRunwayFacilityData> Runways);

internal sealed class SimConnectAirportFacilityQuery(
    string icao,
    CancellationToken cancellationToken)
{
    internal string Icao { get; } = icao;
    internal CancellationToken CancellationToken { get; } = cancellationToken;
    internal TaskCompletionSource<SimConnectAirportFacilitySnapshot?> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class ActiveSimConnectAirportFacilityRequest(
    SimConnectAirportFacilityQuery query,
    uint requestId,
    uint sendId,
    long startedAt)
{
    private SimConnectAirportFacilityData? _airport;
    private uint? _airportUniqueRequestId;
    private uint? _runwayListSize;
    private readonly Dictionary<uint, SimConnectRunwayFacilityData> _runways = [];

    internal SimConnectAirportFacilityQuery Query { get; } = query;
    internal uint RequestId { get; } = requestId;
    internal uint SendId { get; } = sendId;
    internal long StartedAt { get; } = startedAt;

    internal bool Accept(SimConnectMessage message)
    {
        if (message.Kind != SimConnectMessageKind.FacilityData
            || message.RequestId != RequestId
            || message.FacilityDataType is null)
        {
            return false;
        }

        switch (message.FacilityDataType.Value)
        {
            case SimConnectFacilityDataType.Airport:
                if (_airport is not null
                    || message.AirportFacilityData is null
                    || message.IsListItem
                    || message.ParentUniqueRequestId != 0)
                {
                    return false;
                }

                _airport = message.AirportFacilityData;
                _airportUniqueRequestId = message.UniqueRequestId;
                return true;

            case SimConnectFacilityDataType.Runway:
                if (_airportUniqueRequestId is null
                    || message.RunwayFacilityData is null
                    || !message.IsListItem
                    || message.ParentUniqueRequestId != _airportUniqueRequestId.Value
                    || message.ListSize == 0
                    || message.ItemIndex >= message.ListSize
                    || (_runwayListSize.HasValue && _runwayListSize.Value != message.ListSize)
                    || _runways.ContainsKey(message.ItemIndex))
                {
                    return false;
                }

                _runwayListSize ??= message.ListSize;
                _runways.Add(message.ItemIndex, message.RunwayFacilityData);
                return true;

            default:
                return false;
        }
    }

    internal SimConnectAirportFacilitySnapshot? Build()
    {
        if (_airport is null
            || string.IsNullOrWhiteSpace(_airport.Icao)
            || !string.Equals(_airport.Icao, Query.Icao, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_runwayListSize.HasValue && _runways.Count != _runwayListSize.Value)
            return null;

        SimConnectRunwayFacilityData[] runways = _runways
            .OrderBy(static pair => pair.Key)
            .Select(static pair => pair.Value)
            .ToArray();

        return new(
            _airport.Icao.Trim().ToUpperInvariant(),
            _airport.Name.Trim(),
            runways);
    }
}
