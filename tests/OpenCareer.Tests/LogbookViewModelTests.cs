using OpenCareer.App.ViewModels;
using OpenCareer.Application.Logbook;
using OpenCareer.Domain.Flights;
using OpenCareer.Domain.Logbook;

namespace OpenCareer.Tests;

public sealed class LogbookViewModelTests
{
    private static readonly DateTimeOffset StartedAt =
        new(2026, 10, 26, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task RefreshFailureClearsStaleProjectionAndRetryRestoresCommittedHistory()
    {
        LogbookEntry entry = Entry();
        var source = new StubLogbookSource([entry]);
        var viewModel = new LogbookViewModel(source);
        await viewModel.RefreshAsync();

        viewModel.ReportRefreshFailure(
            new IOException("C:\\Users\\pilot\\AppData\\Local\\sensitive-logbook.db"));

        Assert.Empty(viewModel.Entries);
        Assert.Null(viewModel.SelectedEntry);
        Assert.Equal("No flight selected", viewModel.SelectedTitle);
        Assert.Equal("0:00", viewModel.TotalFlightTimeText);
        Assert.DoesNotContain("sensitive-logbook", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("Reopen Logbook", viewModel.StatusText, StringComparison.Ordinal);
        Assert.Contains("diagnostic bundle", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);

        await viewModel.RefreshAsync();

        Assert.Equal(entry.EntryId, Assert.Single(viewModel.Entries).EntryId);
        Assert.NotNull(viewModel.SelectedEntry);
        Assert.Equal("45:00", viewModel.TotalFlightTimeText);
        Assert.Equal("1 committed flight.", viewModel.StatusText);
        Assert.Equal(2, source.QueryCount);
    }

    private static LogbookEntry Entry()
    {
        DateTimeOffset endedAt = StartedAt.AddHours(1);
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
            "KJFK",
            "KISP",
            "KJFK",
            "KISP",
            null,
            40);
        var landing = new LandingDebrief(
            1,
            endedAt.AddMinutes(-5),
            LandingOperationType.FullStop,
            0,
            -180,
            1.05,
            60,
            3,
            0,
            false,
            EvidenceQuality.Observed);
        var leg = new FlightLegDebrief(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            1,
            StartedAt,
            endedAt,
            route,
            time,
            [new(StartedAt.AddMinutes(10), 40.64, -73.78, 2000)],
            [1]);
        FlightDebrief debrief = FlightDebriefFactory.Create(
            new(
                Guid.Parse("71000000-0000-0000-0000-000000000002"),
                Guid.Parse("71000000-0000-0000-0000-000000000003"),
                null,
                LogbookEntryKind.FreeFlight,
                StartedAt,
                endedAt,
                route,
                new("Test C172"),
                time,
                new(FlightTrackingState.Complete, null, endedAt, 1, 1, 0, 0, 0, false),
                [leg],
                new(null, null, null, EvidenceQuality.Unavailable),
                new(null, null, null, null, EvidenceQuality.Unavailable),
                new(false, false, false, false, false),
                FlightSafetyOutcome.CompletedNormally,
                MissionOutcome.NotApplicable,
                [landing],
                [],
                FlightSettlementRecord.NotApplicable));

        return LogbookEntry.Commit(
            Guid.Parse("71000000-0000-0000-0000-000000000004"),
            debrief,
            endedAt.AddMinutes(1),
            LogbookCommitKind.ManualPilotLog);
    }

    private sealed class StubLogbookSource(IReadOnlyList<LogbookEntry> entries)
        : ILogbookSource
    {
        public int QueryCount { get; private set; }

        public Task<IReadOnlyList<LogbookEntry>> QueryAsync(
            LogbookQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            QueryCount++;
            return Task.FromResult(entries);
        }

        public Task<LogbookEntry?> GetAsync(
            Guid entryId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                entries.FirstOrDefault(entry => entry.EntryId == entryId));
        }
    }
}
