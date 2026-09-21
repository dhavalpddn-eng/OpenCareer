using OpenCareer.Domain.Conflict;

namespace OpenCareer.Tests;

public sealed class ConflictCampaignDirectorTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 19, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateProducesDeterministicStrategicObjectives()
    {
        var world = World(control: 0.50, intelligence: 0.30);

        var first = ConflictCampaignDirector.Create("campaign-a", world);
        var second = ConflictCampaignDirector.Create("campaign-a", world);

        Assert.Equal(first.Phase, second.Phase);
        Assert.Equal(first.FriendlyControlAverage, second.FriendlyControlAverage);
        Assert.Equal(first.Objectives, second.Objectives);
        Assert.Equal(first.Identity, second.Identity);
        Assert.NotNull(first.Identity);
        Assert.NotEqual(
            first.Identity!.FriendlyFaction.DisplayName,
            first.Identity.HostileFaction.DisplayName);
        Assert.StartsWith("Operation ", first.Identity.OperationName);
        Assert.Contains(
            first.Objectives,
            item => item.Kind == StrategicObjectiveKind.GainSectorControl);
        Assert.Contains(
            first.Objectives,
            item => item.Kind == StrategicObjectiveKind.ImproveIntelligence);
    }

    [Fact]
    public void AdvanceTracksControlMomentumAndPressurePhase()
    {
        var initialWorld = World(control: 0.50, intelligence: 0.60);
        var campaign = ConflictCampaignDirector.Create("campaign-b", initialWorld);

        var improvedWorld = World(control: 0.64, intelligence: 0.70) with
        {
            UpdatedAt = Epoch.AddHours(2)
        };

        var advanced = ConflictCampaignDirector.Advance(
            campaign,
            improvedWorld);

        Assert.Equal(1, advanced.EvaluationSequence);
        Assert.True(advanced.FriendlyMomentum > 0);
        Assert.Equal(
            ConflictCampaignPhase.FriendlyPressure,
            advanced.Phase);
    }

    [Fact]
    public void AdvanceEvolvesPersistedFactionPosturesWhenPhaseChanges()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-phase",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.LogisticsFocused
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState pressuredWorld =
            World(control: 0.64, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(2)
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                pressuredWorld);

        Assert.Equal(
            ConflictCampaignPhase.FriendlyPressure,
            advanced.Phase);
        Assert.Equal(
            ConflictFactionOperationalPosture.Aggressive,
            advanced.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            advanced.Identity.HostileFaction.Posture);
        Assert.Equal(
            identity.OperationId,
            advanced.Identity.OperationId);
    }

    [Fact]
    public void ReturningToContestedPreservesLastEvolvedFactionPostures()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-contested",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.LogisticsFocused
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState pressuredWorld =
            World(control: 0.64, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1)
            };

        ConflictCampaignState pressured =
            ConflictCampaignDirector.Advance(
                campaign,
                pressuredWorld);

        Assert.Equal(
            ConflictFactionOperationalPosture.Aggressive,
            pressured.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            pressured.Identity.HostileFaction.Posture);

        ConflictWorldState contestedWorld =
            World(control: 0.50, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(2)
            };

        ConflictCampaignState contested =
            ConflictCampaignDirector.Advance(
                pressured,
                contestedWorld);

        Assert.Equal(
            ConflictCampaignPhase.Contested,
            contested.Phase);
        Assert.Equal(
            pressured.Identity,
            contested.Identity);
        Assert.Equal(
            ConflictFactionOperationalPosture.Aggressive,
            contested.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            contested.Identity.HostileFaction.Posture);
    }

    [Fact]
    public void HostilePressureMakesHostileAggressiveAndFriendlyDefensive()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-hostile-pressure",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.LogisticsFocused
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState pressuredWorld =
            World(control: 0.36, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1)
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                pressuredWorld);

        Assert.Equal(
            ConflictCampaignPhase.HostilePressure,
            advanced.Phase);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            advanced.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Aggressive,
            advanced.Identity.HostileFaction.Posture);
        Assert.Equal(
            identity.OperationId,
            advanced.Identity.OperationId);
    }

    [Fact]
    public void FriendlySecuredMakesFriendlyLogisticsFocusedAndHostileDefensive()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-friendly-secured",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.Aggressive
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState securedWorld =
            World(control: 0.82, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1),
                Units = World(0.82, 0.70).Units
                    .Select(unit => unit.Side == ConflictSide.Hostile
                        ? unit with
                        {
                            Strength = 0.10,
                            Readiness = 0.40
                        }
                        : unit)
                    .ToArray()
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                securedWorld);

        Assert.Equal(
            ConflictCampaignPhase.FriendlySecured,
            advanced.Phase);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            advanced.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            advanced.Identity.HostileFaction.Posture);
        Assert.Equal(
            identity.OperationId,
            advanced.Identity.OperationId);
    }

    [Fact]
    public void HostileSecuredMakesHostileLogisticsFocusedAndFriendlyDefensive()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-hostile-secured",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.Aggressive
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState securedWorld =
            World(control: 0.18, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1),
                Units = World(0.18, 0.70).Units
                    .Select(unit => unit.Side == ConflictSide.Friendly
                        ? unit with
                        {
                            Strength = 0.10,
                            Readiness = 0.40
                        }
                        : unit)
                    .ToArray()
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                securedWorld);

        Assert.Equal(
            ConflictCampaignPhase.HostileSecured,
            advanced.Phase);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            advanced.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            advanced.Identity.HostileFaction.Posture);
        Assert.Equal(
            identity.OperationId,
            advanced.Identity.OperationId);
    }

    [Fact]
    public void RepeatedFriendlySecuredEvaluationPreservesPosturesWhileOutcomeAdvances()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-secured-repeat",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.Aggressive
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState securedWorld =
            World(control: 0.82, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1),
                Units = World(0.82, 0.70).Units
                    .Select(unit => unit.Side == ConflictSide.Hostile
                        ? unit with
                        {
                            Strength = 0.10,
                            Readiness = 0.40
                        }
                        : unit)
                    .ToArray()
            };

        ConflictCampaignState first =
            ConflictCampaignDirector.Advance(
                campaign,
                securedWorld);

        Assert.Equal(
            ConflictCampaignPhase.FriendlySecured,
            first.Phase);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            first.Outcome);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            first.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            first.Identity.HostileFaction.Posture);

        ConflictCampaignState second =
            ConflictCampaignDirector.Advance(
                first,
                securedWorld with
                {
                    UpdatedAt = Epoch.AddHours(2)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Victory,
            second.Outcome);
        Assert.Equal(
            first.Identity,
            second.Identity);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            second.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            second.Identity.HostileFaction.Posture);
    }

    [Fact]
    public void RepeatedHostileSecuredEvaluationPreservesPosturesWhileOutcomeAdvances()
    {
        ConflictWorldState initialWorld =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-posture-hostile-secured-repeat",
                initialWorld);

        ConflictCampaignIdentity identity =
            campaign.Identity! with
            {
                FriendlyFaction =
                    campaign.Identity!.FriendlyFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.Aggressive
                    },
                HostileFaction =
                    campaign.Identity.HostileFaction with
                    {
                        Posture =
                            ConflictFactionOperationalPosture.AirFocused
                    }
            };

        campaign = campaign with { Identity = identity };

        ConflictWorldState securedWorld =
            World(control: 0.18, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddHours(1),
                Units = World(0.18, 0.70).Units
                    .Select(unit => unit.Side == ConflictSide.Friendly
                        ? unit with
                        {
                            Strength = 0.10,
                            Readiness = 0.40
                        }
                        : unit)
                    .ToArray()
            };

        ConflictCampaignState first =
            ConflictCampaignDirector.Advance(
                campaign,
                securedWorld);

        Assert.Equal(
            ConflictCampaignPhase.HostileSecured,
            first.Phase);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            first.Outcome);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            first.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            first.Identity.HostileFaction.Posture);

        ConflictCampaignState second =
            ConflictCampaignDirector.Advance(
                first,
                securedWorld with
                {
                    UpdatedAt = Epoch.AddHours(2)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Defeat,
            second.Outcome);
        Assert.Equal(
            first.Identity,
            second.Identity);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            second.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.LogisticsFocused,
            second.Identity.HostileFaction.Posture);
    }

    [Fact]
    public void AdvanceBackfillsIdentityForLegacyCampaignState()
    {
        var world = World(0.50, 0.50);
        var current = ConflictCampaignDirector.Create(
            "campaign-legacy",
            world) with
        {
            Identity = null
        };

        current.Validate();

        var advancedWorld = world with
        {
            UpdatedAt = Epoch.AddMinutes(30)
        };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                current,
                advancedWorld);

        Assert.NotNull(advanced.Identity);
        Assert.Equal(
            ConflictCampaignIdentityGenerator.Create(
                current.CampaignId,
                advancedWorld),
            advanced.Identity);
    }

    [Fact]
    public void LegacyIdentityBackfillAppliesPressurePhasePostures()
    {
        ConflictWorldState world =
            World(control: 0.50, intelligence: 0.60);

        ConflictCampaignState current =
            ConflictCampaignDirector.Create(
                "campaign-legacy-pressure",
                world) with
            {
                Identity = null
            };

        current.Validate();

        ConflictWorldState pressuredWorld =
            World(control: 0.64, intelligence: 0.70) with
            {
                UpdatedAt = Epoch.AddMinutes(30)
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                current,
                pressuredWorld);

        Assert.Equal(
            ConflictCampaignPhase.FriendlyPressure,
            advanced.Phase);
        Assert.NotNull(advanced.Identity);
        Assert.Equal(
            ConflictFactionOperationalPosture.Aggressive,
            advanced.Identity!.FriendlyFaction.Posture);
        Assert.Equal(
            ConflictFactionOperationalPosture.Defensive,
            advanced.Identity.HostileFaction.Posture);
        Assert.Equal(
            ConflictCampaignIdentityGenerator.Create(
                current.CampaignId,
                pressuredWorld).OperationId,
            advanced.Identity.OperationId);
    }

    [Fact]
    public void SecuredCampaignRequiresTwoEvaluationsBeforeVictory()
    {
        ConflictWorldState world = World(0.82, 0.70) with
        {
            Units = World(0.82, 0.70).Units
                .Select(unit => unit.Side == ConflictSide.Hostile
                    ? unit with
                    {
                        Strength = 0.10,
                        Readiness = 0.40
                    }
                    : unit)
                .ToArray()
        };

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-victory",
                world);

        Assert.Equal(
            ConflictCampaignPhase.FriendlySecured,
            campaign.Phase);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            campaign.Outcome);

        ConflictCampaignState first =
            ConflictCampaignDirector.Advance(
                campaign,
                world with
                {
                    UpdatedAt = Epoch.AddHours(1)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            first.Outcome);
        Assert.Equal(1, first.SecuredEvaluationCount);

        ConflictCampaignState second =
            ConflictCampaignDirector.Advance(
                first,
                world with
                {
                    UpdatedAt = Epoch.AddHours(2)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Victory,
            second.Outcome);
        Assert.True(second.IsTerminal);
        Assert.Empty(second.Objectives);
    }

    [Fact]
    public void SecuredHostileCampaignEndsInDefeatAfterTwoEvaluations()
    {
        ConflictWorldState world = World(0.18, 0.70) with
        {
            Units = World(0.18, 0.70).Units
                .Select(unit => unit.Side == ConflictSide.Friendly
                    ? unit with
                    {
                        Strength = 0.10,
                        Readiness = 0.40
                    }
                    : unit)
                .ToArray()
        };

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-defeat",
                world);

        Assert.Equal(
            ConflictCampaignPhase.HostileSecured,
            campaign.Phase);

        ConflictCampaignState first =
            ConflictCampaignDirector.Advance(
                campaign,
                world with
                {
                    UpdatedAt = Epoch.AddHours(1)
                });

        ConflictCampaignState second =
            ConflictCampaignDirector.Advance(
                first,
                world with
                {
                    UpdatedAt = Epoch.AddHours(2)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Defeat,
            second.Outcome);
        Assert.True(second.IsTerminal);
        Assert.Empty(second.Objectives);
    }

    [Fact]
    public void ProlongedBalancedCampaignEndsInStalemate()
    {
        ConflictWorldState world = World(0.50, 0.70);
        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-stalemate",
                world) with
            {
                StableEvaluationCount = 7,
                EvaluationSequence = 7,
                FriendlyMomentum = 0
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                world with
                {
                    UpdatedAt = Epoch.AddHours(1)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Stalemate,
            advanced.Outcome);
        Assert.True(advanced.IsTerminal);
    }

    [Fact]
    public void MutualExhaustionCanEndInCeasefire()
    {
        ConflictWorldState world = World(0.50, 0.70) with
        {
            Units = World(0.50, 0.70).Units
                .Select(unit => unit with
                {
                    Strength = 0.20,
                    Readiness = 0.40
                })
                .ToArray()
        };

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-ceasefire",
                world) with
            {
                EvaluationSequence = 3
            };

        ConflictCampaignState advanced =
            ConflictCampaignDirector.Advance(
                campaign,
                world with
                {
                    UpdatedAt = Epoch.AddHours(1)
                });

        Assert.Equal(
            ConflictCampaignOutcome.Ceasefire,
            advanced.Outcome);
        Assert.True(advanced.IsTerminal);
    }

    [Fact]
    public void TelemetrySynchronizationDoesNotCountAsCampaignEvaluation()
    {
        ConflictWorldState world = World(0.50, 0.70);
        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "campaign-sync",
                world) with
            {
                StableEvaluationCount = 4,
                EvaluationSequence = 6
            };

        ConflictCampaignState synchronized =
            ConflictCampaignDirector.Advance(
                campaign,
                world with
                {
                    UpdatedAt = Epoch.AddMinutes(5)
                },
                evaluateCampaignOutcome: false);

        Assert.Equal(
            campaign.EvaluationSequence,
            synchronized.EvaluationSequence);
        Assert.Equal(
            campaign.StableEvaluationCount,
            synchronized.StableEvaluationCount);
        Assert.Equal(
            ConflictCampaignOutcome.Ongoing,
            synchronized.Outcome);
        Assert.Equal(
            Epoch.AddMinutes(5),
            synchronized.UpdatedAt);
    }

    [Fact]
    public void CampaignCannotAdvanceWithAnotherTheater()
    {
        var campaign = ConflictCampaignDirector.Create(
            "campaign-c",
            World(0.50, 0.50));

        var other = World(0.50, 0.50) with
        {
            TheaterId = "FICTIONAL-OTHER"
        };

        Assert.Throws<InvalidOperationException>(
            () => ConflictCampaignDirector.Advance(campaign, other));
    }

    private static ConflictWorldState World(
        double control,
        double intelligence)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("71000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Infantry,
            new GeoPoint(35.0, -97.0),
            Strength: 0.85,
            Readiness: 0.60,
            Pressure: 0.30,
            IsMobile: true);

        var hostile = new GroundUnitState(
            Guid.Parse("72000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.AirDefense,
            new GeoPoint(35.05, -96.95),
            Strength: 0.75,
            Readiness: 0.80,
            Pressure: 0.10,
            IsMobile: false);

        var threat = new ThreatState(
            Guid.Parse("73000000-0000-0000-0000-000000000001"),
            hostile.UnitId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostile.Position,
            RadiusNauticalMiles: 14,
            Severity: 0.60,
            Active: true);

        return ConflictWorldState.Create(
            "FICTIONAL-CAMPAIGN",
            0x123456UL,
            Epoch,
            new[] { friendly, hostile },
            new[]
            {
                new ConflictSectorState(
                    "CAMPAIGN-S1",
                    new GeoPoint(35.02, -96.98),
                    control,
                    intelligence)
            },
            new[] { threat });
    }
}
