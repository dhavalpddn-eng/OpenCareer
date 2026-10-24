using OpenCareer.Application.Careers;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class LogbookCivilianTrainingCompletionEvidenceSourceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid CareerId =
        Guid.Parse("61000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task ValidCommittedTrainingEntryProducesCompletedEvidence()
    {
        LogbookEntry entry = Entry();
        var source = Source(entry);
        CivilianTrainingEvidenceIdentity identity =
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId);

        CivilianTrainingCompletionEvidence? evidence =
            await source.ReadAsync(identity);

        Assert.NotNull(evidence);
        Assert.Equal(identity, evidence.EvidenceIdentity);
        Assert.Equal(CareerId, evidence.CareerId);
        Assert.Equal("student-operation-001", evidence.OperationIdentity);
        Assert.Equal("license.student", evidence.TargetQualification.Value);
        Assert.Equal(CivilianTrainingCompletionStatus.Completed, evidence.Status);
        Assert.Equal(entry.Debrief.EndedAt, evidence.CompletedAt);
    }

    [Fact]
    public async Task GenericTrainingEntryWithoutMetadataCannotAward()
    {
        LogbookEntry entry = Entry() with
        {
            Debrief = Entry().Debrief with { QualificationTraining = null }
        };

        Assert.Null(await Source(entry).ReadAsync(
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId)));
    }

    [Theory]
    [InlineData(LogbookEntryKind.FreeFlight)]
    [InlineData(LogbookEntryKind.CareerJob)]
    public async Task NonTrainingEntriesAreRejected(LogbookEntryKind kind)
    {
        LogbookEntry entry = Entry();
        entry = entry with { Debrief = entry.Debrief with { EntryKind = kind } };

        Assert.Null(await Source(entry).ReadAsync(
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId)));
    }

    [Theory]
    [InlineData(FlightSafetyOutcome.CompletedNormally, MissionOutcome.Failed)]
    [InlineData(FlightSafetyOutcome.CompletedNormally, MissionOutcome.Cancelled)]
    [InlineData(FlightSafetyOutcome.CompletedNormally, MissionOutcome.Pending)]
    [InlineData(FlightSafetyOutcome.Crashed, MissionOutcome.Succeeded)]
    [InlineData(FlightSafetyOutcome.Interrupted, MissionOutcome.Succeeded)]
    [InlineData(FlightSafetyOutcome.DivertedSafely, MissionOutcome.Succeeded)]
    public async Task UnsuccessfulOrIncompleteTrainingIsRejected(
        FlightSafetyOutcome safety,
        MissionOutcome mission)
    {
        LogbookEntry entry = Entry();
        entry = entry with
        {
            Debrief = entry.Debrief with
            {
                SafetyOutcome = safety,
                MissionOutcome = mission
            }
        };

        Assert.Null(await Source(entry).ReadAsync(
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId)));
    }

    [Fact]
    public async Task EvidenceEntryAndQualificationIdentitiesFailClosed()
    {
        LogbookEntry entry = Entry();
        var wrongEntry = entry with { EntryId = Guid.NewGuid() };
        CivilianTrainingEvidenceIdentity requested =
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId);

        Assert.Null(await Source(wrongEntry).ReadAsync(requested));
        Assert.Null(await Source(entry).ReadAsync(new("training-logbook:not-a-guid")));

        LogbookEntry unknownTarget = entry with
        {
            Debrief = entry.Debrief with
            {
                QualificationTraining = entry.Debrief.QualificationTraining! with
                {
                    TargetQualificationIdentity = "license.unknown"
                }
            }
        };
        Assert.Null(await Source(unknownTarget).ReadAsync(requested));
    }

    [Fact]
    public async Task MalformedMetadataAndFinancialSettlementAreRejected()
    {
        LogbookEntry entry = Entry();
        LogbookEntry malformed = entry with
        {
            Debrief = entry.Debrief with
            {
                QualificationTraining = entry.Debrief.QualificationTraining! with
                {
                    CareerId = Guid.Empty
                }
            }
        };
        LogbookEntry settled = entry with
        {
            Debrief = entry.Debrief with
            {
                Settlement = new(
                    SettlementRecordStatus.Settled,
                    "training-payment",
                    "tx-1",
                    entry.Debrief.EndedAt,
                    1m,
                    0)
            }
        };

        CivilianTrainingEvidenceIdentity identity =
            LogbookCivilianTrainingEvidenceIdentity.Create(entry.EntryId);
        Assert.Null(await Source(malformed).ReadAsync(identity));
        Assert.Null(await Source(settled).ReadAsync(identity));
    }

    [Fact]
    public void EvidenceSourceHasReadOnlyDependencies()
    {
        Type[] dependencies =
            Assert.Single(typeof(LogbookCivilianTrainingCompletionEvidenceSource).GetConstructors())
                .GetParameters()
                .Select(static parameter => parameter.ParameterType)
                .ToArray();

        Assert.Equal([typeof(ILogbookSource), typeof(ICivilianQualificationCatalog)], dependencies);
        Assert.DoesNotContain(typeof(PlayerCareerQualificationCoordinator), dependencies);
    }

    private static LogbookCivilianTrainingCompletionEvidenceSource Source(
        LogbookEntry entry) =>
        new(new FakeLogbookSource(entry), CivilianQualificationCatalog.Default);

    private static LogbookEntry Entry()
    {
        DateTimeOffset end = Start.AddHours(1);
        var time = new FlightTimeLedger(
            TimeSpan.FromHours(1),
            TimeSpan.FromHours(1),
            TimeSpan.FromMinutes(50),
            TimeSpan.FromMinutes(45),
            TimeSpan.FromMinutes(40),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(45),
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.Zero,
            TimeSpan.FromMinutes(10),
            TimeSpan.FromMinutes(5));
        var route = new FlightRouteDebrief(
            "KAAA", "KBBB", "KAAA", "KBBB", null, 80);
        var landing = new LandingDebrief(
            1, end.AddMinutes(-5), LandingOperationType.FullStop, 0,
            -180, 1.05, 60, 3, 0, false,
            EvidenceQuality.Observed);
        var leg = new FlightLegDebrief(
            Guid.NewGuid(), 1, Start, end, route, time,
            [new(Start.AddMinutes(10), 32, -97, 2000)], [1]);
        FlightDebrief debrief = FlightDebriefFactory.Create(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                LogbookEntryKind.Training,
                Start,
                end,
                route,
                new("Training Aircraft"),
                time,
                new(FlightTrackingState.Complete, null, end, 1, 1, 0, 0, 0, false),
                [leg],
                new(null, null, null, EvidenceQuality.Unavailable),
                new(null, null, null, null, EvidenceQuality.Unavailable),
                new(false, false, false, false, false),
                FlightSafetyOutcome.CompletedNormally,
                MissionOutcome.Succeeded,
                [landing],
                [],
                FlightSettlementRecord.NotApplicable,
                new(
                    CareerId,
                    "student-operation-001",
                    "license.student",
                    "Committed civilian training operation.")));

        return LogbookEntry.Commit(
            Guid.Parse("62000000-0000-0000-0000-000000000001"),
            debrief,
            end.AddMinutes(1),
            LogbookCommitKind.ManualPilotLog);
    }

    private sealed class FakeLogbookSource(LogbookEntry entry)
        : ILogbookSource
    {
        public Task<IReadOnlyList<LogbookEntry>> QueryAsync(
            LogbookQuery query,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Evidence lookup must use immutable entry identity.");

        public Task<LogbookEntry?> GetAsync(
            Guid entryId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<LogbookEntry?>(entry);
        }
    }
}
