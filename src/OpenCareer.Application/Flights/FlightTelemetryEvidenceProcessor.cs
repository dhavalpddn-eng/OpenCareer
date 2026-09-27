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
    private bool _landingContactObserved;
    private DateTimeOffset? _bounceAirborneAt;
    private bool _pendingBounceRecontact;

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

        ObserveLandingContact(telemetry, operationalSample && observation.ContinuityPlausible);

        bool touchdownConfirmed =
            operationalSample
            && _airborneConfirmedPreviously
            && telemetry.OnGround
            && _groundSampleCount
                == _options.GroundConfirmationSamples;

        bool wasLandingEpisodeActive = _landingEpisodeActive;
        bool bounceRecontact = touchdownConfirmed && _pendingBounceRecontact;
        if (touchdownConfirmed)
        {
            _landingEpisodeActive = true;
            _pendingBounceRecontact = false;
        }

        bool landingRolloutConfirmed =
            operationalSample
            && wasLandingEpisodeActive
            && !bounceRecontact
            && telemetry.OnGround
            && _groundSampleCount
                >= _options.GroundConfirmationSamples
            && telemetry.GroundSpeedKnots
                <= _options.LandingRolloutMaximumGroundSpeedKnots;

        if (landingRolloutConfirmed)
        {
            _landingEpisodeActive = false;
            ResetLandingContact();
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
                    or FlightTrackingState.Approach
                    or FlightTrackingState.LandingEpisode;

        _takeoffCandidateActive =
            state == FlightTrackingState.TakeoffRoll;

        _landingEpisodeActive =
            state == FlightTrackingState.LandingEpisode;
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
        ResetLandingContact();
    }

    private void ResetTransientEvidence()
    {
        _stableSampleCount = 0;
        _airborneSampleCount = 0;
        _groundSampleCount = 0;
        _takeoffCandidateActive = false;
        ResetLandingContact();
    }

    private void ObserveLandingContact(AircraftTelemetrySnapshot telemetry, bool trustworthy)
    {
        // Contact evidence belongs to this processor; continuity still owns spatial plausibility.
        // No recontact is inferred across a disconnect, pause, slew or missing observation interval.
        if (!trustworthy || _previous is null
            || (telemetry.Timestamp - _previous.Timestamp).TotalSeconds > _options.BounceMaximumAirborneSeconds)
        {
            ResetLandingContact();
            return;
        }

        if (!telemetry.OnGround)
        {
            if (_landingContactObserved && _previous.OnGround)
                _bounceAirborneAt = _previous.Timestamp;

            if (_bounceAirborneAt is { } airborneAt
                && (telemetry.AltitudeAglFeet > _options.BounceMaximumAglFeet
                    || (telemetry.Timestamp - airborneAt).TotalSeconds > _options.BounceMaximumAirborneSeconds))
            {
                ResetLandingContact();
            }
            return;
        }

        if (!_previous.OnGround && _airborneConfirmedPreviously)
        {
            if (_landingContactObserved && _bounceAirborneAt is { } airborneAt
                && (telemetry.Timestamp - airborneAt).TotalSeconds <= _options.BounceMaximumAirborneSeconds)
            {
                _pendingBounceRecontact = true;
            }
            _landingContactObserved = true;
            _bounceAirborneAt = null;
        }
    }

    private void ResetLandingContact()
    {
        _landingContactObserved = false;
        _bounceAirborneAt = null;
        _pendingBounceRecontact = false;
    }

    private void ValidateTimestamp(
        AircraftTelemetrySnapshot telemetry)
    {
        if (_previous is not null
            && telemetry.Timestamp < _previous.Timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(telemetry),
                "Telemetry cannot move backward in time.");
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
