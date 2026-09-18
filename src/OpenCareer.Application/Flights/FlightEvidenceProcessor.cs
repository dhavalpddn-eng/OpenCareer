using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed class FlightEvidenceProcessor
{
    private readonly FlightEvidenceProcessorOptions _options;

    private AircraftTelemetrySnapshot? _previous;
    private AircraftTelemetrySnapshot? _lastValidBeforeDisconnect;
    private DateTimeOffset? _lastTimestamp;
    private DateTimeOffset? _stableSince;
    private DateTimeOffset? _movementSince;
    private DateTimeOffset? _takeoffRollSince;
    private DateTimeOffset? _airborneSince;
    private DateTimeOffset? _approachSince;
    private DateTimeOffset? _goAroundSince;
    private DateTimeOffset? _rejectedTakeoffSince;
    private DateTimeOffset? _landingRolloutSince;
    private DateTimeOffset? _parkingSince;
    private DateTimeOffset? _touchdownAt;

    private bool _wasDisconnected;
    private bool _reconnectContinuityPlausible = true;
    private bool _takeoffCandidateActive;
    private bool _airborneEpisodeConfirmed;
    private bool _approachWasConfirmed;
    private bool _leftGroundAfterTouchdown;
    private bool _landingRolloutEmitted;
    private double _highestAglSinceAirborne;
    private int _lastEnginesRunning;

    public FlightEvidenceProcessor(FlightEvidenceProcessorOptions? options = null)
    {
        _options = options ?? new FlightEvidenceProcessorOptions();
        _options.Validate();
    }

    public FlightStateEvidence Process(
        bool connected,
        AircraftTelemetrySnapshot? telemetry,
        DateTimeOffset observedAt)
    {
        if (_lastTimestamp is { } lastTimestamp && observedAt < lastTimestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observedAt),
                "Flight evidence timestamps cannot move backwards.");
        }

        _lastTimestamp = observedAt;

        if (!connected || telemetry is null)
        {
            if (!_wasDisconnected && _previous is { } previous && IsValidLoadedAircraft(previous))
                _lastValidBeforeDisconnect = previous;

            _wasDisconnected = true;
            ResetContinuousStreaks();
            return new FlightStateEvidence(observedAt, Connected: false);
        }

        DateTimeOffset timestamp = telemetry.Timestamp;
        if (_lastTimestamp is { } last && timestamp < last && timestamp != observedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telemetry),
                "Telemetry timestamps cannot move backwards.");
        }

        _lastTimestamp = timestamp;

        bool validLoadedAircraft = IsValidLoadedAircraft(telemetry);
        bool continuousSample =
            validLoadedAircraft
            && IsContinuousFromPrevious(telemetry);

        if (!continuousSample)
            ResetContinuousStreaks();

        if (validLoadedAircraft && continuousSample)
            _stableSince ??= timestamp;
        else if (validLoadedAircraft && _previous is null)
            _stableSince = timestamp;
        else if (!validLoadedAircraft)
            _stableSince = null;

        bool stableTelemetry =
            validLoadedAircraft
            && Held(_stableSince, timestamp, _options.StableTelemetryDuration);

        if (_wasDisconnected && validLoadedAircraft)
        {
            _reconnectContinuityPlausible =
                IsReconnectContinuityPlausible(_lastValidBeforeDisconnect, telemetry);
            _wasDisconnected = false;
        }

        bool usable =
            stableTelemetry
            && !telemetry.Paused
            && !telemetry.SlewActive;

        bool movementCondition =
            usable
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots >= _options.MovementGroundSpeedKnots
            && telemetry.EnginesRunning > 0;
        UpdateStreak(ref _movementSince, movementCondition, timestamp);
        bool movementConfirmed = Held(
            _movementSince,
            timestamp,
            _options.MovementConfirmationDuration);

        bool takeoffRollCondition =
            usable
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots >= _options.TakeoffRollGroundSpeedKnots;
        UpdateStreak(ref _takeoffRollSince, takeoffRollCondition, timestamp);
        bool takeoffCandidate = Held(
            _takeoffRollSince,
            timestamp,
            _options.TakeoffRollConfirmationDuration);

        if (takeoffCandidate)
            _takeoffCandidateActive = true;

        bool airborneCondition =
            usable
            && !telemetry.OnGround
            && telemetry.AltitudeAglFeet >= _options.AirborneMinimumAglFeet
            && telemetry.GroundSpeedKnots >= _options.AirborneGroundSpeedKnots;
        UpdateStreak(ref _airborneSince, airborneCondition, timestamp);
        bool airborneConfirmed = Held(
            _airborneSince,
            timestamp,
            _options.AirborneConfirmationDuration);

        if (_airborneEpisodeConfirmed && usable && !telemetry.OnGround)
        {
            _highestAglSinceAirborne = Math.Max(
                _highestAglSinceAirborne,
                telemetry.AltitudeAglFeet);
        }

        bool approachCondition =
            usable
            && !telemetry.OnGround
            && telemetry.AltitudeAglFeet <= _options.ApproachMaximumAglFeet
            && _highestAglSinceAirborne - telemetry.AltitudeAglFeet
                >= _options.ApproachMinimumDescentFromPeakFeet
            && telemetry.GroundSpeedKnots >= _options.AirborneGroundSpeedKnots
            && telemetry.VerticalSpeedFeetPerMinute
                <= _options.ApproachMaximumVerticalSpeedFpm;
        UpdateStreak(ref _approachSince, approachCondition, timestamp);
        bool approachConfirmed = Held(
            _approachSince,
            timestamp,
            _options.ApproachConfirmationDuration);

        bool touchdownTransition =
            usable
            && telemetry.OnGround
            && _previous is { OnGround: false }
            && _airborneEpisodeConfirmed;
        bool touchdownConfirmed = touchdownTransition;

        bool bounceRecontact = false;
        if (_touchdownAt.HasValue
            && _leftGroundAfterTouchdown
            && usable
            && telemetry.OnGround
            && _previous is { OnGround: false }
            && timestamp - _touchdownAt.Value <= _options.BounceWindow)
        {
            bounceRecontact = true;
            _leftGroundAfterTouchdown = false;
        }

        if (_touchdownAt.HasValue
            && _previous is { OnGround: true }
            && !telemetry.OnGround)
        {
            _leftGroundAfterTouchdown = true;
        }

        bool goAroundCondition =
            usable
            && _approachWasConfirmed
            && !_touchdownAt.HasValue
            && !telemetry.OnGround
            && telemetry.VerticalSpeedFeetPerMinute
                >= _options.GoAroundMinimumVerticalSpeedFpm
            && _previous is not null
            && telemetry.AltitudeAglFeet > _previous.AltitudeAglFeet;
        UpdateStreak(ref _goAroundSince, goAroundCondition, timestamp);
        bool goAroundConfirmed = Held(
            _goAroundSince,
            timestamp,
            _options.GoAroundConfirmationDuration);

        bool rejectedTakeoffCondition =
            usable
            && _takeoffCandidateActive
            && !_airborneEpisodeConfirmed
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots
                <= _options.RejectedTakeoffMaximumGroundSpeedKnots;
        UpdateStreak(
            ref _rejectedTakeoffSince,
            rejectedTakeoffCondition,
            timestamp);
        bool rejectedTakeoffConfirmed = Held(
            _rejectedTakeoffSince,
            timestamp,
            _options.RejectedTakeoffConfirmationDuration);

        bool landingRolloutCondition =
            usable
            && _touchdownAt.HasValue
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots
                <= _options.LandingRolloutMaximumGroundSpeedKnots;
        UpdateStreak(
            ref _landingRolloutSince,
            landingRolloutCondition,
            timestamp);
        bool landingRolloutConfirmed =
            !_landingRolloutEmitted
            && Held(
                _landingRolloutSince,
                timestamp,
                _options.LandingRolloutConfirmationDuration);

        bool parkingCondition =
            stableTelemetry
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots <= _options.StationaryGroundSpeedKnots
            && telemetry.ParkingBrakeSet;
        UpdateStreak(ref _parkingSince, parkingCondition, timestamp);
        bool parkingConfirmed = Held(
            _parkingSince,
            timestamp,
            _options.ParkingConfirmationDuration);

        bool touchAndGoConfirmed =
            _touchdownAt.HasValue
            && timestamp - _touchdownAt.Value <= _options.TouchAndGoWindow
            && !telemetry.OnGround
            && Held(
                _airborneSince,
                timestamp,
                _options.AirborneConfirmationDuration);

        bool engineStartObserved =
            _lastEnginesRunning == 0
            && telemetry.EnginesRunning > 0
            && stableTelemetry;

        if (airborneConfirmed)
        {
            if (!_airborneEpisodeConfirmed)
                _highestAglSinceAirborne = telemetry.AltitudeAglFeet;

            _airborneEpisodeConfirmed = true;
            _takeoffCandidateActive = false;
            _rejectedTakeoffSince = null;
        }

        if (approachConfirmed)
            _approachWasConfirmed = true;

        if (touchdownConfirmed)
        {
            _touchdownAt = timestamp;
            _highestAglSinceAirborne = 0;
            _leftGroundAfterTouchdown = false;
            _landingRolloutEmitted = false;
            _landingRolloutSince = null;
            _airborneEpisodeConfirmed = false;
            _approachWasConfirmed = false;
            _goAroundSince = null;
        }

        if (goAroundConfirmed)
        {
            _approachWasConfirmed = false;
            _highestAglSinceAirborne = telemetry.AltitudeAglFeet;
            _goAroundSince = null;
        }

        if (rejectedTakeoffConfirmed)
        {
            _takeoffCandidateActive = false;
            _takeoffRollSince = null;
            _rejectedTakeoffSince = null;
        }

        if (landingRolloutConfirmed)
            _landingRolloutEmitted = true;

        if (touchAndGoConfirmed)
        {
            _touchdownAt = null;
            _highestAglSinceAirborne = telemetry.AltitudeAglFeet;
            _leftGroundAfterTouchdown = false;
            _landingRolloutSince = null;
            _landingRolloutEmitted = false;
            _airborneEpisodeConfirmed = true;
        }

        _lastEnginesRunning = telemetry.EnginesRunning;
        _previous = telemetry;

        return new FlightStateEvidence(
            timestamp,
            Connected: true,
            StableTelemetry: stableTelemetry,
            ValidLoadedAircraft: validLoadedAircraft,
            ContinuityPlausible: _reconnectContinuityPlausible,
            EngineStartObserved: engineStartObserved,
            SelfPoweredMovementForFlight: movementConfirmed,
            TakeoffCandidate: takeoffCandidate,
            RejectedTakeoffConfirmed: rejectedTakeoffConfirmed,
            AirborneConfirmed: airborneConfirmed,
            ApproachConfirmed: approachConfirmed,
            TouchdownConfirmed: touchdownConfirmed,
            BounceRecontact: bounceRecontact,
            GoAroundConfirmed: goAroundConfirmed,
            TouchAndGoConfirmed: touchAndGoConfirmed,
            LandingRolloutConfirmed: landingRolloutConfirmed,
            ParkingConfirmed: parkingConfirmed);
    }

    private bool IsValidLoadedAircraft(AircraftTelemetrySnapshot telemetry)
    {
        if (telemetry.LatitudeDegrees is < -90 or > 90
            || telemetry.LongitudeDegrees is < -180 or > 180)
        {
            return false;
        }

        bool nearObservedLoadingSentinel =
            Math.Abs(
                telemetry.LatitudeDegrees
                - _options.LoadingSentinelLatitudeDegrees)
                <= _options.LoadingSentinelToleranceDegrees
            && Math.Abs(
                telemetry.LongitudeDegrees
                - _options.LoadingSentinelLongitudeDegrees)
                <= _options.LoadingSentinelToleranceDegrees;

        if (!nearObservedLoadingSentinel)
            return true;

        return telemetry.GroundSpeedKnots > 1
            || telemetry.IndicatedAirspeedKnots > 1
            || telemetry.EnginesRunning > 0
            || telemetry.FuelTotalPounds > 1;
    }

    private bool IsContinuousFromPrevious(AircraftTelemetrySnapshot current)
    {
        if (_previous is null)
            return true;

        TimeSpan delta = current.Timestamp - _previous.Timestamp;
        if (delta < TimeSpan.Zero || delta > _options.MaximumContinuousSampleGap)
            return false;

        if (delta == TimeSpan.Zero)
            return true;

        double distance = GreatCircleDistanceNauticalMiles(
            _previous.LatitudeDegrees,
            _previous.LongitudeDegrees,
            current.LatitudeDegrees,
            current.LongitudeDegrees);

        double speedAllowance =
            Math.Max(
                _previous.GroundSpeedKnots,
                current.GroundSpeedKnots)
            * delta.TotalHours
            + 2;

        return distance <= Math.Max(2, speedAllowance);
    }

    private bool IsReconnectContinuityPlausible(
        AircraftTelemetrySnapshot? previous,
        AircraftTelemetrySnapshot current)
    {
        if (previous is null)
            return true;

        TimeSpan elapsed = current.Timestamp - previous.Timestamp;
        if (elapsed < TimeSpan.Zero)
            return false;

        double allowedDistance =
            _options.ReconnectBaseDistanceNauticalMiles
            + elapsed.TotalHours
                * _options.ReconnectMaximumPlausibleGroundSpeedKnots;

        double distance = GreatCircleDistanceNauticalMiles(
            previous.LatitudeDegrees,
            previous.LongitudeDegrees,
            current.LatitudeDegrees,
            current.LongitudeDegrees);

        return distance <= allowedDistance;
    }

    private static void UpdateStreak(
        ref DateTimeOffset? since,
        bool condition,
        DateTimeOffset timestamp)
    {
        if (!condition)
        {
            since = null;
            return;
        }

        since ??= timestamp;
    }

    private static bool Held(
        DateTimeOffset? since,
        DateTimeOffset timestamp,
        TimeSpan duration) =>
        since.HasValue && timestamp - since.Value >= duration;

    private void ResetContinuousStreaks()
    {
        _stableSince = null;
        _movementSince = null;
        _takeoffRollSince = null;
        _airborneSince = null;
        _approachSince = null;
        _goAroundSince = null;
        _rejectedTakeoffSince = null;
        _landingRolloutSince = null;
        _parkingSince = null;
    }

    private static double GreatCircleDistanceNauticalMiles(
        double latitude1,
        double longitude1,
        double latitude2,
        double longitude2)
    {
        const double EarthRadiusNm = 3_440.065;
        static double Radians(double degrees) => degrees * Math.PI / 180d;

        double lat1 = Radians(latitude1);
        double lat2 = Radians(latitude2);
        double dLat = lat2 - lat1;
        double dLon = Radians(longitude2 - longitude1);

        double a =
            Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(lat1)
            * Math.Cos(lat2)
            * Math.Sin(dLon / 2)
            * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(Math.Max(0, 1 - a)));
        return EarthRadiusNm * c;
    }
}
