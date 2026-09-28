using System.Collections.Concurrent;
using System.Globalization;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Airports;
using OpenCareer.SimConnect.Native;

namespace OpenCareer.SimConnect;

public sealed class SimConnectAirportDataObservationSource(
    SimConnectConnection connection,
    TimeProvider? clock = null)
    : IAirportDataObservationSource
{
    private const double FeetPerMeter = 3.280839895013123;
    internal static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, AirportLookupState> _lookups =
        new(StringComparer.OrdinalIgnoreCase);

    public string SourceId => "msfs-simconnect-facility";
    public AirportDataAuthority Authority => AirportDataAuthority.LocalSimulator;

    public async Task<AirportDataObservation?> FindAirportObservationAsync(
        string icao,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icao);

        string normalizedIcao = icao.Trim().ToUpperInvariant();
        cancellationToken.ThrowIfCancellationRequested();

        if (connection.Current.State != SimulatorConnectionState.Connected)
            return null;

        AirportLookupState state = _lookups.GetOrAdd(
            normalizedIcao,
            static _ => new());
        AirportLookupOperation operation;

        lock (state.Gate)
        {
            if (state.FailedAtTimestamp is { } failedAt
                && _clock.GetElapsedTime(failedAt) < FailureCooldown)
            {
                return null;
            }

            operation = state.InFlight ?? StartLookup(normalizedIcao, state);
            operation.WaiterCount++;
        }

        try
        {
            return await operation.Task
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            ReleaseWaiter(state, operation);
        }
    }

    private AirportLookupOperation StartLookup(
        string icao,
        AirportLookupState state)
    {
        var operation = new AirportLookupOperation();
        state.InFlight = operation;
        operation.Task = ObserveAirportAsync(icao, state, operation);
        return operation;
    }

    private async Task<AirportDataObservation?> ObserveAirportAsync(
        string icao,
        AirportLookupState state,
        AirportLookupOperation operation)
    {
        AirportDataObservation? observation = null;
        bool completed = false;

        try
        {
            SimConnectAirportFacilitySnapshot? snapshot = await connection
                .RequestAirportFacilityAsync(icao, operation.Cancellation.Token)
                .ConfigureAwait(false);

            if (snapshot is not null)
                observation = MapObservation(snapshot);

            completed = true;
            return observation;
        }
        finally
        {
            lock (state.Gate)
            {
                if (ReferenceEquals(state.InFlight, operation))
                {
                    state.InFlight = null;

                    if (completed)
                    {
                        state.FailedAtTimestamp = observation is null
                            && connection.Current.State == SimulatorConnectionState.Connected
                            ? _clock.GetTimestamp()
                            : null;
                    }
                }
            }

            operation.Cancellation.Dispose();
        }
    }

    private AirportDataObservation? MapObservation(
        SimConnectAirportFacilitySnapshot snapshot)
    {
        RunwayRecord[] runways = snapshot.Runways
            .Select(MapRunway)
            .Where(static runway => runway is not null)
            .Cast<RunwayRecord>()
            .OrderBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (runways
            .GroupBy(static runway => runway.Identifier, StringComparer.OrdinalIgnoreCase)
            .Any(static group => group.Count() > 1))
        {
            return null;
        }

        string name = string.IsNullOrWhiteSpace(snapshot.Name)
            ? snapshot.Icao
            : snapshot.Name;

        var observation = new AirportDataObservation(
            new AirportRecord(
                snapshot.Icao,
                name,
                runways,
                snapshot.LatitudeDegrees,
                snapshot.LongitudeDegrees),
            new AirportDataProvenance(
                SourceId,
                Authority,
                _clock.GetUtcNow()));

        try
        {
            observation.Validate();
        }
        catch (ArgumentException)
        {
            // Invalid optional facility facts cannot outrank valid reference data.
            return null;
        }
        return observation;
    }

    private static void ReleaseWaiter(
        AirportLookupState state,
        AirportLookupOperation operation)
    {
        bool cancel = false;

        lock (state.Gate)
        {
            operation.WaiterCount--;
            if (operation.WaiterCount == 0
                && ReferenceEquals(state.InFlight, operation)
                && !operation.Task.IsCompleted)
            {
                state.InFlight = null;
                cancel = true;
            }
        }

        if (cancel)
        {
            try
            {
                operation.Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The lookup completed between the waiter release and cancellation.
            }
        }
    }

    private sealed class AirportLookupState
    {
        internal object Gate { get; } = new();
        internal long? FailedAtTimestamp { get; set; }
        internal AirportLookupOperation? InFlight { get; set; }
    }

    private sealed class AirportLookupOperation
    {
        internal CancellationTokenSource Cancellation { get; } = new();
        internal Task<AirportDataObservation?> Task { get; set; } = null!;
        internal int WaiterCount { get; set; }
    }

    private static RunwayRecord? MapRunway(SimConnectRunwayFacilityData runway)
    {
        string? primary = FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator);
        string? secondary = FormatRunwayEnd(runway.SecondaryNumber, runway.SecondaryDesignator);
        string? identifier = GetRunwayIdentifier(runway);

        if (identifier is null)
            return null;

        bool isClosed = primary is not null && secondary is not null
            ? runway.PrimaryClosed && runway.SecondaryClosed
            : primary is not null
                ? runway.PrimaryClosed
                : runway.SecondaryClosed;

        IReadOnlyList<RunwayEndRecord>? ends =
            MapRunwayEnds(runway, primary, secondary);

        return new(
            identifier,
            ToPositiveFeet(runway.LengthMeters),
            ToPositiveFeet(runway.WidthMeters),
            MapSurface(runway.Surface),
            isClosed,
            ends);
    }

    private static IReadOnlyList<RunwayEndRecord>? MapRunwayEnds(
        SimConnectRunwayFacilityData runway,
        string? primary,
        string? secondary)
    {
        if (!float.IsFinite(runway.HeadingTrueDegrees))
            return null;

        var ends = new List<RunwayEndRecord>(2);
        double primaryHeading = NormalizeHeading(runway.HeadingTrueDegrees);

        if (primary is not null)
            ends.Add(new(primary, primaryHeading, runway.PrimaryClosed));

        if (secondary is not null)
        {
            ends.Add(new(
                secondary,
                NormalizeHeading(primaryHeading + 180.0),
                runway.SecondaryClosed));
        }

        return ends.Count == 0 ? null : ends;
    }

    private static double NormalizeHeading(double heading)
    {
        double normalized = heading % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double? ToPositiveFeet(float meters)
    {
        if (!float.IsFinite(meters) || meters <= 0)
            return null;

        return meters * FeetPerMeter;
    }

    private static RunwaySurface MapSurface(int surface) =>
        surface switch
        {
            0 => RunwaySurface.Concrete,
            1 or 3 or 5 or 6 or 7 => RunwaySurface.Grass,
            2 or 26 or 27 or 28 or 29 or 30 or 31 => RunwaySurface.Water,
            4 or 15 or 17 or 19 or 23 => RunwaySurface.Asphalt,
            8 or 9 => RunwaySurface.SnowOrIce,
            12 => RunwaySurface.Dirt,
            14 => RunwaySurface.Gravel,
            10 or 11 or 13 or 16 or 18 or 20 or 21 or 22 or 24 or 32 => RunwaySurface.Other,
            254 or 255 => RunwaySurface.Unknown,
            _ => RunwaySurface.Unknown
        };

    internal static string? GetRunwayIdentifier(SimConnectRunwayFacilityData runway)
    {
        string? primary = FormatRunwayEnd(runway.PrimaryNumber, runway.PrimaryDesignator);
        string? secondary = FormatRunwayEnd(runway.SecondaryNumber, runway.SecondaryDesignator);

        if (primary is null && secondary is null)
            return null;

        return primary is not null && secondary is not null
            ? string.Equals(primary, secondary, StringComparison.OrdinalIgnoreCase)
                ? primary
                : $"{primary}/{secondary}"
            : primary ?? secondary;
    }

    internal static string? FormatRunwayEnd(int number, int designator)
    {
        string? numberText = number switch
        {
            >= 1 and <= 36 => number.ToString("00", CultureInfo.InvariantCulture),
            37 => "N",
            38 => "NE",
            39 => "E",
            40 => "SE",
            41 => "S",
            42 => "SW",
            43 => "W",
            44 => "NW",
            _ => null
        };

        if (numberText is null)
            return null;

        string? designatorText = designator switch
        {
            0 => string.Empty,
            1 => "L",
            2 => "R",
            3 => "C",
            4 => "W",
            5 => "A",
            6 => "B",
            _ => null
        };

        return designatorText is null ? null : numberText + designatorText;
    }
}
