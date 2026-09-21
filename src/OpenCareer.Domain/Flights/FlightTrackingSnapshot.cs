namespace OpenCareer.Domain.Flights;

public sealed record FlightTrackingSnapshot(
    FlightTrackingState State,
    FlightTrackingState? SuspendedFrom,
    DateTimeOffset UpdatedAt,
    int TakeoffCount,
    int LandingEpisodeCount,
    int BounceCount,
    int TouchAndGoCount,
    int RejectedTakeoffCount,
    bool CrashReported)
{
    public static FlightTrackingSnapshot Start(DateTimeOffset timestamp) =>
        new(FlightTrackingState.Observing, null, timestamp, 0, 0, 0, 0, 0, false);
}
