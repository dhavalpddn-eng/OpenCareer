using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationConsequenceOrchestratorTests
{
    [Fact]
    public void SuccessAppliesEveryConsequenceFromOneResolvedOutcome()
    {
        OperationConsequenceOrchestrator orchestrator =
            CreateOrchestrator();

        OperationConsequenceResult result =
            orchestrator.Apply(
                ValidInput(),
                BaselineState());

        Assert.Equal(
            OperationOutcomeStatus.Success,
            result.Outcome.Status);

        Assert.Equal(
            0.54,
            result.FactionInfluence.FriendlyInfluence,
            precision: 10);
        Assert.Equal(
            0.46,
            result.FactionInfluence.HostileInfluence,
            precision: 10);

        Assert.Equal(
            0.56,
            result.CampaignProgress.FriendlyProgress,
            precision: 10);

        Assert.False(result.TerritoryPressure.ThresholdCrossed);
        Assert.Equal(
            0.10,
            result.TerritoryPressure.State.AccumulatedFriendlyPressure,
            precision: 10);

        Assert.Equal(
            0.52,
            result.MilitaryCareer.Trust,
            precision: 10);
        Assert.Equal(
            3,
            result.MilitaryCareer.SuccessfulOperations);

        Assert.Equal(
            0.54,
            result.Resources.FriendlySupply,
            precision: 10);
        Assert.Equal(
            0.53,
            result.Resources.FriendlyOperationalReadiness,
            precision: 10);
        Assert.Equal(
            0.47,
            result.Resources.HostileSupply,
            precision: 10);
    }

    [Fact]
    public void FailureAppliesFailureConsequencesConsistently()
    {
        OperationConsequenceResult result =
            CreateOrchestrator().Apply(
                ValidInput() with
                {
                    MissionResult = MissionExecutionResult.Failed
                },
                BaselineState());

        Assert.Equal(
            OperationOutcomeStatus.Failure,
            result.Outcome.Status);
        Assert.Equal(
            0.47,
            result.FactionInfluence.FriendlyInfluence,
            precision: 10);
        Assert.Equal(
            0.45,
            result.CampaignProgress.FriendlyProgress,
            precision: 10);
        Assert.Equal(
            -0.08,
            result.TerritoryPressure.State.AccumulatedFriendlyPressure,
            precision: 10);
        Assert.Equal(
            0.47,
            result.MilitaryCareer.Trust,
            precision: 10);
        Assert.Equal(
            2,
            result.MilitaryCareer.FailedOperations);
        Assert.Equal(
            0.46,
            result.Resources.FriendlySupply,
            precision: 10);
    }

    [Fact]
    public void DuplicateResolutionCannotApplyConsequencesTwice()
    {
        OperationConsequenceOrchestrator orchestrator =
            CreateOrchestrator();
        OperationResolutionInput input = ValidInput();
        OperationConsequenceState state = BaselineState();

        orchestrator.Apply(input, state);

        Assert.Throws<DuplicateOperationResolutionException>(
            () => orchestrator.Apply(input, state));
    }

    [Fact]
    public void InvalidAggregateStateIsRejectedBeforeResolutionIsConsumed()
    {
        OperationConsequenceOrchestrator orchestrator =
            CreateOrchestrator();
        OperationResolutionInput input = ValidInput();

        OperationConsequenceState invalid =
            BaselineState() with
            {
                Resources = BaselineState().Resources with
                {
                    CampaignId = "campaign:other"
                }
            };

        Assert.Throws<ArgumentException>(
            () => orchestrator.Apply(input, invalid));

        OperationConsequenceResult retry =
            orchestrator.Apply(
                input,
                BaselineState());

        Assert.Equal(
            OperationOutcomeStatus.Success,
            retry.Outcome.Status);
    }

    [Fact]
    public void ResultPreservesCampaignAndSectorIdentity()
    {
        OperationConsequenceState current = BaselineState();

        OperationConsequenceResult result =
            CreateOrchestrator().Apply(
                ValidInput(),
                current);

        Assert.Equal(
            current.CampaignProgress.CampaignId,
            result.CampaignProgress.CampaignId);
        Assert.Equal(
            current.Resources.CampaignId,
            result.Resources.CampaignId);
        Assert.Equal(
            current.TerritoryPressure.SectorId,
            result.TerritoryPressure.State.SectorId);
        Assert.Equal(
            current.FactionInfluence.FriendlyFactionId,
            result.FactionInfluence.FriendlyFactionId);
        Assert.Equal(
            current.FactionInfluence.HostileFactionId,
            result.FactionInfluence.HostileFactionId);
    }

    private static OperationConsequenceOrchestrator CreateOrchestrator()
    {
        IOperationResolver resolver =
            new IdempotentOperationResolver(
                new OperationResolver(),
                new InMemoryOperationResolutionRegistry());

        var reputation =
            new MilitaryReputationConsequence(
                new InMemoryMilitaryReputationConsequenceRegistry());

        return new OperationConsequenceOrchestrator(
            resolver,
            reputation);
    }

    private static OperationConsequenceState BaselineState() =>
        new(
            new FactionInfluenceState(
                "faction:ast",
                "faction:ghc",
                FriendlyInfluence: 0.50,
                HostileInfluence: 0.50),
            new CampaignProgressState(
                "campaign:test",
                FriendlyProgress: 0.50),
            new TerritoryPressureState(
                "sector-alpha",
                AccumulatedFriendlyPressure: 0),
            new MilitaryCareerState(
                MilitaryAffiliation.Reserve,
                MilitaryQualification.MilitaryFlight,
                Trust: 0.50,
                SuccessfulOperations: 2,
                FailedOperations: 1),
            new ConflictResourceState(
                "campaign:test",
                FriendlySupply: 0.50,
                FriendlyOperationalReadiness: 0.50,
                HostileSupply: 0.50));

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("64d6dc78-9984-4dbc-a101-b963b2e8508d"),
            "support-010",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(45),
            ResolvedAt: new DateTimeOffset(
                2026,
                9,
                21,
                5,
                0,
                0,
                TimeSpan.Zero));
}
