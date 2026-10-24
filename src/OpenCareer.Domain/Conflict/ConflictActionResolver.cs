namespace OpenCareer.Domain.Conflict;

public sealed record ConflictActionResolution(
    ConflictWorldState State,
    PlayerActionResult Result);

public static class ConflictActionResolver
{
    public static ConflictActionResolution Apply(
        ConflictWorldState state,
        PlayerActionRequest request)
    {
        ConflictValidation.Validate(state);
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        if (state.ProcessedEventIds.Contains(request.ActionId, StringComparer.Ordinal))
        {
            return new ConflictActionResolution(
                state,
                new PlayerActionResult(
                    request.ActionId,
                    PlayerActionOutcome.DuplicateIgnored,
                    0,
                    0,
                    0,
                    "Action already applied."));
        }

        var targetIndex = Array.FindIndex(
            state.Units,
            unit => unit.UnitId == request.TargetUnitId);

        if (targetIndex < 0 || state.Units[targetIndex].Side != ConflictSide.Hostile)
        {
            return new ConflictActionResolution(
                state,
                new PlayerActionResult(
                    request.ActionId,
                    PlayerActionOutcome.InvalidTarget,
                    0,
                    0,
                    0,
                    "Target is unavailable or not hostile."));
        }

        var target = state.Units[targetIndex];
        if (!target.IsOperational)
        {
            return new ConflictActionResolution(
                MarkProcessed(state, request.ActionId),
                new PlayerActionResult(
                    request.ActionId,
                    PlayerActionOutcome.Rejected,
                    0,
                    0,
                    0,
                    "Target is no longer operational."));
        }

        var quality = Math.Clamp(
            request.GeometryQuality * 0.65 + request.TargetConfidence * 0.35,
            0,
            1);

        var strengthRemoved = 0d;
        var readinessRemoved = 0d;
        var intelligenceGain = 0d;

        switch (request.Kind)
        {
            case PlayerActionKind.PrecisionAttack:
                strengthRemoved = Math.Min(target.Strength, 0.32 * quality);
                readinessRemoved = Math.Min(target.Readiness, 0.18 * quality);
                break;

            case PlayerActionKind.Suppression:
                strengthRemoved = Math.Min(target.Strength, 0.08 * quality);
                readinessRemoved = Math.Min(target.Readiness, 0.42 * quality);
                break;

            case PlayerActionKind.Reconnaissance:
                intelligenceGain = 0.30 * quality;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request.Kind));
        }

        var units = state.Units.ToArray();
        units[targetIndex] = target with
        {
            Strength = Math.Clamp(target.Strength - strengthRemoved, 0, 1),
            Readiness = Math.Clamp(target.Readiness - readinessRemoved, 0, 1)
        };

        var sectors = state.Sectors
            .Select(sector =>
            {
                if (ConflictGeometry.DistanceNauticalMiles(sector.Center, target.Position) > 35)
                    return sector;

                var controlGain = request.Kind == PlayerActionKind.Reconnaissance
                    ? 0
                    : Math.Clamp(strengthRemoved * 0.08 + readinessRemoved * 0.03, 0, 0.04);

                return sector with
                {
                    FriendlyControl = Math.Clamp(sector.FriendlyControl + controlGain, 0, 1),
                    IntelligenceConfidence = Math.Clamp(
                        sector.IntelligenceConfidence + intelligenceGain,
                        0,
                        1)
                };
            })
            .ToArray();

        var updated = state with
        {
            Units = units,
            Sectors = sectors,
            ProcessedEventIds = state.ProcessedEventIds
                .Append(request.ActionId)
                .ToArray()
        };

        updated = ConflictWorldEngine.RecalculatePressureAndThreats(updated);
        updated = SupportRequestGenerator.Refresh(updated, state.UpdatedAt);

        return new ConflictActionResolution(
            updated,
            new PlayerActionResult(
                request.ActionId,
                PlayerActionOutcome.Applied,
                strengthRemoved,
                readinessRemoved,
                intelligenceGain,
                null));
    }

    private static ConflictWorldState MarkProcessed(
        ConflictWorldState state,
        string eventId) =>
        state with
        {
            ProcessedEventIds = state.ProcessedEventIds
                .Append(eventId)
                .ToArray()
        };
}
