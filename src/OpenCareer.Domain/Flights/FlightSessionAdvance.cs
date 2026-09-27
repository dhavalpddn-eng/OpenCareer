namespace OpenCareer.Domain.Flights;

public sealed record FlightSessionAdvance(
    FlightStateEvidence Evidence,
    FlightTimeInterval? TimeInterval = null,
    bool ShutdownConfirmed = false,
    bool CancelRequested = false,
    FlightContinuityAnchor? ContinuityAnchor = null,
    FlightSessionObservation? Observation = null)
{
    public void Validate(DateTimeOffset previousUpdate)
    {
        ArgumentNullException.ThrowIfNull(Evidence);

        if (Evidence.Timestamp < previousUpdate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Evidence),
                "Flight-session evidence cannot move backward in time.");
        }

        if (CancelRequested && ShutdownConfirmed)
        {
            throw new ArgumentException(
                "A flight-session update cannot confirm shutdown and cancellation simultaneously.");
        }

        Observation?.Validate();

        DateTimeOffset? previousContact = null;
        foreach (FlightLandingContactEvidence contact in Evidence.LandingContacts ?? [])
        {
            ArgumentNullException.ThrowIfNull(contact);
            contact.Validate();
            if (contact.Timestamp > Evidence.Timestamp
                || previousContact is { } previous && contact.Timestamp <= previous)
                throw new ArgumentException("Contact evidence must be ordered and cannot be newer than its confirmation.", nameof(Evidence));
            previousContact = contact.Timestamp;
        }

        if (Observation is not null
            && Observation.Timestamp > Evidence.Timestamp)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Observation),
                "A flight-session observation cannot be newer than its evidence.");
        }

        if (ContinuityAnchor is not null)
        {
            ContinuityAnchor.Validate();

            if (ContinuityAnchor.Timestamp > Evidence.Timestamp)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ContinuityAnchor),
                    "A continuity anchor cannot be newer than its flight evidence.");
            }
        }
    }
}
