using OpenCareer.Domain.Flights;

namespace OpenCareer.Application.Flights;

public sealed class FlightSessionCoordinator
{
    public FlightSession? Current { get; private set; }

    public event EventHandler<FlightSessionChangedEventArgs>? SessionChanged;

    public FlightSession Start(
        DateTimeOffset timestamp,
        Guid? contractId = null,
        Guid? sessionId = null)
    {
        if (Current is { IsTerminal: false })
        {
            throw new InvalidOperationException(
                "An active flight session already exists.");
        }

        Current =
            FlightSession.Start(
                timestamp,
                contractId,
                sessionId);

        Publish();
        return Current;
    }

    public FlightSession Advance(
        FlightSessionAdvance update)
    {
        ArgumentNullException.ThrowIfNull(update);

        FlightSession current =
            Current
            ?? throw new InvalidOperationException(
                "No flight session is active.");

        Current =
            FlightSessionEngine.Advance(
                current,
                update);

        Publish();
        return Current;
    }

    public void Restore(FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (Current is { IsTerminal: false })
        {
            throw new InvalidOperationException(
                "Cannot restore over an active flight session.");
        }

        Current = session;
        Publish();
    }

    public void ClearTerminalSession()
    {
        if (Current is null)
            return;

        if (!Current.IsTerminal)
        {
            throw new InvalidOperationException(
                "An active flight session cannot be cleared.");
        }

        Current = null;
        Publish();
    }

    private void Publish()
    {
        SessionChanged?.Invoke(
            this,
            new FlightSessionChangedEventArgs(Current));
    }
}

public sealed class FlightSessionChangedEventArgs(
    FlightSession? session) : EventArgs
{
    public FlightSession? Session { get; } = session;
}
