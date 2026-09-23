namespace OpenCareer.Application.Simulator;

/// <summary>
/// Exposes only the aircraft currently loaded in the connected simulator.
/// This is live-flight evidence, not installed-aircraft or career ownership authority.
/// </summary>
public interface ILiveAircraftIdentitySource
{
    string? CurrentAircraftTitle { get; }
}
