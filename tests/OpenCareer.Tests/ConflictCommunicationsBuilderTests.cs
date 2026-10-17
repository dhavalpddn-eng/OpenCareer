using OpenCareer.Application.Military;
using OpenCareer.Domain.Conflict;
using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class ConflictCommunicationsBuilderTests
{
    private static readonly DateTimeOffset Epoch =
        new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SameSnapshotProducesSameOrderedCommunications()
    {
        ConflictOperationsSnapshot snapshot =
            Snapshot(
                SupportUrgency.Immediate,
                threatSeverity: 0.80);

        ConflictCommunicationEntry[] first =
            ConflictCommunicationsBuilder.Build(snapshot);

        ConflictCommunicationEntry[] second =
            ConflictCommunicationsBuilder.Build(snapshot);

        Assert.Equal(first, second);
        Assert.True(first.Length >= 3);

        ConflictCommunicationEntry dispatch = first.First(
            entry => entry.Channel
                == ConflictCommunicationChannel.Dispatch);

        ConflictCommunicationEntry intel = first.First(
            entry => entry.Channel
                == ConflictCommunicationChannel.Intelligence);

        Assert.Equal(
            ConflictCommunicationPriority.Immediate,
            dispatch.Priority);
        Assert.Equal(
            ConflictCommunicationPriority.Immediate,
            intel.Priority);
        Assert.True(
            Array.IndexOf(first, dispatch)
            < Array.IndexOf(first, intel));

        Assert.Contains("08:30 UTC", dispatch.Message);
        Assert.Contains("80", intel.Message);
        Assert.All(first, entry => entry.Validate());
    }

    [Fact]
    public void SupportUrgencyMapsToCommunicationPriority()
    {
        ConflictCommunicationEntry routine =
            ConflictCommunicationsBuilder
                .Build(Snapshot(SupportUrgency.Routine, 0.20))
                .Single(entry => entry.Channel
                    == ConflictCommunicationChannel.Dispatch);

        ConflictCommunicationEntry priority =
            ConflictCommunicationsBuilder
                .Build(Snapshot(SupportUrgency.Priority, 0.20))
                .Single(entry => entry.Channel
                    == ConflictCommunicationChannel.Dispatch);

        ConflictCommunicationEntry immediate =
            ConflictCommunicationsBuilder
                .Build(Snapshot(SupportUrgency.Immediate, 0.20))
                .Single(entry => entry.Channel
                    == ConflictCommunicationChannel.Dispatch);

        Assert.Equal(
            ConflictCommunicationPriority.Advisory,
            routine.Priority);
        Assert.Equal(
            ConflictCommunicationPriority.Priority,
            priority.Priority);
        Assert.Equal(
            ConflictCommunicationPriority.Immediate,
            immediate.Priority);
    }

    [Fact]
    public void TerminalCampaignProducesCommandConclusionWithoutInventedEvents()
    {
        ConflictOperationsSnapshot snapshot =
            Snapshot(
                SupportUrgency.Routine,
                threatSeverity: 0.20,
                terminal: true);

        ConflictCommunicationEntry[] messages =
            ConflictCommunicationsBuilder.Build(snapshot);

        ConflictCommunicationEntry command =
            messages.Single(entry => entry.Channel
                == ConflictCommunicationChannel.Command);

        Assert.Equal(
            ConflictCommunicationPriority.Priority,
            command.Priority);
        Assert.Contains(
            "concluded with victory",
            command.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            messages,
            entry => entry.Message.Contains(
                "destroyed",
                StringComparison.OrdinalIgnoreCase));
    }

    private static ConflictOperationsSnapshot Snapshot(
        SupportUrgency urgency,
        double threatSeverity,
        bool terminal = false)
    {
        var friendly = new GroundUnitState(
            Guid.Parse("f1000000-0000-0000-0000-000000000001"),
            ConflictSide.Friendly,
            GroundUnitRole.Command,
            new GeoPoint(35.00, -97.00),
            Strength: 0.85,
            Readiness: 0.80,
            Pressure: 0.20,
            IsMobile: false);

        var hostile = new GroundUnitState(
            Guid.Parse("f2000000-0000-0000-0000-000000000001"),
            ConflictSide.Hostile,
            GroundUnitRole.Armor,
            new GeoPoint(35.10, -96.90),
            Strength: 0.80,
            Readiness: 0.75,
            Pressure: 0.20,
            IsMobile: true);

        var threat = new ThreatState(
            Guid.Parse("f3000000-0000-0000-0000-000000000001"),
            hostile.UnitId,
            ConflictSide.Hostile,
            AirThreatType.AirDefense,
            hostile.Position,
            RadiusNauticalMiles: 28,
            Severity: threatSeverity,
            Active: true);

        ConflictWorldState world =
            ConflictWorldState.Create(
                "FICTIONAL-COMMS",
                theaterSeed: 0xC0115UL,
                updatedAt: Epoch,
                units: new[] { friendly, hostile },
                sectors: new[]
                {
                    new ConflictSectorState(
                        "COMMS-S1",
                        new GeoPoint(35.05, -96.95),
                        FriendlyControl: 0.52,
                        IntelligenceConfidence: 0.70)
                },
                threats: new[] { threat });

        var request = new AirSupportRequest(
            "comms-support-1",
            SupportRequestType.CloseAirSupport,
            urgency,
            friendly.UnitId,
            hostile.UnitId,
            hostile.Position,
            RequiredEffect: 0.20,
            CreatedAt: Epoch,
            ExpiresAt: Epoch.AddMinutes(30));

        world = world with
        {
            SupportRequests = new[] { request }
        };

        ConflictCampaignState campaign =
            ConflictCampaignDirector.Create(
                "comms-campaign",
                world);

        if (terminal)
        {
            campaign = campaign with
            {
                Outcome = ConflictCampaignOutcome.Victory,
                Objectives =
                    Array.Empty<ConflictStrategicObjective>()
            };
        }

        ConflictCampaignCheckpoint checkpoint =
            ConflictCampaignCheckpoint.Create(
                "comms-campaign",
                world,
                MilitaryCareerState.Civilian,
                PlayerCombatState.Undamaged,
                combatSupportMissions: null,
                areaSupportMissions: null,
                airOperationMissions: null,
                savedAt: Epoch,
                campaignState: campaign);

        return ConflictOperationsSnapshotBuilder.Build(
            checkpoint);
    }
}
