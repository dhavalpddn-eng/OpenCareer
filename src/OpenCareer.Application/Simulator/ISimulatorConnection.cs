namespace OpenCareer.Application.Simulator;

/// <summary>Owns connection lifetime without exposing SDK handles, threads or UI objects.</summary>
public interface ISimulatorConnection : IAsyncDisposable
{
    /// <summary>An immutable snapshot safe to read from any thread.</summary>
    SimulatorConnectionSnapshot Current { get; }

    /// <summary>Starts automatic connection attempts without blocking. Repeated starts are idempotent.</summary>
    void Start();

    /// <summary>Stops retries and waits for the owning worker to release the connection.</summary>
    Task StopAsync();
}
