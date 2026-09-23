using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Flights;

public sealed class FlightTelemetryEvidenceProcessor
{
    private readonly FlightEvidenceProcessorOptions _options;

    private AircraftTelemetrySnapshot? _previous;
    private int _stableSampleCount;
    private int _airborneSampleCount;
    private int _groundSampleCount;
    private bool _airborneConfirmedPreviously;
    private bool _takeoffCandidateActive;
    private bool _landingEpisodeActive;
    private bool _bounceAirborneObserved;
    private DateTimeOffset? _touchdownAt;

    public FlightTelemetryEvidenceProcessor(
        FlightEvidenceProcessorOptions? options = null)
    {
        _options =
            options
            ?? new FlightEvidenceProcessorOptions();

        _options.Validate();
    }

    public FlightStateEvidence Process(
        FlightEvidenceObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        bool connected =
            observation.ConnectionState
            == SimulatorConnectionState.Connected;

        AircraftTelemetrySnapshot? telemetry =
            observation.Telemetry;

        if (!connected || telemetry is null)
        {
            ResetTransientEvidence();

            return new FlightStateEvidence(
                Timestamp:
                    telemetry?.Timestamp
                    ?? _previous?.Timestamp
                    ?? DateTimeOffset.UtcNow,
                Connected: false,
                ContinuityPlausible:
                    observation.ContinuityPlausible,
                AuthorizedAirborneStart:
                    observation.AuthorizedAirborneStart,
                AuthorizedRunwayStart:
                    observation.AuthorizedRunwayStart,
                CrashReported:
                    observation.CrashReported);
        }

        ValidateTimestamp(telemetry);

        bool sampleValid =
            HasFiniteCoreTelemetry(telemetry);

        if (sampleValid)
            _stableSampleCount++;
        else
            _stableSampleCount = 0;

        bool stableTelemetry =
            sampleValid
            && _stableSampleCount
                >= _options.StableTelemetrySamples;

        bool operationalSample =
            sampleValid
            && !telemetry.Paused
            && !telemetry.SlewActive;

        bool engineStartObserved =
            operationalSample
            && telemetry.EnginesRunning > 0
            && (_previous is null
                || _previous.EnginesRunning == 0);

        bool selfPoweredMovement =
            operationalSample
            && telemetry.OnGround
            && telemetry.EnginesRunning > 0
            && telemetry.GroundSpeedKnots
                >= _options.TaxiGroundSpeedKnots;

        bool takeoffCandidate =
            operationalSample
            && telemetry.OnGround
            && telemetry.EnginesRunning > 0
            && telemetry.GroundSpeedKnots
                >= _options.TakeoffCandidateGroundSpeedKnots
            && telemetry.IndicatedAirspeedKnots
                >= _options.TakeoffCandidateIndicatedAirspeedKnots;

        if (takeoffCandidate)
            _takeoffCandidateActive = true;

        bool rejectedTakeoff =
            operationalSample
            && _takeoffCandidateActive
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots
                <= _options.RejectedTakeoffGroundSpeedKnots;

        if (rejectedTakeoff)
            _takeoffCandidateActive = false;

        bool airborneSample =
            operationalSample
            && !telemetry.OnGround
            && telemetry.AltitudeAglFeet
                >= _options.AirborneMinimumAglFeet;

        if (airborneSample)
        {
            _airborneSampleCount++;
            _groundSampleCount = 0;
        }
        else
        {
            _airborneSampleCount = 0;

            if (operationalSample
                && telemetry.OnGround)
            {
                _groundSampleCount++;
            }
            else
            {
                _groundSampleCount = 0;
            }
        }

        bool airborneConfirmed =
            _airborneSampleCount
            >= _options.AirborneConfirmationSamples;

        if (airborneConfirmed)
        {
            _airborneConfirmedPreviously = true;
            _takeoffCandidateActive = false;
        }

        if (operationalSample && _landingEpisodeActive && !telemetry.OnGround
            && telemetry.AltitudeAglFeet <= FlightAirframeCalibration.NearGroundCeilingFeet
            && _touchdownAt is { } touchdownAt
            && telemetry.Timestamp - touchdownAt <= FlightAirframeCalibration.TouchdownCorrelationWindow)
            _bounceAirborneObserved = true;

        bool touchdownConfirmed =
            operationalSample
            && stableTelemetry
            && _airborneConfirmedPreviously
            && !_landingEpisodeActive
            && telemetry.OnGround
            && _groundSampleCount
                >= _options.GroundConfirmationSamples;

        if (touchdownConfirmed)
        {
            _landingEpisodeActive = true;
            _airborneConfirmedPreviously = false;
            _touchdownAt = telemetry.Timestamp;
        }

        bool bounceRecontact = operationalSample && stableTelemetry && _landingEpisodeActive
            && _bounceAirborneObserved && telemetry.OnGround
            && _groundSampleCount >= _options.GroundConfirmationSamples;
        if (bounceRecontact)
        {
            _bounceAirborneObserved = false;
            _airborneConfirmedPreviously = false;
            _touchdownAt = telemetry.Timestamp;
        }

        bool landingRolloutConfirmed =
            operationalSample
            && _landingEpisodeActive
            && !_bounceAirborneObserved
            && !touchdownConfirmed
            && !bounceRecontact
            && telemetry.OnGround
            && _groundSampleCount
                >= _options.GroundConfirmationSamples
            && telemetry.GroundSpeedKnots
                <= _options.LandingRolloutMaximumGroundSpeedKnots;

        if (landingRolloutConfirmed)
        {
            _landingEpisodeActive = false;
            _airborneConfirmedPreviously = false;
            _touchdownAt = null;
        }

        bool approachConfirmed =
            operationalSample
            && !telemetry.OnGround
            && telemetry.AltitudeAglFeet
                <= _options.ApproachMaximumAglFeet
            && telemetry.VerticalSpeedFeetPerMinute
                <= _options.ApproachMaximumVerticalSpeedFeetPerMinute;

        bool parkingConfirmed =
            operationalSample
            && telemetry.OnGround
            && telemetry.GroundSpeedKnots
                <= _options.ParkingMaximumGroundSpeedKnots
            && telemetry.ParkingBrakeSet;

        bool operationComplete =
            observation.OperationCompleteConfirmed
            && parkingConfirmed
            && telemetry.EnginesRunning == 0;

        var evidence =
            new FlightStateEvidence(
                telemetry.Timestamp,
                Connected: true,
                StableTelemetry:
                    stableTelemetry,
                ValidLoadedAircraft:
                    stableTelemetry
                    && observation.ValidLoadedAircraft,
                ContinuityPlausible:
                    observation.ContinuityPlausible,
                AuthorizedAirborneStart:
                    observation.AuthorizedAirborneStart,
                AuthorizedRunwayStart:
                    observation.AuthorizedRunwayStart,
                EngineStartObserved:
                    engineStartObserved,
                SelfPoweredMovementForFlight:
                    selfPoweredMovement,
                TakeoffCandidate:
                    takeoffCandidate,
                RejectedTakeoffConfirmed:
                    rejectedTakeoff,
                AirborneConfirmed:
                    airborneConfirmed,
                ApproachConfirmed:
                    approachConfirmed,
                TouchdownConfirmed:
                    touchdownConfirmed,
                BounceRecontact:
                    bounceRecontact,
                LandingRolloutConfirmed:
                    landingRolloutConfirmed,
                ParkingConfirmed:
                    parkingConfirmed,
                OperationCompleteConfirmed:
                    operationComplete,
                CrashReported:
                    observation.CrashReported);

        _previous = telemetry;

        return evidence;
    }

    public void RestoreContext(
        FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        Reset();

        FlightTrackingState state =
            session.Tracking.State
                == FlightTrackingState.Suspended
            && session.Tracking.SuspendedFrom is { } suspendedFrom
                ? suspendedFrom
                : session.Tracking.State;

        _airborneConfirmedPreviously =
            state
                is FlightTrackingState.Airborne
                    or FlightTrackingState.Approach;

        _takeoffCandidateActive =
            state == FlightTrackingState.TakeoffRoll;

        _landingEpisodeActive =
            state == FlightTrackingState.LandingEpisode;
        _touchdownAt = _landingEpisodeActive
            ? session.EffectiveLandingEpisodes.LastOrDefault()?.TouchdownAt
            : null;
    }

    public void Reset()
    {
        _previous = null;
        _stableSampleCount = 0;
        _airborneSampleCount = 0;
        _groundSampleCount = 0;
        _airborneConfirmedPreviously = false;
        _takeoffCandidateActive = false;
        _landingEpisodeActive = false;
        _bounceAirborneObserved = false;
        _touchdownAt = null;
    }

    private void ResetTransientEvidence()
    {
        _stableSampleCount = 0;
        _airborneSampleCount = 0;
        _groundSampleCount = 0;
        _takeoffCandidateActive = false;
        _bounceAirborneObserved = false;
    }

    private void ValidateTimestamp(
        AircraftTelemetrySnapshot telemetry)
    {
        if (_previous is not null
            && telemetry.Timestamp <= _previous.Timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telemetry),
                "Telemetry must advance beyond the previous accepted sample.");
        }
    }

    private static bool HasFiniteCoreTelemetry(
        AircraftTelemetrySnapshot telemetry) =>
        double.IsFinite(telemetry.LatitudeDegrees)
        && double.IsFinite(telemetry.LongitudeDegrees)
        && double.IsFinite(telemetry.AltitudeMslFeet)
        && double.IsFinite(telemetry.AltitudeAglFeet)
        && double.IsFinite(telemetry.IndicatedAirspeedKnots)
        && double.IsFinite(telemetry.GroundSpeedKnots)
        && double.IsFinite(telemetry.VerticalSpeedFeetPerMinute)
        && double.IsFinite(telemetry.HeadingDegrees)
        && double.IsFinite(telemetry.PitchDegrees)
        && double.IsFinite(telemetry.BankDegrees)
        && double.IsFinite(telemetry.NormalAccelerationG)
        && telemetry.EnginesRunning >= 0
        && double.IsFinite(telemetry.FuelTotalPounds)
        && double.IsFinite(telemetry.PayloadPounds)
        && double.IsFinite(telemetry.FlapsPositionPercent);
}
