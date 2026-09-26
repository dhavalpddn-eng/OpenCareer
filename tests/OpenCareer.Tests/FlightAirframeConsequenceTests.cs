using System.Collections.Immutable;
using OpenCareer.Domain.Aircraft;
using OpenCareer.Domain.Flights;

namespace OpenCareer.Tests;

public sealed class FlightAirframeConsequenceTests
{
    [Theory]
    [InlineData(-899, FlightDamageSeverity.Normal, AirframeDamageState.None)]
    [InlineData(-900, FlightDamageSeverity.ElevatedWear, AirframeDamageState.None)]
    [InlineData(-1299, FlightDamageSeverity.ElevatedWear, AirframeDamageState.None)]
    [InlineData(-1300, FlightDamageSeverity.MinorDamage, AirframeDamageState.Recorded)]
    [InlineData(-1799, FlightDamageSeverity.MinorDamage, AirframeDamageState.Recorded)]
    [InlineData(-1800, FlightDamageSeverity.MajorDamage, AirframeDamageState.Recorded)]
    [InlineData(-2500, FlightDamageSeverity.MajorDamage, AirframeDamageState.Recorded)]
    [InlineData(2500, FlightDamageSeverity.Normal, AirframeDamageState.None)]
    public void SignedContactDescentUsesEstablishedGameplayBands(double verticalSpeed, FlightDamageSeverity severity, AirframeDamageState damage)
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(verticalSpeeds: [verticalSpeed]));
        Assert.Equal(severity, result.Severity);
        Assert.Equal(damage, result.ApplyTo(new(0, AirframeDamageState.None)).Damage);
        Assert.Contains("not measured impact force", result.Rationale);
    }

    [Fact]
    public void TwoBouncesRetainOneCycleAndStrongestContactWithoutAdditionalStructuralWear()
    {
        var session = ConsequenceFixture.Session(verticalSpeeds: [-950, -1900, -1400]);
        var result = FlightAirframeConsequenceCalculator.Calculate(session);
        Assert.Equal(1, result.Summary.LandingEpisodeCount);
        Assert.Equal(2, result.Summary.BounceCount);
        Assert.Single(result.Summary.LandingEpisodes);
        Assert.Equal(3, result.Summary.LandingEpisodes[0].EffectiveContacts.Count);
        Assert.Equal(session.EffectiveLandingEpisodes[0].EffectiveContacts[1], result.StrongestContact);
        Assert.Equal(FlightDamageSeverity.MajorDamage, result.Severity);
        Assert.Equal(0.0016, result.RoutineStructuralWearFraction, 12);
    }

    [Fact]
    public void GIsRetainedWithoutInventingAGThreshold()
    {
        var session = ConsequenceFixture.Session(verticalSpeeds: [-500]);
        var contact = session.EffectiveLandingEpisodes[0].EffectiveContacts[0] with { NormalAccelerationG = 20 };
        session = session with { LandingEpisodes = [session.EffectiveLandingEpisodes[0] with { Contacts = [contact] }] };
        var result = FlightAirframeConsequenceCalculator.Calculate(session);
        Assert.Equal(20, result.StrongestContact!.NormalAccelerationG);
        Assert.Equal(FlightDamageSeverity.Normal, result.Severity);
        Assert.Equal(AirframeDamageState.None, result.ApplyTo(new(0, AirframeDamageState.None)).Damage);
    }

    [Fact]
    public void UnknownLandingEvidenceIsNotFabricatedButTrustedUsageStillAccrues()
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session());
        Assert.Null(result.StrongestContact);
        Assert.Null(result.Severity);
        Assert.Equal(0.0016, result.RoutineStructuralWearFraction, 12);
        Assert.Contains("unknown", result.Rationale);
    }

    [Theory]
    [InlineData(FlightSessionStatus.Completed)]
    [InlineData(FlightSessionStatus.Cancelled)]
    [InlineData(FlightSessionStatus.Interrupted)]
    public void ReportedCrashIsSevereWithoutLandingEvidence(FlightSessionStatus status)
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(status: status, crash: true));
        Assert.Equal(FlightDamageSeverity.Severe, result.Severity);
        Assert.Equal(AirframeDamageState.Grounding, result.ApplyTo(new(0, AirframeDamageState.None)).Damage);
    }

    [Fact]
    public void NonCrashInterruptionRetainsExplicitNoMutationDecisionEvenWithPriorContacts()
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(
            status: FlightSessionStatus.Interrupted, verticalSpeeds: [-2000]));
        var before = new AirframeCondition(0.4, AirframeDamageState.Recorded);
        Assert.False(result.ApplyCondition);
        Assert.Null(result.Severity);
        Assert.NotNull(result.StrongestContact);
        Assert.Equal(0, result.RoutineStructuralWearFraction);
        Assert.Same(before, result.ApplyTo(before));
        Assert.Contains("continuity is uncertain", result.Rationale);
    }

    [Fact]
    public void RoutineWearClampsAndNeverDowngradesDiscreteDamage()
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(verticalSpeeds: [-500]));
        Assert.Equal(new AirframeCondition(1, AirframeDamageState.Grounding), result.ApplyTo(new(0.9999, AirframeDamageState.Grounding)));
        Assert.Equal(AirframeDamageState.Recorded, result.ApplyTo(new(0, AirframeDamageState.Recorded)).Damage);
        var calibration = result.Calibration;
        Assert.Equal(900, calibration.ElevatedDescentFpm);
        Assert.Equal(1300, calibration.MinorDamageDescentFpm);
        Assert.Equal(1800, calibration.MajorDamageDescentFpm);
        Assert.Equal(0.0008, calibration.StructuralWearFractionPerAirborneHour);
    }

    [Fact]
    public void PreflightCancellationHasNoWearOrInventedLanding()
    {
        var session = ConsequenceFixture.Session(status: FlightSessionStatus.Cancelled) with { TimeLedger = FlightTimeLedger.Empty };
        var result = FlightAirframeConsequenceCalculator.Calculate(session);
        Assert.Null(result.Severity);
        Assert.Equal(0, result.RoutineStructuralWearFraction);
        Assert.Equal(new AirframeCondition(0.2, AirframeDamageState.None), result.ApplyTo(new(0.2, AirframeDamageState.None)));
    }

    [Fact]
    public void ActiveOrModelOnlyFlightCannotProducePhysicalConsequence()
    {
        Assert.Throws<InvalidOperationException>(() => FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(status: FlightSessionStatus.Active)));
        Assert.Throws<InvalidOperationException>(() => FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session() with { AircraftIdentity = new(ConsequenceFixture.Model) }));
    }

    [Fact]
    public void DecisionTamperingAndUnorderedOrUncountedContactsFailClosed()
    {
        var result = FlightAirframeConsequenceCalculator.Calculate(ConsequenceFixture.Session(verticalSpeeds: [-1500, -500]));
        Assert.Throws<ArgumentException>(() => (result with { Severity = FlightDamageSeverity.Normal }).Validate());
        Assert.Throws<ArgumentException>(() => (result with { RoutineStructuralWearFraction = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (result.Summary with { LandingEpisodeCount = 0 }).Validate());
        var episode = result.Summary.LandingEpisodes[0];
        Assert.Throws<ArgumentException>(() => (result.Summary with { LandingEpisodes = [episode with { Contacts = episode.EffectiveContacts.Reverse().ToImmutableList() }] }).Validate());
    }
}

internal static class ConsequenceFixture
{
    internal const string Model = AircraftCanonicalIdentity.Cessna172SkyhawkAircraftId;
    internal static readonly DateTimeOffset Epoch = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    // Normalized terminal fixture, never production telemetry. Existing reducer/contact tests prove
    // how these episodes are admitted; here we test their deterministic persisted consequences.
    internal static FlightSession Session(AirframeId? airframe = null, FlightSessionStatus status = FlightSessionStatus.Completed,
        bool crash = false, params double[] verticalSpeeds)
    {
        var session = FlightSession.Start(Epoch, Guid.NewGuid(), aircraftIdentity: new(Model, airframe ?? new AirframeId(Guid.NewGuid())));
        var contacts = verticalSpeeds.Select((vs, i) => new FlightLandingContactEvidence(
            Epoch.AddHours(2).AddSeconds(i * 3), vs, 1.2, 60, 55, 310, 2, 1, 150, 340)).ToImmutableList();
        return session with
        {
            Status = status,
            UpdatedAt = Epoch.AddHours(3),
            OperationState = status == FlightSessionStatus.Completed ? FlightOperationState.Complete : session.OperationState,
            Tracking = session.Tracking with
            {
                State = status == FlightSessionStatus.Completed ? FlightTrackingState.Complete : FlightTrackingState.Airborne,
                UpdatedAt = Epoch.AddHours(3), TakeoffCount = 1,
                LandingEpisodeCount = contacts.Count == 0 ? 0 : 1,
                BounceCount = Math.Max(0, contacts.Count - 1), CrashReported = crash
            },
            TimeLedger = FlightTimeLedger.Empty with { AirborneTime = TimeSpan.FromHours(2), BlockTime = TimeSpan.FromHours(2.5) },
            LandingEpisodes = contacts.Count == 0 ? [] : [new(1, contacts[0].Timestamp.AddSeconds(1), FlightSessionLandingKind.FullStop,
                contacts.Count - 1, Epoch.AddHours(2.1), contacts)]
        };
    }

    internal static FlightSession SessionWithLandingEpisodes(AirframeId airframe, params double[][] verticalSpeedsByEpisode)
    {
        if (verticalSpeedsByEpisode.Length == 0 || verticalSpeedsByEpisode.Any(speeds => speeds.Length == 0))
            throw new ArgumentException("At least one contact is required for every fixture landing episode.", nameof(verticalSpeedsByEpisode));

        var session = Session(airframe);
        var episodes = verticalSpeedsByEpisode.Select((speeds, episodeIndex) =>
        {
            DateTimeOffset episodeStart = Epoch.AddHours(2).AddMinutes(episodeIndex * 10);
            var contacts = speeds.Select((verticalSpeed, contactIndex) => new FlightLandingContactEvidence(
                episodeStart.AddSeconds(contactIndex * 3), verticalSpeed, 1.2, 60, 55, 310, 2, 1, 150, 340)).ToImmutableList();
            return new FlightSessionLandingEpisode(episodeIndex + 1, contacts[0].Timestamp.AddSeconds(1),
                episodeIndex == verticalSpeedsByEpisode.Length - 1 ? FlightSessionLandingKind.FullStop : FlightSessionLandingKind.TouchAndGo,
                contacts.Count - 1, contacts[^1].Timestamp.AddSeconds(2), contacts);
        }).ToArray();
        return session with
        {
            Tracking = session.Tracking with
            {
                LandingEpisodeCount = episodes.Length,
                BounceCount = episodes.Sum(episode => episode.BounceCount),
                TouchAndGoCount = Math.Max(0, episodes.Length - 1)
            },
            LandingEpisodes = episodes
        };
    }
}
