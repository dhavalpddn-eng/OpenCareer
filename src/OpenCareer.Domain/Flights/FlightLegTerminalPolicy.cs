namespace OpenCareer.Domain.Flights;

public sealed record FlightLegTerminalEvidence(
    bool AtAcceptedTerminal,
    bool OnGround,
    bool Stationary,
    bool ParkingBrakeSet,
    int EnginesRunning,
    bool ServicingComplete);

public sealed record FlightLegTerminalPolicy(
    bool RequireAcceptedTerminal,
    bool RequireOnGround,
    bool RequireStationary,
    bool RequireParkingBrake,
    bool RequireEnginesOff,
    bool RequireServicingComplete)
{
    public static FlightLegTerminalPolicy ConventionalCommercialTurnaround { get; } =
        new(true, true, true, true, true, true);

    public bool IsSatisfied(FlightLegTerminalEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        if (evidence.EnginesRunning < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(evidence), "Engine count cannot be negative.");
        }

        return (!RequireAcceptedTerminal || evidence.AtAcceptedTerminal) &&
               (!RequireOnGround || evidence.OnGround) &&
               (!RequireStationary || evidence.Stationary) &&
               (!RequireParkingBrake || evidence.ParkingBrakeSet) &&
               (!RequireEnginesOff || evidence.EnginesRunning == 0) &&
               (!RequireServicingComplete || evidence.ServicingComplete);
    }
}
