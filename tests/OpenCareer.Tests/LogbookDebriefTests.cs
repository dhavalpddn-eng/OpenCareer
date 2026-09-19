using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class LogbookDebriefTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FactoryPreservesIndependentTimeDimensionsAndSortsEvidence()
    {
        Guid laterEvent = Guid.Parse("30000000-0000-0000-0000-000000000002");
        Guid earlierEvent = Guid.Parse("30000000-0000-0000-0000-000000000001");

        FlightDebrief debrief = FlightDebriefFactory.Create(
            Draft(
                events:
                [
                    new(
                        laterEvent,
                        Start.AddMinutes(50),
                        "Handling",
                        DebriefEventSeverity.Advisory,
                        "Late event",
                        EvidenceQuality.DerivedHighConfidence),
                    new(
                        earlierEvent,
                        Start.AddMinutes(20),
                        "Safety",
                        DebriefEventSeverity.Information,
                        "Earlier event",
                        EvidenceQuality.Observed)
                ]));

        Assert.Equal(TimeSpan.FromMinutes(70), debrief.Time.MovementFlightTime);
        Assert.Equal(TimeSpan.FromMinutes(55), debrief.Time.AirborneTime);
        Assert.Equal(TimeSpan.FromMinutes(60), debrief.Time.CareerCreditTime);
        Assert.Equal(earlierEvent, debrief.Events[0].EventId);
        Assert.Equal(laterEvent, debrief.Events[1].EventId);
    }

    [Fact]
    public void UnknownLandingMetricsRemainUnknown()
    {
        FlightDebrief debrief = FlightDebriefFactory.Create(
            Draft(
                landings:
                [
                    new(
                        1,
                        Start.AddMinutes(70),
                        LandingOperationType.FullStop,
                        0,
                        null,
                        null,
                        null,
                        null,
                        null,
                        null,
                        EvidenceQuality.Unavailable)
                ]));

        LandingDebrief landing = Assert.Single(debrief.Landings);
        Assert.Null(landing.VerticalSpeedFeetPerMinute);
        Assert.Null(landing.TouchdownG);
        Assert.Null(landing.HardLanding);
        Assert.Equal(EvidenceQuality.Unavailable, landing.EvidenceQuality);
    }

    [Fact]
    public void DuplicateLandingEpisodesAreRejected()
    {
        LandingDebrief landingA = Landing(1, Start.AddMinutes(60));
        LandingDebrief landingB = Landing(1, Start.AddMinutes(61));

        Assert.Throws<InvalidOperationException>(
            () => FlightDebriefFactory.Create(
                Draft(landings: [landingA, landingB])));
    }

    [Fact]
    public void CareerLogbookCommitRequiresFinalAuthoritativeSettlement()
    {
        FlightDebrief pending = FlightDebriefFactory.Create(
            Draft(
                settlement: FlightSettlementRecord.Pending("settle-contract-1")));

        Assert.Throws<InvalidOperationException>(
            () => LogbookEntry.Commit(
                Guid.NewGuid(),
                pending,
                Start.AddHours(2),
                LogbookCommitKind.AutomaticCareerSettlement));
    }

    [Fact]
    public void AutomaticCareerCommitFreezesSettlementReference()
    {
        var settlement = new FlightSettlementRecord(
            SettlementRecordStatus.Settled,
            "settle-contract-1",
            "txn-1",
            Start.AddMinutes(95),
            1_250m,
            2.5);

        FlightDebrief debrief = FlightDebriefFactory.Create(
            Draft(
                missionOutcome: MissionOutcome.Succeeded,
                settlement: settlement));

        LogbookEntry entry = LogbookEntry.Commit(
            Guid.NewGuid(),
            debrief,
            Start.AddMinutes(96),
            LogbookCommitKind.AutomaticCareerSettlement);

        Assert.Equal("txn-1", entry.Debrief.Settlement.TransactionId);
        Assert.Equal(1_250m, entry.Debrief.Settlement.CashDelta);
        Assert.Equal(2.5, entry.Debrief.Settlement.ReputationDelta);
    }

    [Fact]
    public void FreeFlightCanBeManuallyCommittedWithoutSettlement()
    {
        FlightDebrief debrief = FlightDebriefFactory.Create(
            Draft(
                contractId: null,
                entryKind: LogbookEntryKind.FreeFlight,
                missionOutcome: MissionOutcome.NotApplicable,
                settlement: FlightSettlementRecord.NotApplicable));

        LogbookEntry entry = LogbookEntry.Commit(
            Guid.NewGuid(),
            debrief,
            Start.AddMinutes(95),
            LogbookCommitKind.ManualPilotLog);

        Assert.Equal(LogbookCommitKind.ManualPilotLog, entry.CommitKind);
        Assert.Equal(SettlementRecordStatus.NotApplicable, entry.Debrief.Settlement.Status);
    }

    [Fact]
    public void ContractFlightCannotUseManualLogCommitPath()
    {
        FlightDebrief debrief = FlightDebriefFactory.Create(
            Draft(
                missionOutcome: MissionOutcome.Failed,
                settlement: new(
                    SettlementRecordStatus.Settled,
                    "settle-contract-1",
                    "txn-1",
                    Start.AddMinutes(95),
                    0,
                    -2)));

        Assert.Throws<InvalidOperationException>(
            () => LogbookEntry.Commit(
                Guid.NewGuid(),
                debrief,
                Start.AddMinutes(96),
                LogbookCommitKind.ManualPilotLog));
    }

    [Fact]
    public void DebriefEventOutsideFlightIntervalIsRejected()
    {
        FlightDebriefEvent invalid = new(
            Guid.NewGuid(),
            Start.AddHours(3),
            "Safety",
            DebriefEventSeverity.Warning,
            "Outside interval",
            EvidenceQuality.Observed);

        Assert.Throws<InvalidOperationException>(
            () => FlightDebriefFactory.Create(
                Draft(events: [invalid])));
    }

    private static FlightDebriefDraft Draft(
        Guid? contractId = null,
        LogbookEntryKind entryKind = LogbookEntryKind.CareerJob,
        MissionOutcome missionOutcome = MissionOutcome.Pending,
        IReadOnlyList<LandingDebrief>? landings = null,
        IReadOnlyList<FlightDebriefEvent>? events = null,
        FlightSettlementRecord? settlement = null)
    {
        contractId ??= entryKind == LogbookEntryKind.CareerJob
            ? Guid.Parse("10000000-0000-0000-0000-000000000001")
            : null;

        return new(
            Guid.Parse("10000000-0000-0000-0000-000000000010"),
            Guid.Parse("10000000-0000-0000-0000-000000000020"),
            contractId,
            entryKind,
            Start,
            Start.AddMinutes(90),
            new("KAAA", "KBBB", "KAAA", "KBBB", null, 185),
            new("Cessna 172", "C172", "N123OC"),
            new(
                TimeSpan.FromMinutes(90),
                TimeSpan.FromMinutes(90),
                TimeSpan.FromMinutes(80),
                TimeSpan.FromMinutes(70),
                TimeSpan.FromMinutes(55),
                TimeSpan.FromMinutes(8),
                TimeSpan.FromMinutes(7),
                TimeSpan.FromMinutes(60),
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.Zero,
                TimeSpan.FromMinutes(20),
                TimeSpan.FromMinutes(12)),
            new(
                FlightTrackingState.Complete,
                null,
                Start.AddMinutes(90),
                1,
                1,
                0,
                0,
                0,
                false),
            FlightSafetyOutcome.CompletedNormally,
            missionOutcome,
            landings ?? [Landing(1, Start.AddMinutes(70))],
            events ?? Array.Empty<FlightDebriefEvent>(),
            settlement ?? FlightSettlementRecord.Pending("settle-contract-1"));
    }

    private static LandingDebrief Landing(
        int episode,
        DateTimeOffset timestamp) =>
        new(
            episode,
            timestamp,
            LandingOperationType.FullStop,
            0,
            -210,
            1.12,
            62,
            4,
            1,
            false,
            EvidenceQuality.DerivedHighConfidence);
}
