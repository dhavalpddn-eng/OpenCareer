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
    public void ActiveSessionCannotBeCleared()
    {
        var coordinator =
            new FlightSessionCoordinator();

        coordinator.Start(Epoch);

        Assert.Throws<InvalidOperationException>(
            coordinator.ClearTerminalSession);
    }
}
