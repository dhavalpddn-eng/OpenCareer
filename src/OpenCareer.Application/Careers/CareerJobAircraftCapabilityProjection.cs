using OpenCareer.Domain.Aircraft;

namespace OpenCareer.Application.Careers;

/// <summary>
/// Shared fail-closed projection used by job screening and the final start gate.
/// Missing numeric or capability evidence becomes zero/false and can never make
/// an aircraft more capable than the registry proved.
/// </summary>
public static class CareerJobAircraftCapabilityProjection
{
    public static AircraftRegistryRecord? TryCreate(
        AircraftRegistryResolution resolution)
    {
        ArgumentNullException.ThrowIfNull(resolution);

        AircraftRegistryRecord? complete = resolution.TryCreateRegistryRecord();
        if (complete is not null)
            return complete;

        if (resolution.InstallationStatus
                != AircraftInstallationStatus.Installed
            || resolution.CapabilityValues.Access is not { } access
            || access == AircraftAccess.None)
        {
            return null;
        }

        ResolvedAircraftCapabilities values = resolution.CapabilityValues;
        var profile = new AircraftCapabilityProfile(
            resolution.CanonicalAircraftId,
            string.IsNullOrWhiteSpace(values.DisplayName)
                ? resolution.CanonicalAircraftId
                : values.DisplayName,
            values.Capabilities ?? AircraftCapability.None,
            access,
            values.MaximumPayloadPounds ?? 0,
            values.MaximumRangeNauticalMiles ?? 0,
            values.TypicalCruiseKnots ?? 0,
            values.Seats ?? 0,
            values.EngineCount ?? 0,
            values.IfrCapable ?? false,
            values.Pressurized ?? false,
            values.RetractableGear ?? false);

        var record = new AircraftRegistryRecord(
            profile,
            IsInstalled: true,
            resolution.RunwayPerformance);

        record.Validate();
        return record;
    }
}
