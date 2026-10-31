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
        if (Current is not null)
        {
            throw new InvalidOperationException(
                "The current flight session must be cleared before a new session can start.");
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

        if (Current is not null)
        {
            throw new InvalidOperationException(
                "The current flight session must be cleared before another session can be restored.");
        }

        Current = session;
        Publish();
    }

    public void CommitPersisted(
        FlightSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (Current is null)
        {
            Current = session;
            Publish();
            return;
        }

        if (Current.SessionId != session.SessionId)
        {
            throw new InvalidOperationException(
                "Cannot replace the current flight session with a different session identity.");
        }

        if (Current.ContractId != session.ContractId
            || Current.CreatedAt != session.CreatedAt
            || Current.Plan != session.Plan)
        {
            throw new InvalidOperationException(
                "Cannot replace the current flight session with different immutable session authority.");
        }

        if (Current.IsTerminal && !session.IsTerminal)
        {
            throw new InvalidOperationException(
                "Cannot replace a terminal flight session with a nonterminal checkpoint.");
        }

        if (Current.IsTerminal
            && session.Status != Current.Status)
        {
            throw new InvalidOperationException(
                "Cannot replace a terminal flight session with a different terminal outcome.");
        }

        if (session.UpdatedAt < Current.UpdatedAt)
        {
            throw new InvalidOperationException(
                "Cannot replace the current flight session with an older checkpoint.");
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

    public void ClearTerminalSession(
        FlightSession expectedSession)
    {
        ArgumentNullException.ThrowIfNull(expectedSession);

        if (Current is null)
            return;

        if (!ReferenceEquals(Current, expectedSession))
        {
            throw new InvalidOperationException(
                "The current flight session changed before terminal cleanup completed.");
        }

        ClearTerminalSession();
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
