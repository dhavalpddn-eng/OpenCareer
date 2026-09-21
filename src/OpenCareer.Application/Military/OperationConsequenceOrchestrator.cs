using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Application.Military;

public sealed record OperationConsequenceState(
    FactionInfluenceState FactionInfluence,
    CampaignProgressState CampaignProgress,
    TerritoryPressureState TerritoryPressure,
    MilitaryCareerState MilitaryCareer,
    ConflictResourceState Resources)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(FactionInfluence);
        ArgumentNullException.ThrowIfNull(CampaignProgress);
        ArgumentNullException.ThrowIfNull(TerritoryPressure);
        ArgumentNullException.ThrowIfNull(MilitaryCareer);
        ArgumentNullException.ThrowIfNull(Resources);

        FactionInfluence.Validate();
        CampaignProgress.Validate();
        TerritoryPressure.Validate();
        MilitaryCareer.Validate();
        Resources.Validate();

        if (!string.Equals(
            CampaignProgress.CampaignId,
            Resources.CampaignId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Campaign progress and conflict resources must belong to the same campaign.");
        }

        if (MilitaryCareer.Affiliation == MilitaryAffiliation.None)
        {
            throw new InvalidOperationException(
                "Military operation consequences require an affiliated military career.");
        }
    }
}

public sealed record OperationConsequenceResult(
    OperationOutcome Outcome,
    FactionInfluenceState FactionInfluence,
    CampaignProgressState CampaignProgress,
    TerritoryPressureResult TerritoryPressure,
    MilitaryCareerState MilitaryCareer,
    ConflictResourceState Resources)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Outcome);
        ArgumentNullException.ThrowIfNull(FactionInfluence);
        ArgumentNullException.ThrowIfNull(CampaignProgress);
        ArgumentNullException.ThrowIfNull(TerritoryPressure);
        ArgumentNullException.ThrowIfNull(MilitaryCareer);
        ArgumentNullException.ThrowIfNull(Resources);

        Outcome.Validate();
        FactionInfluence.Validate();
        CampaignProgress.Validate();
        TerritoryPressure.State.Validate();
        MilitaryCareer.Validate();
        Resources.Validate();

        if (!double.IsFinite(TerritoryPressure.FriendlyControlDelta)
            || TerritoryPressure.FriendlyControlDelta is < -1 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(TerritoryPressure.FriendlyControlDelta));
        }

        if (!string.Equals(
            CampaignProgress.CampaignId,
            Resources.CampaignId,
            StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Campaign progress and conflict resources must belong to the same campaign.");
        }
    }
}

public sealed class OperationConsequenceOrchestrator
{
    private readonly IOperationResolver _resolver;
    private readonly MilitaryReputationConsequence _reputation;

    public OperationConsequenceOrchestrator(
        IOperationResolver resolver,
        MilitaryReputationConsequence reputation)
    {
        _resolver = resolver
            ?? throw new ArgumentNullException(nameof(resolver));
        _reputation = reputation
            ?? throw new ArgumentNullException(nameof(reputation));
    }

    public OperationConsequenceResult Apply(
        OperationResolutionInput input,
        OperationConsequenceState current)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(current);

        input.Validate();
        current.Validate();

        OperationOutcome outcome = _resolver.Resolve(input);
        outcome.Validate();

        FactionInfluenceState influence =
            FactionInfluenceConsequence.Apply(
                current.FactionInfluence,
                outcome);

        CampaignProgressState progress =
            CampaignProgressConsequence.Apply(
                current.CampaignProgress,
                outcome);

        TerritoryPressureResult territory =
            TerritoryPressureConsequence.Apply(
                current.TerritoryPressure,
                outcome);

        MilitaryCareerState reputation =
            _reputation.Apply(
                current.MilitaryCareer,
                outcome);

        ConflictResourceState resources =
            ConflictResourceConsequence.Apply(
                current.Resources,
                outcome);

        var result = new OperationConsequenceResult(
            outcome,
            influence,
            progress,
            territory,
            reputation,
            resources);

        result.Validate();
        return result;
    }
}
