using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class OperationResolutionIdempotencyTests
{
    [Fact]
    public void ResolutionKeyIsStableForSameOperationAndMission()
    {
        Guid missionId =
            Guid.Parse("09cbef2f-ef42-495e-9ae7-3a1d615752ef");

        OperationResolutionKey first =
            OperationResolutionKey.Create("support-004", missionId);
        OperationResolutionKey second =
            OperationResolutionKey.Create("support-004", missionId);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(
            "support-004:09cbef2fef42495e9ae73a1d615752ef",
            first.ToString());
    }

    [Fact]
    public void ResolutionKeyChangesWhenOperationIdentityChanges()
    {
        Guid firstMission =
            Guid.Parse("09cbef2f-ef42-495e-9ae7-3a1d615752ef");
        Guid secondMission =
            Guid.Parse("b80b83cc-574a-4f5e-81b7-fd0283919358");

        OperationResolutionKey first =
            OperationResolutionKey.Create("support-004", firstMission);
        OperationResolutionKey differentOperation =
            OperationResolutionKey.Create("support-005", firstMission);
        OperationResolutionKey differentMission =
            OperationResolutionKey.Create("support-004", secondMission);

        Assert.NotEqual(first, differentOperation);
        Assert.NotEqual(first, differentMission);
    }

    [Fact]
    public void RepeatedCompletionEventIsRejected()
    {
        var resolver = new IdempotentOperationResolver(
            new OperationResolver(),
            new InMemoryOperationResolutionRegistry());
        OperationResolutionInput input = ValidInput();

        OperationOutcome first = resolver.Resolve(input);

        var exception =
            Assert.Throws<DuplicateOperationResolutionException>(
                () => resolver.Resolve(input));

        Assert.Equal(
            OperationResolutionKey.Create(
                input.OperationId,
                input.MissionId),
            exception.ResolutionKey);
        Assert.Equal(OperationOutcomeStatus.Success, first.Status);
    }

    [Fact]
    public void DifferentOperationCanStillResolve()
    {
        var resolver = new IdempotentOperationResolver(
            new OperationResolver(),
            new InMemoryOperationResolutionRegistry());

        OperationOutcome first =
            resolver.Resolve(ValidInput());

        OperationOutcome second =
            resolver.Resolve(
                ValidInput() with
                {
                    OperationId = "support-005",
                    MissionId = Guid.Parse(
                        "b80b83cc-574a-4f5e-81b7-fd0283919358")
                });

        Assert.Equal(OperationOutcomeStatus.Success, first.Status);
        Assert.Equal(OperationOutcomeStatus.Success, second.Status);
    }

    [Fact]
    public void FailedResolutionReleasesReservationForRetry()
    {
        var registry = new InMemoryOperationResolutionRegistry();
        var resolver = new IdempotentOperationResolver(
            new ThrowOnceResolver(),
            registry);
        OperationResolutionInput input = ValidInput();

        Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(input));

        OperationOutcome retried = resolver.Resolve(input);

        Assert.Equal(OperationOutcomeStatus.Success, retried.Status);
    }

    [Fact]
    public void ResolutionKeyRejectsInvalidIdentity()
    {
        Assert.Throws<ArgumentException>(
            () => OperationResolutionKey.Create(
                "support-004",
                Guid.Empty));

        Assert.Throws<ArgumentException>(
            () => OperationResolutionKey.Create(
                " ",
                Guid.NewGuid()));
    }

    private static OperationResolutionInput ValidInput() =>
        new(
            Guid.Parse("09cbef2f-ef42-495e-9ae7-3a1d615752ef"),
            "support-004",
            MissionExecutionResult.Completed,
            ObjectivesCompleted: 1,
            ObjectivesRequired: 1,
            AircraftSurvived: true,
            CrewSurvived: true,
            MissionDuration: TimeSpan.FromMinutes(38),
            ResolvedAt: new DateTimeOffset(
                2026,
                9,
                20,
                23,
                0,
                0,
                TimeSpan.Zero));

    private sealed class ThrowOnceResolver : IOperationResolver
    {
        private bool _shouldThrow = true;

        public OperationOutcome Resolve(OperationResolutionInput input)
        {
            if (_shouldThrow)
            {
                _shouldThrow = false;
                throw new InvalidOperationException(
                    "Simulated resolution failure.");
            }

            return new OperationResolver().Resolve(input);
        }
    }
}
