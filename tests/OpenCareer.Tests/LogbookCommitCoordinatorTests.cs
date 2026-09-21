using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class LogbookCommitCoordinatorTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RepeatedCareerCommitReturnsOriginalStoredEntry()
    {
        var writer = new FakeWriter();
        var coordinator = new LogbookCommitCoordinator(writer);
        FlightDebrief debrief = CareerDebrief();

        Guid firstEntryId = Guid.NewGuid();
        LogbookAppendResult first = await coordinator.CommitAsync(
            firstEntryId,
            debrief,
            Start.AddHours(2),
            LogbookCommitKind.AutomaticCareerSettlement);

        LogbookAppendResult retry = await coordinator.CommitAsync(
            Guid.NewGuid(),
            debrief,
            Start.AddHours(2).AddMinutes(1),
            LogbookCommitKind.AutomaticCareerSettlement);

        Assert.Equal(LogbookAppendDisposition.Appended, first.Disposition);
        Assert.Equal(LogbookAppendDisposition.AlreadyExists, retry.Disposition);
        Assert.Equal(firstEntryId, retry.Entry.EntryId);
        Assert.Single(writer.StoredEntries);
    }

    [Fact]
    public async Task DifferentManualDebriefsUseDifferentIdempotencyKeys()
    {
        var writer = new FakeWriter();
        var coordinator = new LogbookCommitCoordinator(writer);

        FlightDebrief firstDebrief = FreeFlightDebrief(Guid.NewGuid());
        FlightDebrief secondDebrief = FreeFlightDebrief(Guid.NewGuid());

        await coordinator.CommitAsync(
            Guid.NewGuid(),
            firstDebrief,
            Start.AddHours(2),
            LogbookCommitKind.ManualPilotLog);

        await coordinator.CommitAsync(
            Guid.NewGuid(),
            secondDebrief,
            Start.AddHours(2),
            LogbookCommitKind.ManualPilotLog);

        Assert.Equal(2, writer.StoredEntries.Count);
    }

    [Fact]
    public void CareerIdempotencyKeyComesFromAuthoritativeSettlement()
    {
        FlightDebrief debrief = CareerDebrief();

        string key = LogbookCommitCoordinator.BuildIdempotencyKey(
            debrief,
            LogbookCommitKind.AutomaticCareerSettlement);

        Assert.Equal("career:settlement-contract-1", key);
    }

    private static FlightDebrief CareerDebrief() =>
        CreateDebrief(
            Guid.NewGuid(),
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            LogbookEntryKind.CareerJob,
            MissionOutcome.Succeeded,
            new(
                SettlementRecordStatus.Settled,
                "settlement-contract-1",
                "txn-1",
                Start.AddMinutes(95),
                900,
                1));

    private static FlightDebrief FreeFlightDebrief(Guid debriefId) =>
        CreateDebrief(
            debriefId,
            null,
            LogbookEntryKind.FreeFlight,
            MissionOutcome.NotApplicable,
            FlightSettlementRecord.NotApplicable);

    private static FlightDebrief CreateDebrief(
        Guid debriefId,
        Guid? contractId,
        LogbookEntryKind entryKind,
        MissionOutcome missionOutcome,
        FlightSettlementRecord settlement)
    {
        LandingDebrief landing = new(
            1,
            Start.AddMinutes(70),
            LandingOperationType.FullStop,
            0,
            -180,
            1.08,
            58,
            3,
            0,
            false,
            EvidenceQuality.DerivedHighConfidence);

        FlightTimeLedger ledger = new(
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
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5));

        FlightLegDebrief leg = new(
            Guid.NewGuid(),
            1,
            Start,
            Start.AddMinutes(90),
            new("KAAA", "KBBB", "KAAA", "KBBB", null, 120),
            ledger,
            [
                new(Start.AddMinutes(5), 34, -97, 1200),
                new(Start.AddMinutes(80), 35, -96, 1500)
            ],
            [1]);

        return FlightDebriefFactory.Create(
            new(
                debriefId,
                Guid.NewGuid(),
                contractId,
                entryKind,
                Start,
                Start.AddMinutes(90),
                new("KAAA", "KBBB", "KAAA", "KBBB", null, 120),
                new("Cessna 172"),
                ledger,
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
                [leg],
                new(200, 140, 60, EvidenceQuality.Observed),
                new(null, 100, "Cargo", "Delivered", EvidenceQuality.MissionDeclared),
                new(false, false, false, false, false),
                FlightSafetyOutcome.CompletedNormally,
                missionOutcome,
                [landing],
                Array.Empty<FlightDebriefEvent>(),
                settlement));
    }

    private sealed class FakeWriter : ILogbookWriter
    {
        private readonly Dictionary<string, LogbookEntry> _entries =
            new(StringComparer.Ordinal);

        public IReadOnlyCollection<LogbookEntry> StoredEntries =>
            _entries.Values;

        public Task<LogbookAppendResult> TryAppendAsync(
            LogbookEntry entry,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(idempotencyKey))
                throw new ArgumentException(
                    "Idempotency key is required.",
                    nameof(idempotencyKey));

            if (_entries.TryGetValue(idempotencyKey, out LogbookEntry? existing))
            {
                return Task.FromResult(
                    new LogbookAppendResult(
                        LogbookAppendDisposition.AlreadyExists,
                        existing));
            }

            _entries.Add(idempotencyKey, entry);

            return Task.FromResult(
                new LogbookAppendResult(
                    LogbookAppendDisposition.Appended,
                    entry));
        }
    }
}
