using OpenCareer.Application.Planning;
using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Infrastructure.Aircraft;

/// <summary>
/// Minimal provider-neutral reference data needed to screen the current playable
/// loop's Cessna 172. Installation remains a separate simulator observation.
/// Published values come from Textron Aviation's current Skyhawk specification;
/// the 124-knot maximum cruise is rounded down for typical-cruise screening and
/// the runway width is a conservative whole-foot screen above the published
/// 36 ft 1 in wingspan. Boolean configuration fields describe that documented
/// current production model, not every historical 172 variant.
/// </summary>
public sealed class PlayableLoopReferenceAircraftObservationSource
    : IAircraftRegistryObservationSource
{
    public const string ProviderId = "opencareer-playable-loop-aircraft";
    public const string SkyhawkSource =
        "https://cessna.txtav.com/en/piston/cessna-skyhawk";

    private const string SkyhawkRecordId =
        "cessna-skyhawk-reference-2026-09-25";

    private static readonly AircraftRegistryObservation Skyhawk =
        new(
            AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId,
            ProviderId,
            SkyhawkRecordId,
            AircraftDataConfidence.Reference,
            IsInstalled: false,
            DisplayName: AircraftCanonicalIdentity.Cessna172SkyhawkTitle,
            Capabilities:
                AircraftCapability.Passenger
                | AircraftCapability.Training
                | AircraftCapability.Trainer,
            Access: AircraftAccess.Civilian,
            MaximumPayloadPounds: 870,
            MaximumRangeNauticalMiles: 640,
            TypicalCruiseKnots: 120,
            Seats: 4,
            EngineCount: 1,
            IfrCapable: true,
            Pressurized: false,
            RetractableGear: false,
            RunwayPerformance:
                new AircraftRunwayPerformanceProfile(
                    MinimumTakeoffRunwayFeet: 1_630,
                    MinimumLandingRunwayFeet: 1_335,
                    MinimumRunwayWidthFeet: 40,
                    SupportedSurfaces:
                        RunwaySurfaceSupport.Asphalt
                        | RunwaySurfaceSupport.Concrete,
                    AircraftDataConfidence.Reference,
                    Source: SkyhawkSource),
            ReferenceMetadata:
                new AircraftReferenceMetadata(
                    IcaoManufacturer: "Cessna",
                    IcaoModel: "172 Skyhawk",
                    EngineType: AircraftEngineType.Piston,
                    PassengerCapacity: 4));

    public Task<IReadOnlyList<AircraftRegistryObservation>> FindAircraftObservationsAsync(
        string canonicalAircraftId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAircraftId);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AircraftRegistryObservation> result =
            string.Equals(
                canonicalAircraftId.Trim(),
                AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId,
                StringComparison.OrdinalIgnoreCase)
                ? [Skyhawk]
                : Array.Empty<AircraftRegistryObservation>();

        return Task.FromResult(result);
    }
}
