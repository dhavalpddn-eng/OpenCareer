using OpenCareer.Domain.Telemetry;

namespace OpenCareer.Application.Simulator;

public sealed record SimulatorMissionAircraftSpawnRequest(
    string ActorKey,
    string ContainerTitle,
    string Livery,
    string TailNumber,
    int FlightNumber,
    string FlightPlanPath,
    double FlightPlanPosition,
    bool TouchAndGo)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ActorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(ContainerTitle);
        ArgumentNullException.ThrowIfNull(Livery);
        ArgumentException.ThrowIfNullOrWhiteSpace(TailNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(FlightPlanPath);

        if (TailNumber.Length > 12)
            throw new ArgumentException(
                "SimConnect AI aircraft tail numbers cannot exceed 12 characters.",
                nameof(TailNumber));

        if (!double.IsFinite(FlightPlanPosition) || FlightPlanPosition < 0)
            throw new ArgumentOutOfRangeException(nameof(FlightPlanPosition));
    }
}

public sealed record SimulatorMissionActorHandle(
    string ActorKey,
    uint ObjectId);

public sealed record SimulatorMissionActorSnapshot(
    SimulatorMissionActorHandle Handle,
    AircraftTelemetrySnapshot? Telemetry,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface ISimulatorMissionActorService
{
    IReadOnlyList<SimulatorMissionActorSnapshot> Actors { get; }

    Task<SimulatorMissionActorHandle> SpawnEnrouteAircraftAsync(
        SimulatorMissionAircraftSpawnRequest request);

    Task RemoveActorAsync(SimulatorMissionActorHandle actor);
}
