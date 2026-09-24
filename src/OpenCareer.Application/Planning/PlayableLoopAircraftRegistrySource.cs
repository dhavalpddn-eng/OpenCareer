using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Planning;

/// <summary>
/// Career-owned bootstrap for the first provider aircraft. This identity and its
/// conservative planning profile do not depend on a local MSFS installation.
/// Additional aircraft require explicit app-side registration.
/// </summary>
public sealed class PlayableLoopAircraftRegistrySource : IAircraftRegistryObservationSource
{
    public const string AircraftTitle = "Cessna 172 Skyhawk";
    public static string AircraftId => AircraftCanonicalIdentity.FromMsfsTitle(AircraftTitle);

    public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AircraftRegistryObservation> observations =
            string.Equals(canonicalAircraftId, AircraftId, StringComparison.OrdinalIgnoreCase)
                ? [new AircraftRegistryObservation(
                    AircraftId,
                    "opencareer-provider-bootstrap",
                    "cessna-172-skyhawk-v1",
                    AircraftDataConfidence.Reference,
                    IsInstalled: false,
                    DisplayName: AircraftTitle,
                    Capabilities: AircraftCapability.Passenger | AircraftCapability.Training,
                    Access: AircraftAccess.Civilian,
                    MaximumPayloadPounds: 500,
                    MaximumRangeNauticalMiles: 300,
                    TypicalCruiseKnots: 100,
                    Seats: 4,
                    EngineCount: 1,
                    IfrCapable: false,
                    Pressurized: false,
                    RetractableGear: false,
                    RunwayPerformance: new AircraftRunwayPerformanceProfile(
                        MinimumTakeoffRunwayFeet: 2_500,
                        MinimumLandingRunwayFeet: 2_000,
                        MinimumRunwayWidthFeet: 50,
                        SupportedSurfaces: RunwaySurfaceSupport.Asphalt | RunwaySurfaceSupport.Concrete,
                        Confidence: AircraftDataConfidence.Reference,
                        Source: "opencareer-c172-conservative-dispatch-baseline"))]
                : [];

        return Task.FromResult(observations);
    }
}
