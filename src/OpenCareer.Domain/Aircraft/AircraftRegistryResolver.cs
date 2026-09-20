namespace OpenCareer.Domain.Aircraft;

public enum AircraftInstallationStatus
{
    KnownOnly = 0,
    Installed = 1
}

public enum AircraftRegistryField
{
    DisplayName = 0,
    Capabilities,
    Access,
    MaximumPayloadPounds,
    MaximumRangeNauticalMiles,
    TypicalCruiseKnots,
    Seats,
    EngineCount,
    IfrCapable,
    Pressurized,
    RetractableGear,
    RunwayPerformance,
    ReferenceMetadata
}

public sealed record AircraftRegistryObservation(
    string CanonicalAircraftId,
    string ProviderId,
    string ProviderRecordId,
    AircraftDataConfidence Confidence,
    bool IsInstalled,
    string? DisplayName = null,
    AircraftCapability? Capabilities = null,
    AircraftAccess? Access = null,
    double? MaximumPayloadPounds = null,
    double? MaximumRangeNauticalMiles = null,
    double? TypicalCruiseKnots = null,
    int? Seats = null,
    int? EngineCount = null,
    bool? IfrCapable = null,
    bool? Pressurized = null,
    bool? RetractableGear = null,
    AircraftRunwayPerformanceProfile? RunwayPerformance = null,
    AircraftReferenceMetadata? ReferenceMetadata = null)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(CanonicalAircraftId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ProviderRecordId);

        if (!Enum.IsDefined(Confidence))
            throw new ArgumentOutOfRangeException(nameof(Confidence));

        if (DisplayName is not null && string.IsNullOrWhiteSpace(DisplayName))
            throw new ArgumentException("Display name must be non-empty when supplied.", nameof(DisplayName));

        if (Access is { } access
            && (access == AircraftAccess.None
                || (access & ~AircraftAccess.Any) != 0))
        {
            throw new ArgumentOutOfRangeException(nameof(Access));
        }

        ValidateOptionalNonNegative(MaximumPayloadPounds, nameof(MaximumPayloadPounds));
        ValidateOptionalNonNegative(MaximumRangeNauticalMiles, nameof(MaximumRangeNauticalMiles));
        ValidateOptionalNonNegative(TypicalCruiseKnots, nameof(TypicalCruiseKnots));

        if (Seats is < 0)
            throw new ArgumentOutOfRangeException(nameof(Seats));

        if (EngineCount is < 0)
            throw new ArgumentOutOfRangeException(nameof(EngineCount));

        RunwayPerformance?.Validate();
        ReferenceMetadata?.Validate();
    }

    private static void ValidateOptionalNonNegative(double? value, string name)
    {
        if (value is { } number && (!double.IsFinite(number) || number < 0))
            throw new ArgumentOutOfRangeException(name);
    }
}

public sealed record AircraftRegistryFieldProvenance(
    AircraftRegistryField Field,
    string ProviderId,
    string ProviderRecordId,
    AircraftDataConfidence Confidence,
    bool InstalledEvidence);

public sealed record ResolvedAircraftCapabilities(
    string? DisplayName,
    AircraftCapability? Capabilities,
    AircraftAccess? Access,
    double? MaximumPayloadPounds,
    double? MaximumRangeNauticalMiles,
    double? TypicalCruiseKnots,
    int? Seats,
    int? EngineCount,
    bool? IfrCapable,
    bool? Pressurized,
    bool? RetractableGear);

public sealed record AircraftRegistryResolution(
    string CanonicalAircraftId,
    AircraftInstallationStatus InstallationStatus,
    ResolvedAircraftCapabilities CapabilityValues,
    AircraftRunwayPerformanceProfile? RunwayPerformance,
    AircraftReferenceMetadata? ReferenceMetadata,
    IReadOnlyList<AircraftRegistryField> UnresolvedCapabilityFields,
    IReadOnlyDictionary<AircraftRegistryField, AircraftRegistryFieldProvenance> Provenance)
{
    public bool HasCompleteCapabilityProfile => UnresolvedCapabilityFields.Count == 0;

    public AircraftRegistryRecord? TryCreateRegistryRecord()
    {
        if (!HasCompleteCapabilityProfile)
            return null;

        var profile = new AircraftCapabilityProfile(
            CanonicalAircraftId,
            CapabilityValues.DisplayName!,
            CapabilityValues.Capabilities!.Value,
            CapabilityValues.Access!.Value,
            CapabilityValues.MaximumPayloadPounds!.Value,
            CapabilityValues.MaximumRangeNauticalMiles!.Value,
            CapabilityValues.TypicalCruiseKnots!.Value,
            CapabilityValues.Seats!.Value,
            CapabilityValues.EngineCount!.Value,
            CapabilityValues.IfrCapable!.Value,
            CapabilityValues.Pressurized!.Value,
            CapabilityValues.RetractableGear!.Value);

        var record = new AircraftRegistryRecord(
            profile,
            InstallationStatus == AircraftInstallationStatus.Installed,
            RunwayPerformance);

        record.Validate();
        return record;
    }
}

public static class AircraftRegistryResolver
{
    private static readonly AircraftRegistryField[] RequiredCapabilityFields =
    [
        AircraftRegistryField.DisplayName,
        AircraftRegistryField.Capabilities,
        AircraftRegistryField.Access,
        AircraftRegistryField.MaximumPayloadPounds,
        AircraftRegistryField.MaximumRangeNauticalMiles,
        AircraftRegistryField.TypicalCruiseKnots,
        AircraftRegistryField.Seats,
        AircraftRegistryField.EngineCount,
        AircraftRegistryField.IfrCapable,
        AircraftRegistryField.Pressurized,
        AircraftRegistryField.RetractableGear
    ];

    public static AircraftRegistryResolution Resolve(
        IEnumerable<AircraftRegistryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        AircraftRegistryObservation[] items = observations.ToArray();
        if (items.Length == 0)
            throw new ArgumentException("At least one aircraft registry observation is required.", nameof(observations));

        foreach (AircraftRegistryObservation observation in items)
        {
            ArgumentNullException.ThrowIfNull(observation);
            observation.Validate();
        }

        string[] canonicalIds = items
            .Select(static item => item.CanonicalAircraftId.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (canonicalIds.Length != 1)
            throw new ArgumentException("All observations must refer to one canonical aircraft identity.", nameof(observations));

        EnsureUniqueProviderRecords(items);

        string canonicalId = items
            .Select(static item => item.CanonicalAircraftId.Trim())
            .OrderBy(static id => id, StringComparer.Ordinal)
            .First();

        var selected = new Dictionary<AircraftRegistryField, FieldCandidate>();

        Add(
            AircraftRegistryField.DisplayName,
            Select(items, static item => item.DisplayName));
        Add(
            AircraftRegistryField.Capabilities,
            Select(items, static item => item.Capabilities));
        Add(
            AircraftRegistryField.Access,
            Select(items, static item => item.Access));
        Add(
            AircraftRegistryField.MaximumPayloadPounds,
            Select(items, static item => item.MaximumPayloadPounds));
        Add(
            AircraftRegistryField.MaximumRangeNauticalMiles,
            Select(items, static item => item.MaximumRangeNauticalMiles));
        Add(
            AircraftRegistryField.TypicalCruiseKnots,
            Select(items, static item => item.TypicalCruiseKnots));
        Add(
            AircraftRegistryField.Seats,
            Select(items, static item => item.Seats));
        Add(
            AircraftRegistryField.EngineCount,
            Select(items, static item => item.EngineCount));
        Add(
            AircraftRegistryField.IfrCapable,
            Select(items, static item => item.IfrCapable));
        Add(
            AircraftRegistryField.Pressurized,
            Select(items, static item => item.Pressurized));
        Add(
            AircraftRegistryField.RetractableGear,
            Select(items, static item => item.RetractableGear));
        Add(
            AircraftRegistryField.RunwayPerformance,
            Select(
                items,
                static item => item.RunwayPerformance,
                static item => item.RunwayPerformance!.Confidence));
        Add(
            AircraftRegistryField.ReferenceMetadata,
            Select(items, static item => item.ReferenceMetadata));

        AircraftRegistryField[] unresolved = RequiredCapabilityFields
            .Where(field => !selected.ContainsKey(field))
            .OrderBy(static field => field)
            .ToArray();

        var provenance = selected.ToDictionary(
            static pair => pair.Key,
            static pair => new AircraftRegistryFieldProvenance(
                pair.Key,
                pair.Value.Observation.ProviderId,
                pair.Value.Observation.ProviderRecordId,
                pair.Value.Confidence,
                pair.Value.Observation.IsInstalled));

        var capabilities = new ResolvedAircraftCapabilities(
            ReferenceValue<string>(AircraftRegistryField.DisplayName),
            StructValue<AircraftCapability>(AircraftRegistryField.Capabilities),
            StructValue<AircraftAccess>(AircraftRegistryField.Access),
            StructValue<double>(AircraftRegistryField.MaximumPayloadPounds),
            StructValue<double>(AircraftRegistryField.MaximumRangeNauticalMiles),
            StructValue<double>(AircraftRegistryField.TypicalCruiseKnots),
            StructValue<int>(AircraftRegistryField.Seats),
            StructValue<int>(AircraftRegistryField.EngineCount),
            StructValue<bool>(AircraftRegistryField.IfrCapable),
            StructValue<bool>(AircraftRegistryField.Pressurized),
            StructValue<bool>(AircraftRegistryField.RetractableGear));

        return new(
            canonicalId,
            items.Any(static item => item.IsInstalled)
                ? AircraftInstallationStatus.Installed
                : AircraftInstallationStatus.KnownOnly,
            capabilities,
            ReferenceValue<AircraftRunwayPerformanceProfile>(AircraftRegistryField.RunwayPerformance),
            ReferenceValue<AircraftReferenceMetadata>(AircraftRegistryField.ReferenceMetadata),
            unresolved,
            provenance);

        void Add(AircraftRegistryField field, FieldCandidate? candidate)
        {
            if (candidate is not null)
                selected.Add(field, candidate);
        }

        T? StructValue<T>(AircraftRegistryField field)
            where T : struct
        {
            if (!selected.TryGetValue(field, out FieldCandidate? candidate))
                return null;

            return (T)candidate.Value;
        }

        T? ReferenceValue<T>(AircraftRegistryField field)
            where T : class
        {
            if (!selected.TryGetValue(field, out FieldCandidate? candidate))
                return null;

            return (T)candidate.Value;
        }
    }

    public static IReadOnlyList<AircraftRegistryResolution> ResolveAll(
        IEnumerable<AircraftRegistryObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        AircraftRegistryObservation[] items = observations.ToArray();
        foreach (AircraftRegistryObservation observation in items)
        {
            ArgumentNullException.ThrowIfNull(observation);
            observation.Validate();
        }

        EnsureProviderRecordsDoNotCrossAircraft(items);

        return items
            .GroupBy(
                static item => item.CanonicalAircraftId.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(static group => Resolve(group))
            .OrderBy(static resolution => resolution.CanonicalAircraftId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static resolution => resolution.CanonicalAircraftId, StringComparer.Ordinal)
            .ToArray();
    }

    private static FieldCandidate? Select(
        IEnumerable<AircraftRegistryObservation> observations,
        Func<AircraftRegistryObservation, object?> valueSelector,
        Func<AircraftRegistryObservation, AircraftDataConfidence>? confidenceSelector = null)
    {
        var candidates = new List<FieldCandidate>();

        foreach (AircraftRegistryObservation observation in observations)
        {
            object? value = valueSelector(observation);
            if (value is null)
                continue;

            candidates.Add(
                new(
                    observation,
                    value,
                    confidenceSelector?.Invoke(observation)
                        ?? observation.Confidence));
        }

        return candidates
            .OrderByDescending(static candidate => candidate.Confidence)
            .ThenByDescending(static candidate => candidate.Observation.IsInstalled)
            .ThenBy(static candidate => candidate.Observation.ProviderId, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.Observation.ProviderRecordId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static void EnsureUniqueProviderRecords(
        IEnumerable<AircraftRegistryObservation> observations)
    {
        var keys = new HashSet<(string ProviderId, string ProviderRecordId)>();

        foreach (AircraftRegistryObservation observation in observations)
        {
            if (!keys.Add((observation.ProviderId, observation.ProviderRecordId)))
            {
                throw new ArgumentException(
                    "Duplicate provider record identity was supplied for one aircraft.",
                    nameof(observations));
            }
        }
    }

    private static void EnsureProviderRecordsDoNotCrossAircraft(
        IEnumerable<AircraftRegistryObservation> observations)
    {
        var owners = new Dictionary<(string ProviderId, string ProviderRecordId), string>();

        foreach (AircraftRegistryObservation observation in observations)
        {
            var key = (observation.ProviderId, observation.ProviderRecordId);
            string canonicalId = observation.CanonicalAircraftId.Trim();

            if (owners.TryGetValue(key, out string? existing)
                && !string.Equals(existing, canonicalId, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "One provider record cannot resolve to multiple canonical aircraft identities.",
                    nameof(observations));
            }

            owners[key] = canonicalId;
        }
    }

    private sealed record FieldCandidate(
        AircraftRegistryObservation Observation,
        object Value,
        AircraftDataConfidence Confidence);
}
