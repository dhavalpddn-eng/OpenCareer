using OpenCareer.Domain.Careers;
using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public class RegionalSecurityJobMarketTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JobMarketDestination[] Destinations =
    [
        new("KALB", 80, EstimatedFlightHours: 0.8),
        new("KBOS", 220, EstimatedFlightHours: 1.9),
        new("KPHL", 260, EstimatedFlightHours: 2.2),
        new("KIAD", 300, EstimatedFlightHours: 2.6),
        new("KORD", 550, EstimatedFlightHours: 4.0)
    ];

    [Fact]
    public void SevereActiveConflictSuppressesCivilianTracksAndBoostsMilitary()
    {
        var conflict = new RegionalSecurityState(
            "region-test",
            RegionalSecurityPhase.ActiveConflict,
            Severity: 0.95,
            RegionalSecuritySource.Simulated,
            Epoch,
            Epoch);

        var offers = GenerateAcrossBuckets(conflict, 300).ToArray();

        Assert.NotEmpty(offers);
        Assert.DoesNotContain(offers, x => x.ServiceTrack is
            ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract);
        Assert.True(
            Share(offers, ServiceTrack.MilitaryService) > Share(offers, ServiceTrack.GovernmentContract),
            "Severe active conflict should make military work the dominant eligible track at this mixed defense airport.");
    }

    [Fact]
    public void ActiveConflictCanProduceTroopMovementAndReconnaissanceScenarios()
    {
        var conflict = new RegionalSecurityState(
            "region-test",
            RegionalSecurityPhase.ActiveConflict,
            Severity: 0.85,
            RegionalSecuritySource.Simulated,
            Epoch,
            Epoch);

        var offers = GenerateAcrossBuckets(conflict, 900).ToArray();

        Assert.Contains(offers, x => x.Scenario == JobScenarioKind.TroopMovement);
        Assert.Contains(offers, x => x.Scenario == JobScenarioKind.ConflictReconnaissance);
        Assert.Contains(offers, x => x.ServiceTrack == ServiceTrack.MilitaryService);
    }

    [Fact]
    public void CeasefireAndRecoveryRequireImmediateBoardRefresh()
    {
        var conflict = new RegionalSecurityState(
            "region-test",
            RegionalSecurityPhase.ActiveConflict,
            Severity: 0.75,
            RegionalSecuritySource.Simulated,
            Epoch,
            Epoch);
        var ceasefire = conflict.TransitionTo(
            RegionalSecurityPhase.Ceasefire,
            0.45,
            Epoch.AddDays(2),
            RegionalSecuritySource.Simulated);
        var recovery = ceasefire.TransitionTo(
            RegionalSecurityPhase.Recovery,
            0.25,
            Epoch.AddDays(5),
            RegionalSecuritySource.Simulated);

        Assert.True(ceasefire.RequiresImmediateBoardRefreshFrom(conflict));
        Assert.True(recovery.RequiresImmediateBoardRefreshFrom(ceasefire));
        Assert.True(recovery.TrackMultiplier(ServiceTrack.CivilianEmployment) > 1);
        Assert.True(recovery.KindMultiplier(ContractKind.Cargo) > 1);
        Assert.True(recovery.KindMultiplier(ContractKind.DisasterRelief) > recovery.KindMultiplier(ContractKind.Passenger));
    }

    [Fact]
    public void RecoveryGeneratesRecoveryAndInfrastructureScenarios()
    {
        var recovery = new RegionalSecurityState(
            "region-test",
            RegionalSecurityPhase.Recovery,
            Severity: 0.30,
            RegionalSecuritySource.Simulated,
            Epoch,
            Epoch);

        var offers = GenerateAcrossBuckets(recovery, 1200).ToArray();

        Assert.Contains(offers, x => x.Scenario == JobScenarioKind.RecoverySupply);
        Assert.Contains(offers, x => x.Scenario == JobScenarioKind.InfrastructureAssessment);
        Assert.Contains(offers, x => x.ServiceTrack is
            ServiceTrack.CivilianEmployment or ServiceTrack.IndependentContract or ServiceTrack.CompanyContract);
    }

    [Fact]
    public void CuratedLiveSignalRequiresSourceProvenance()
    {
        var invalid = new RegionalSecurityState(
            "region-test",
            RegionalSecurityPhase.ElevatedTension,
            0.4,
            RegionalSecuritySource.CuratedLiveSignal,
            Epoch,
            Epoch);
        Assert.Throws<ArgumentException>(invalid.Validate);

        var valid = invalid with { SourceReference = "source:example:2026-09-17" };
        valid.Validate();
    }

    [Fact]
    public void InvalidSecurityTransitionIsRejected()
    {
        var normal = RegionalSecurityState.Normal("region-test", Epoch);
        Assert.Throws<InvalidOperationException>(() => normal.TransitionTo(
            RegionalSecurityPhase.Recovery,
            0.2,
            Epoch.AddHours(1),
            RegionalSecuritySource.Simulated));
    }

    private static IEnumerable<JobMarketOfferDraft> GenerateAcrossBuckets(
        RegionalSecurityState security,
        int buckets)
    {
        var policy = JobMarketPolicy.Default;
        var standing = new CareerLevelSnapshot(35, 100_000, 120_000);
        var capacity = AirportMarketCapacity.ForScale(AirportMarketScale.MajorHub);

        for (var i = 0; i < buckets; i++)
        {
            var time = Epoch.AddTicks(policy.RefreshInterval.Ticks * i);
            var request = new JobMarketGenerationRequest(
                CareerSeed: 919191,
                Time: time,
                Origin: InitialAirportProfiles.GriffissInternational,
                Destinations: Destinations,
                Access: JobMarketAccess.All,
                Policy: policy,
                CareerStanding: standing,
                Capacity: capacity,
                SecurityState: security with { UpdatedAt = time });

            foreach (var offer in JobMarketGenerator.Generate(request))
                yield return offer;
        }
    }

    private static double Share(IReadOnlyCollection<JobMarketOfferDraft> offers, ServiceTrack track) =>
        offers.Count == 0 ? 0 : offers.Count(x => x.ServiceTrack == track) / (double)offers.Count;
}
