using OpenCareer.Domain.Events;

namespace OpenCareer.Tests;

public class WorldFeedModelTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExternalSignalsRequireProvenanceBeforeUse()
    {
        var signal = new WorldSignal(
            Guid.NewGuid(),
            WorldSignalType.PassengerTrend,
            "city:MIA",
            0.8,
            Epoch,
            Epoch.AddMinutes(5),
            Epoch.AddHours(6),
            WorldSignalSourceKind.PublicWebSource,
            WorldSignalValidationStatus.Pending,
            0);

        Assert.Throws<ArgumentException>(signal.Validate);

        signal = signal with
        {
            SourceReference = "source:example",
            SourcePublisher = "Example Publisher"
        };
        signal.Validate();
        Assert.False(signal.IsUsableAt(Epoch.AddHours(1)));

        signal = signal.MarkValidated(0.8);
        Assert.True(signal.IsUsableAt(Epoch.AddHours(1)));
    }

    [Fact]
    public void TrendImpactIsBoundedAndRequiresValidatedUnexpiredSignal()
    {
        var positive = new WorldSignal(
            Guid.NewGuid(),
            WorldSignalType.CargoTrend,
            "airport:KMEM",
            1,
            Epoch,
            Epoch,
            Epoch.AddHours(4),
            WorldSignalSourceKind.DeterministicSimulation,
            WorldSignalValidationStatus.Validated,
            1);
        positive.Validate();

        Assert.Equal(1.25, WorldSignalMarketMapper.DemandMultiplier(positive, Epoch.AddHours(1)), 10);
        Assert.Equal(1, WorldSignalMarketMapper.DemandMultiplier(positive, Epoch.AddHours(5)), 10);

        var negative = positive with { SignalId = Guid.NewGuid(), Magnitude = -1, Confidence = 0.5 };
        Assert.Equal(0.875, WorldSignalMarketMapper.DemandMultiplier(negative, Epoch.AddHours(1)), 10);
    }

    [Fact]
    public void NonMarketSignalDoesNotDirectlyChangeDemand()
    {
        var security = new WorldSignal(
            Guid.NewGuid(),
            WorldSignalType.RegionalSecurity,
            "region:test",
            1,
            Epoch,
            Epoch,
            Epoch.AddHours(4),
            WorldSignalSourceKind.DeterministicSimulation,
            WorldSignalValidationStatus.Validated,
            1);

        Assert.Equal(1, WorldSignalMarketMapper.DemandMultiplier(security, Epoch.AddHours(1)));
    }

    [Fact]
    public void AiGeneratedPostRequiresDisclosure()
    {
        var post = new WorldFeedPost(
            Guid.NewGuid(),
            Epoch,
            WorldFeedPostCategory.Market,
            "Cargo demand is rising",
            "Express freight pressure is building in the regional market.",
            IsAiGenerated: true);
        Assert.Throws<ArgumentException>(post.Validate);

        (post with { SourceDisclosure = "AI summary of validated OpenCareer world signals." }).Validate();
    }

    [Fact]
    public void RejectedSignalCannotBeSilentlyRevalidated()
    {
        var signal = new WorldSignal(
            Guid.NewGuid(),
            WorldSignalType.CargoTrend,
            "airport:KMEM",
            0.4,
            Epoch,
            Epoch,
            Epoch.AddHours(4),
            WorldSignalSourceKind.DeterministicSimulation,
            WorldSignalValidationStatus.Pending,
            0);
        signal.Validate();

        var rejected = signal.MarkRejected();
        Assert.Throws<InvalidOperationException>(() => rejected.MarkValidated(0.8));
    }
}
