using OpenCareer.Application.Flights;
using OpenCareer.Application.Logbook;
using OpenCareer.Application.Planning;
using OpenCareer.Application.Simulator;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Application.Careers;

public sealed record StudentTrainingOperationStartResult(
    string OperationIdentity,
    FlightSession FlightSession,
    string AircraftDisplayName);

public sealed record StudentTrainingOperationCompletionResult(
    LogbookAppendResult Logbook,
    CivilianQualificationAwardResult Award);

public enum StudentTrainingOperationReadinessState
{
    NoCareer = 0,
    Ready = 1,
    Active = 2,
    CompletedAwaitingFinalization = 3,
    AlreadyEarned = 4,
    OtherFlightActive = 5,
    AircraftIdentityUnavailable = 6,
    AircraftNotEligible = 7,
    AuthorityUnavailable = 8
}

public sealed record StudentTrainingOperationReadiness(
    StudentTrainingOperationReadinessState State,
    string Detail,
    Guid? OperationId = null,
    string? AirportIcao = null,
    string? AircraftDisplayName = null)
{
    public bool CanStart =>
        State == StudentTrainingOperationReadinessState.Ready;
}

public interface IStudentTrainingOperationReadinessSource
{
    Task<StudentTrainingOperationReadiness> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface IStudentTrainingOperationStartAction
{
    Task<StudentTrainingOperationStartResult> StartAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns the first production civilian training operation. Starting the operation
/// creates a persisted, local-circuit FlightSession. Completion remains derived
/// from that session's authoritative lifecycle and landing evidence.
/// </summary>
public sealed class StudentTrainingOperationService
    : IStudentTrainingOperationReadinessSource,
      IStudentTrainingOperationStartAction
{
    public const string SourceProvider = "OpenCareer.CivilianTraining.Student";

    private const string SourceReferencePrefix = "v1:";
    private const string OperationIdentityPrefix = "student-training:";
    private const string TargetIdentityValue = "license.student";
    private const string EvidenceDetail =
        "Verified Student training FlightSession with takeoff, full-stop landing, parking and shutdown evidence.";

    private static readonly CareerQualificationIdentity TargetIdentity =
        new(TargetIdentityValue, CareerQualificationKind.License);

    private readonly PlayerCareerRuntimeState _career;
    private readonly ICivilianQualificationCatalog _catalog;
    private readonly ICurrentLoadedAircraftIdentitySource _loadedAircraft;
    private readonly IAircraftRegistrySource _aircraftRegistry;
    private readonly FlightSessionPersistenceService _persistence;
    private readonly FlightSessionCoordinator _sessions;
    private readonly ManualFlightPostflightCoordinator _postflight;
    private readonly ILogbookIdempotencySource _logbookLookup;
    private readonly CivilianQualificationAwardService _awards;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StudentTrainingOperationService(
        PlayerCareerRuntimeState career,
        ICivilianQualificationCatalog catalog,
        ICurrentLoadedAircraftIdentitySource loadedAircraft,
        IAircraftRegistrySource aircraftRegistry,
        FlightSessionPersistenceService persistence,
        FlightSessionCoordinator sessions,
        ManualFlightPostflightCoordinator postflight,
        ILogbookIdempotencySource logbookLookup,
        CivilianQualificationAwardService awards,
        TimeProvider clock)
    {
        _career = career ?? throw new ArgumentNullException(nameof(career));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _loadedAircraft = loadedAircraft ?? throw new ArgumentNullException(nameof(loadedAircraft));
        _aircraftRegistry = aircraftRegistry ?? throw new ArgumentNullException(nameof(aircraftRegistry));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _postflight = postflight ?? throw new ArgumentNullException(nameof(postflight));
        _logbookLookup = logbookLookup ?? throw new ArgumentNullException(nameof(logbookLookup));
        _awards = awards ?? throw new ArgumentNullException(nameof(awards));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<StudentTrainingOperationReadiness> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        PlayerCareerProfileStoreRecord? profile = await _career
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return new(
                StudentTrainingOperationReadinessState.NoCareer,
                "Complete career onboarding before starting Student training.");
        }

        if (!_catalog.TryGet(TargetIdentity, out CivilianQualificationDefinition? definition)
            || definition is null
            || definition.License != PilotLicenseLevel.Student)
        {
            return new(
                StudentTrainingOperationReadinessState.AuthorityUnavailable,
                "The authoritative Student training definition is unavailable.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        if (definition.IsEarned(profile.Profile.Qualifications))
        {
            return new(
                StudentTrainingOperationReadinessState.AlreadyEarned,
                "The Student pilot qualification is already earned.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        if (!_catalog.ArePrerequisitesMet(
                definition,
                profile.Profile.Qualifications))
        {
            return new(
                StudentTrainingOperationReadinessState.AuthorityUnavailable,
                "Student training prerequisites are not met.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        FlightSession? current = _sessions.Current;
        if (current is not null)
        {
            if (TryGetOperationAuthority(
                    current,
                    out Guid plannedCareerId,
                    out Guid operationId)
                && plannedCareerId == profile.Profile.CareerId)
            {
                FlightSessionPlan currentPlan = current.Plan!;
                string? activeAircraftDisplayName = null;
                string? expectedAircraftId =
                    currentPlan.ExpectedCanonicalAircraftId;
                if (!string.IsNullOrWhiteSpace(expectedAircraftId))
                {
                    AircraftRegistryResolution? activeAircraft =
                        await _aircraftRegistry
                            .FindAircraftAsync(
                                expectedAircraftId.Trim(),
                                cancellationToken)
                            .ConfigureAwait(false);
                    IsEligibleTrainingAircraft(
                        activeAircraft,
                        expectedAircraftId.Trim(),
                        out activeAircraftDisplayName);
                }

                if (current.Status is FlightSessionStatus.Interrupted
                    or FlightSessionStatus.Cancelled)
                {
                    return new(
                        StudentTrainingOperationReadinessState.OtherFlightActive,
                        "Student training ended without qualifying completion; clear the terminal session before retrying.",
                        operationId,
                        currentPlan.PlannedOrigin,
                        activeAircraftDisplayName);
                }

                return new(
                    current.Status == FlightSessionStatus.Completed
                        ? StudentTrainingOperationReadinessState.CompletedAwaitingFinalization
                        : StudentTrainingOperationReadinessState.Active,
                    current.Status == FlightSessionStatus.Completed
                        ? "The completed Student training flight is awaiting authoritative finalization."
                        : "Student training is active. Complete a takeoff, full-stop landing, parking and shutdown local circuit.",
                    operationId,
                    currentPlan.PlannedOrigin,
                    activeAircraftDisplayName);
            }

            return new(
                StudentTrainingOperationReadinessState.OtherFlightActive,
                "Finish and clear the current flight before starting Student training.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        CurrentLoadedAircraftIdentitySnapshot loaded = _loadedAircraft.Current;
        try
        {
            loaded.Validate();
        }
        catch (ArgumentException)
        {
            return new(
                StudentTrainingOperationReadinessState.AircraftIdentityUnavailable,
                "Authoritative loaded-aircraft identity is unavailable for Student training.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        if (loaded.Status != CurrentLoadedAircraftIdentityStatus.Identified
            || string.IsNullOrWhiteSpace(loaded.CanonicalAircraftId))
        {
            return new(
                StudentTrainingOperationReadinessState.AircraftIdentityUnavailable,
                "Load an aircraft so OpenCareer can verify its Student training capability.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        string canonicalAircraftId = loaded.CanonicalAircraftId.Trim();
        AircraftRegistryResolution? resolution = await _aircraftRegistry
            .FindAircraftAsync(canonicalAircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (!IsEligibleTrainingAircraft(
                resolution,
                canonicalAircraftId,
                out string? displayName))
        {
            return new(
                StudentTrainingOperationReadinessState.AircraftNotEligible,
                "The loaded aircraft is not authoritatively verified as installed and training-capable.",
                AirportIcao: profile.Profile.Location.CurrentAirportIcao);
        }

        return new(
            StudentTrainingOperationReadinessState.Ready,
            "Ready to start a no-settlement Student local circuit. Qualification remains evidence-gated.",
            AirportIcao: profile.Profile.Location.CurrentAirportIcao,
            AircraftDisplayName: displayName);
    }

    public async Task<StudentTrainingOperationStartResult> StartAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            StudentTrainingOperationReadiness readiness = await ReadAsync(
                    cancellationToken)
                .ConfigureAwait(false);
            if (!readiness.CanStart)
            {
                throw new InvalidOperationException(readiness.Detail);
            }

            PlayerCareerProfileStoreRecord profile =
                await RequireCareerAsync(cancellationToken).ConfigureAwait(false);

            CivilianQualificationDefinition definition =
                RequireAvailableStudentQualification(profile.Profile.Qualifications);

            if (definition.IsEarned(profile.Profile.Qualifications))
            {
                throw new InvalidOperationException(
                    "The Student qualification is already earned.");
            }

            if (_sessions.Current is not null)
            {
                throw new InvalidOperationException(
                    "Finish and clear the current FlightSession before starting Student training.");
            }

            ResolvedTrainingAircraft aircraft =
                await ResolveTrainingAircraftAsync(
                    expectedCanonicalAircraftId: null,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

            Guid operationId = Guid.NewGuid();
            DateTimeOffset startedAt = Latest(
                _clock.GetUtcNow(),
                profile.SavedAt,
                profile.Profile.Location.UpdatedAt);
            string airport = profile.Profile.Location.CurrentAirportIcao;

            var plan = new FlightSessionPlan(
                PlannedOrigin: airport,
                PlannedDestination: airport,
                SourceProvider: SourceProvider,
                SourceReference: CreateSourceReference(
                    profile.Profile.CareerId,
                    operationId),
                ExpectedCanonicalAircraftId: aircraft.CanonicalAircraftId);

            FlightSession session = await _persistence
                .StartAsync(
                    startedAt,
                    contractId: null,
                    sessionId: operationId,
                    plan: plan,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            return new(
                CreateOperationIdentity(operationId),
                session,
                aircraft.DisplayName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<StudentTrainingOperationCompletionResult> CompleteAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Training operation ID is required.", nameof(operationId));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            PlayerCareerProfileStoreRecord profile =
                await RequireCareerAsync(cancellationToken).ConfigureAwait(false);
            string operationIdentity = CreateOperationIdentity(operationId);

            LogbookEntry? existing = await _logbookLookup
                .FindByIdempotencyKeyAsync(
                    ManualIdempotencyKey(operationId),
                    cancellationToken)
                .ConfigureAwait(false);

            LogbookAppendResult logbook;
            if (existing is not null && _sessions.Current?.SessionId != operationId)
            {
                logbook = new(
                    LogbookAppendDisposition.AlreadyExists,
                    ValidateCommittedEntry(
                        existing,
                        profile.Profile.CareerId,
                        operationId));
            }
            else
            {
                FlightSession session = RequireCompletedSession(
                    profile.Profile.CareerId,
                    operationId);
                ResolvedTrainingAircraft aircraft =
                    await ResolveTrainingAircraftAsync(
                        session.Plan!.ExpectedCanonicalAircraftId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                DateTimeOffset completedAt =
                    session.Milestones.CompletedAt
                    ?? throw new InvalidOperationException(
                        "Student training requires an authoritative FlightSession completion timestamp.");
                DateTimeOffset committedAt = Latest(
                    _clock.GetUtcNow(),
                    completedAt,
                    profile.SavedAt);

                var metadata = new CivilianQualificationTrainingMetadata(
                    profile.Profile.CareerId,
                    operationIdentity,
                    TargetIdentityValue,
                    EvidenceDetail);
                var context = new FlightSessionDebriefContext(
                    LogbookEntryKind.Training,
                    new AircraftDebrief(aircraft.DisplayName),
                    ActualDeparture: null,
                    ActualArrival: null,
                    DiversionLocation: null,
                    Payload: new PayloadDebrief(
                        null,
                        null,
                        null,
                        null,
                        EvidenceQuality.Unavailable),
                    SafetyOutcome: FlightSafetyOutcome.CompletedNormally,
                    MissionOutcome: MissionOutcome.Succeeded,
                    Settlement: FlightSettlementRecord.NotApplicable,
                    QualificationTraining: metadata);

                ManualFlightPostflightLogResult result = await _postflight
                    .LogAsync(
                        session.SessionId,
                        new(context, committedAt, committedAt),
                        cancellationToken)
                    .ConfigureAwait(false);
                logbook = new(
                    result.Logbook.Disposition,
                    ValidateCommittedEntry(
                        result.Logbook.Entry,
                        profile.Profile.CareerId,
                        operationId));
            }

            CivilianTrainingEvidenceIdentity evidenceIdentity =
                LogbookCivilianTrainingEvidenceIdentity.Create(
                    logbook.Entry.EntryId);
            CivilianQualificationAwardResult award = await _awards
                .AwardAsync(evidenceIdentity, cancellationToken)
                .ConfigureAwait(false);

            return new(logbook, award);
        }
        finally
        {
            _gate.Release();
        }
    }

    public static string CreateOperationIdentity(Guid operationId)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("Training operation ID is required.", nameof(operationId));

        return $"{OperationIdentityPrefix}{operationId:N}";
    }

    public static bool TryGetOperationAuthority(
        FlightSession? session,
        out Guid careerId,
        out Guid operationId)
    {
        careerId = Guid.Empty;
        operationId = Guid.Empty;

        return session is not null
            && session.ContractId is null
            && session.Plan is not null
            && string.Equals(
                session.Plan.SourceProvider,
                SourceProvider,
                StringComparison.Ordinal)
            && TryParseSourceReference(
                session.Plan.SourceReference,
                out careerId,
                out operationId)
            && operationId == session.SessionId;
    }

    private async Task<PlayerCareerProfileStoreRecord> RequireCareerAsync(
        CancellationToken cancellationToken) =>
        await _career
            .InitializeAsync(cancellationToken)
            .ConfigureAwait(false)
        ?? throw new InvalidOperationException(
            "Complete career onboarding before starting or completing Student training.");

    private CivilianQualificationDefinition RequireAvailableStudentQualification(
        PilotQualificationState qualifications)
    {
        if (!_catalog.TryGet(TargetIdentity, out CivilianQualificationDefinition? definition)
            || definition is null
            || definition.License != PilotLicenseLevel.Student)
        {
            throw new InvalidOperationException(
                "The civilian qualification catalog does not define Student training.");
        }

        if (!_catalog.ArePrerequisitesMet(definition, qualifications))
        {
            throw new InvalidOperationException(
                "Student training prerequisites are not met.");
        }

        return definition;
    }

    private FlightSession RequireCompletedSession(
        Guid careerId,
        Guid operationId)
    {
        FlightSession session = _sessions.Current
            ?? throw new InvalidOperationException(
                "No Student training FlightSession is available for completion.");

        if (session.SessionId != operationId
            || session.ContractId is not null
            || session.Plan is null
            || !string.Equals(
                session.Plan.SourceProvider,
                SourceProvider,
                StringComparison.Ordinal)
            || !TryParseSourceReference(
                session.Plan.SourceReference,
                out Guid plannedCareerId,
                out Guid plannedOperationId)
            || plannedCareerId != careerId
            || plannedOperationId != operationId
            || !string.Equals(
                session.Plan.PlannedOrigin,
                session.Plan.PlannedDestination,
                StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(session.Plan.PlannedOrigin)
            || string.IsNullOrWhiteSpace(
                session.Plan.ExpectedCanonicalAircraftId))
        {
            throw new InvalidOperationException(
                "The current FlightSession is not the authoritative Student training operation.");
        }

        if (session.Status != FlightSessionStatus.Completed
            || session.OperationState != FlightOperationState.Complete
            || session.Tracking.State != FlightTrackingState.Complete
            || session.Tracking.CrashReported
            || session.Tracking.TakeoffCount < 1
            || !session.EffectiveLandingEpisodes.Any(
                static landing =>
                    landing.Kind == FlightSessionLandingKind.FullStop
                    && landing.CompletedAt is not null))
        {
            throw new InvalidOperationException(
                "Student training requires a successful takeoff, full-stop landing, parking and shutdown FlightSession.");
        }

        session.Plan.Validate();
        session.ValidateLegs();
        foreach (FlightSessionLandingEpisode landing in session.EffectiveLandingEpisodes)
            landing.Validate();

        return session;
    }

    private async Task<ResolvedTrainingAircraft> ResolveTrainingAircraftAsync(
        string? expectedCanonicalAircraftId,
        CancellationToken cancellationToken)
    {
        string canonicalAircraftId;
        if (!string.IsNullOrWhiteSpace(expectedCanonicalAircraftId))
        {
            canonicalAircraftId = expectedCanonicalAircraftId.Trim();
        }
        else
        {
            CurrentLoadedAircraftIdentitySnapshot loaded = _loadedAircraft.Current;
            loaded.Validate();

            if (loaded.Status != CurrentLoadedAircraftIdentityStatus.Identified
                || string.IsNullOrWhiteSpace(loaded.CanonicalAircraftId))
            {
                throw new InvalidOperationException(
                    "Authoritative loaded-aircraft identity is unavailable for Student training.");
            }

            canonicalAircraftId = loaded.CanonicalAircraftId.Trim();
        }

        AircraftRegistryResolution? resolution = await _aircraftRegistry
            .FindAircraftAsync(canonicalAircraftId, cancellationToken)
            .ConfigureAwait(false);

        if (!IsEligibleTrainingAircraft(
                resolution,
                canonicalAircraftId,
                out string? displayName))
        {
            throw new InvalidOperationException(
                "Student training requires an installed aircraft with authoritative training capability and display identity.");
        }

        return new(
            canonicalAircraftId,
            displayName!);
    }

    private static bool IsEligibleTrainingAircraft(
        AircraftRegistryResolution? resolution,
        string canonicalAircraftId,
        out string? displayName)
    {
        displayName = null;

        if (resolution is null
            || resolution.InstallationStatus != AircraftInstallationStatus.Installed
            || !string.Equals(
                resolution.CanonicalAircraftId,
                canonicalAircraftId,
                StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(resolution.CapabilityValues.DisplayName)
            || resolution.CapabilityValues.Capabilities is not { } capabilities
            || (capabilities & AircraftCapability.Training) == 0)
        {
            return false;
        }

        displayName = resolution.CapabilityValues.DisplayName.Trim();
        return true;
    }

    private static LogbookEntry ValidateCommittedEntry(
        LogbookEntry entry,
        Guid careerId,
        Guid operationId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        LogbookEntry.Commit(
            entry.EntryId,
            entry.Debrief,
            entry.CommittedAt,
            entry.CommitKind);

        CivilianQualificationTrainingMetadata? metadata =
            entry.Debrief.QualificationTraining;
        if (entry.CommitKind != LogbookCommitKind.ManualPilotLog
            || entry.Debrief.DebriefId != operationId
            || entry.Debrief.SessionId != operationId
            || entry.Debrief.ContractId is not null
            || entry.Debrief.EntryKind != LogbookEntryKind.Training
            || entry.Debrief.MissionOutcome != MissionOutcome.Succeeded
            || entry.Debrief.SafetyOutcome != FlightSafetyOutcome.CompletedNormally
            || entry.Debrief.Settlement.Status != SettlementRecordStatus.NotApplicable
            || metadata is null
            || metadata.CareerId != careerId
            || !string.Equals(
                metadata.TrainingOperationIdentity,
                CreateOperationIdentity(operationId),
                StringComparison.Ordinal)
            || !string.Equals(
                metadata.TargetQualificationIdentity,
                TargetIdentityValue,
                StringComparison.Ordinal)
            || !string.Equals(
                metadata.SourceDetail,
                EvidenceDetail,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Committed Logbook entry does not match the authoritative Student training operation.");
        }

        return entry;
    }

    private static string CreateSourceReference(
        Guid careerId,
        Guid operationId)
    {
        if (careerId == Guid.Empty || operationId == Guid.Empty)
            throw new ArgumentException("Training source identities are required.");

        return $"{SourceReferencePrefix}{careerId:N}:{operationId:N}";
    }

    private static bool TryParseSourceReference(
        string? sourceReference,
        out Guid careerId,
        out Guid operationId)
    {
        careerId = Guid.Empty;
        operationId = Guid.Empty;

        if (sourceReference is null
            || !sourceReference.StartsWith(
                SourceReferencePrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        string[] parts = sourceReference[SourceReferencePrefix.Length..]
            .Split(':', StringSplitOptions.None);

        return parts.Length == 2
            && parts[0].Length == 32
            && parts[1].Length == 32
            && Guid.TryParseExact(parts[0], "N", out careerId)
            && Guid.TryParseExact(parts[1], "N", out operationId)
            && careerId != Guid.Empty
            && operationId != Guid.Empty
            && string.Equals(
                CreateSourceReference(careerId, operationId),
                sourceReference,
                StringComparison.Ordinal);
    }

    private static string ManualIdempotencyKey(Guid operationId) =>
        $"manual:{operationId:N}";

    private static DateTimeOffset Latest(
        DateTimeOffset first,
        DateTimeOffset second,
        DateTimeOffset third) =>
        new[] { first, second, third }.Max();

    private sealed record ResolvedTrainingAircraft(
        string CanonicalAircraftId,
        string DisplayName);
}
