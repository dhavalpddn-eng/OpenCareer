using OpenCareer.Application.Flights;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightSessionCoordinatorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void StartingSecondSessionWhileFirstIsActiveIsRejected()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Start(Epoch);

        Assert.Throws<InvalidOperationException>(
            () =>
                coordinator.Start(
                    Epoch.AddMinutes(1)));
    }

    [Fact]
    public void StartingOverTerminalSessionIsRejectedUntilExplicitlyCleared()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Start(Epoch);

        FlightSession terminal =
            coordinator.Advance(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinator.Start(
                    Epoch.AddSeconds(2)));

        Assert.Contains("cleared", error.Message);
        Assert.Equal(terminal, coordinator.Current);
    }

    [Fact]
    public void CoordinatorPublishesStateChanges()
    {
        var coordinator =
            new FlightSessionCoordinator();

        var observed =
            new List<FlightSession?>();

        coordinator.SessionChanged +=
            (_, args) =>
                observed.Add(args.Session);

        coordinator.Start(Epoch);

        coordinator.Advance(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true,
                    StableTelemetry: true,
                    ValidLoadedAircraft: true,
                    ContinuityPlausible: true)));

        Assert.Equal(
            2,
            observed.Count);

        Assert.Equal(
            FlightOperationState.ReadyForStart,
            observed[^1]?.OperationState);
    }

    [Fact]
    public void TerminalSessionCanBeClearedAndNewSessionStarted()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Start(Epoch);

        coordinator.Advance(
            new FlightSessionAdvance(
                new FlightStateEvidence(
                    Epoch.AddSeconds(1),
                    Connected: true),
                CancelRequested: true));

        coordinator.ClearTerminalSession();

        FlightSession next =
            coordinator.Start(
                Epoch.AddSeconds(2));

        Assert.NotNull(next);
        Assert.Equal(
            FlightSessionStatus.Active,
            next.Status);
    }

    [Fact]
    public void RestoreCreatesApplicationSeamForLaterPersistence()
    {
        var coordinator =
            new FlightSessionCoordinator();

        FlightSession saved =
            FlightSession.Start(
                Epoch,
                sessionId:
                    Guid.Parse(
                        "11111111-1111-1111-1111-111111111111"));

        coordinator.Restore(saved);

        Assert.Equal(
            saved.SessionId,
            coordinator.Current?.SessionId);
    }

    [Fact]
    public void RestorePreservesTerminalSessionUntilExplicitlyCleared()
    {
        var coordinator =
            new FlightSessionCoordinator();

        FlightSession terminal =
            FlightSessionEngine.Advance(
                FlightSession.Start(Epoch),
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        FlightSession replacement =
            FlightSession.Start(
                Epoch.AddSeconds(2));

        coordinator.Restore(terminal);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinator.Restore(replacement));

        Assert.Contains("cleared", error.Message);
        Assert.Equal(terminal, coordinator.Current);

        coordinator.ClearTerminalSession();
        coordinator.Restore(replacement);

        Assert.Equal(replacement, coordinator.Current);
    }

    [Fact]
    public void NewerNonterminalCheckpointCannotResurrectTerminalSession()
    {
        var coordinator =
            new FlightSessionCoordinator();

        FlightSession active =
            coordinator.Start(Epoch);

        FlightSession terminal =
            coordinator.Advance(
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        FlightSession staleActive =
            active with
            {
                UpdatedAt = Epoch.AddSeconds(2)
            };

        var observed =
            new List<FlightSession?>();

        coordinator.SessionChanged +=
            (_, args) =>
                observed.Add(args.Session);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinator.CommitPersisted(staleActive));

        Assert.Contains("terminal", error.Message);
        Assert.Equal(terminal, coordinator.Current);
        Assert.Empty(observed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PersistedCheckpointCannotChangeImmutableSessionAuthority(
        bool changeContract)
    {
        var coordinator =
            new FlightSessionCoordinator();

        Guid contractId =
            Guid.Parse(
                "22222222-2222-2222-2222-222222222222");

        FlightSession current =
            coordinator.Start(
                Epoch,
                contractId,
                Guid.Parse(
                    "11111111-1111-1111-1111-111111111111"));

        FlightSession conflicting =
            current with
            {
                ContractId =
                    changeContract
                        ? Guid.Parse(
                            "33333333-3333-3333-3333-333333333333")
                        : current.ContractId,
                CreatedAt =
                    changeContract
                        ? current.CreatedAt
                        : current.CreatedAt.AddSeconds(-1),
                UpdatedAt = current.UpdatedAt.AddSeconds(1)
            };

        var observed =
            new List<FlightSession?>();

        coordinator.SessionChanged +=
            (_, args) =>
                observed.Add(args.Session);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinator.CommitPersisted(conflicting));

        Assert.Contains("immutable session authority", error.Message);
        Assert.Same(current, coordinator.Current);
        Assert.Empty(observed);
    }

    [Fact]
    public void NewerTerminalCheckpointCannotRewriteTerminalOutcome()
    {
        var coordinator =
            new FlightSessionCoordinator();

        FlightSession active =
            FlightSession.Start(Epoch);

        FlightSession cancelled =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: true),
                    CancelRequested: true));

        FlightSession suspended =
            FlightSessionEngine.Advance(
                active,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(1),
                        Connected: false)));

        FlightSession interrupted =
            FlightSessionEngine.Advance(
                suspended,
                new FlightSessionAdvance(
                    new FlightStateEvidence(
                        Epoch.AddSeconds(2),
                        Connected: true,
                        StableTelemetry: true,
                        ContinuityPlausible: false)));

        coordinator.Restore(cancelled);

        var observed =
            new List<FlightSession?>();

        coordinator.SessionChanged +=
            (_, args) =>
                observed.Add(args.Session);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(
                () => coordinator.CommitPersisted(interrupted));

        Assert.Contains("terminal outcome", error.Message);
        Assert.Equal(cancelled, coordinator.Current);
        Assert.Empty(observed);
    }

    [Fact]
    public void ActiveSessionCannotBeCleared()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Start(Epoch);

        Assert.Throws<InvalidOperationException>(
            coordinator.ClearTerminalSession);
    }
}
