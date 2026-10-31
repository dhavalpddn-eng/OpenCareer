using OpenCareer.Domain.Military;

namespace OpenCareer.Tests;

public sealed class MilitaryReputationConsequenceTests
{
    [Theory]
    [InlineData(OperationOutcomeStatus.Success, 0.52, 1, 0)]
    [InlineData(OperationOutcomeStatus.PartialSuccess, 0.51, 1, 0)]
    [InlineData(OperationOutcomeStatus.Failure, 0.47, 0, 1)]
    [InlineData(OperationOutcomeStatus.Aborted, 0.495, 0, 0)]
    public void ApplyChangesTrustAndCountersDeterministically(
        OperationOutcomeStatus status,
        double expectedTrust,
        int expectedSuccessDelta,
        int expectedFailureDelta)
    {
        var consequence = CreateConsequence();
        MilitaryCareerState current = Career();

        MilitaryCareerState result =
            consequence.Apply(current, Outcome(status));

        Assert.Equal(expectedTrust, result.Trust, precision: 10);
        Assert.Equal(
            current.SuccessfulOperations + expectedSuccessDelta,
            result.SuccessfulOperations);
        Assert.Equal(
            current.FailedOperations + expectedFailureDelta,
            result.FailedOperations);
    }

    [Fact]
    public void SuccessClampsTrustAtUpperBound()
    {
        var consequence = CreateConsequence();

        MilitaryCareerState result =
            consequence.Apply(
                Career() with { Trust = 0.99 },
                Outcome(OperationOutcomeStatus.Success));

        Assert.Equal(1, result.Trust);
    }

    [Fact]
    public void FailureClampsTrustAtLowerBound()
    {
        var consequence = CreateConsequence();

        MilitaryCareerState result =
            consequence.Apply(
                Career() with { Trust = 0.01 },
                Outcome(OperationOutcomeStatus.Failure));

        Assert.Equal(0, result.Trust);
    }

    [Fact]
    public void DuplicateOutcomeCannotApplyReputationTwice()
    {
        var consequence = CreateConsequence();
        MilitaryCareerState current = Career();
        OperationOutcome outcome =
            Outcome(OperationOutcomeStatus.Success);

        MilitaryCareerState first =
            consequence.Apply(current, outcome);

        var exception =
            Assert.Throws<DuplicateMilitaryReputationConsequenceException>(
                () => consequence.Apply(first, outcome));

        Assert.Equal(
            OperationResolutionKey.Create(
                outcome.OperationId,
                outcome.MissionId),
            exception.ResolutionKey);
    }

    [Fact]
    public void DifferentOperationCanApplyAfterPreviousOutcome()
    {
        var consequence = CreateConsequence();

        MilitaryCareerState first =
            consequence.Apply(
                Career(),
                Outcome(OperationOutcomeStatus.Success));

        MilitaryCareerState second =
            consequence.Apply(
                first,
                Outcome(OperationOutcomeStatus.PartialSuccess) with
                {
                    MissionId = Guid.Parse(
                        "68d2af3a-6747-4d72-a66d-ec87f999e81f"),
                    OperationId = "support-009"
                });

        Assert.Equal(0.53, second.Trust, precision: 10);
        Assert.Equal(4, second.SuccessfulOperations);
    }

    [Fact]
    public void CivilianCareerCannotReceiveMilitaryReputation()
    {
        var consequence = CreateConsequence();

        Assert.Throws<InvalidOperationException>(
            () => consequence.Apply(
                MilitaryCareerState.Civilian,
                Outcome(OperationOutcomeStatus.Success)));
    }

    [Fact]
    public void FailedApplyReleasesRegistryReservation()
    {
        var registry =
            new InMemoryMilitaryReputationConsequenceRegistry();
        var consequence =
            new MilitaryReputationConsequence(registry);
        MilitaryCareerState overflowing =
            Career() with
            {
                SuccessfulOperations = int.MaxValue
            };
        OperationOutcome outcome =
            Outcome(OperationOutcomeStatus.Success);

        Assert.Throws<OverflowException>(
            () => consequence.Apply(
                overflowing,
                outcome));

        MilitaryCareerState retried =
            consequence.Apply(
                Career(),
                outcome);

        Assert.Equal(3, retried.SuccessfulOperations);
    }

    private static MilitaryReputationConsequence CreateConsequence() =>
        new(
            new InMemoryMilitaryReputationConsequenceRegistry());

    private static MilitaryCareerState Career() =>
        new(
            MilitaryAffiliation.Reserve,
            MilitaryQualification.MilitaryFlight,
            Trust: 0.50,
            SuccessfulOperations: 2,
            FailedOperations: 1);

    private static OperationOutcome Outcome(OperationOutcomeStatus status) =>
        new(
            Guid.Parse("19080c94-3bea-4244-9b19-4326beeeae44"),
            "support-008",
            status,
            new DateTimeOffset(
                2026,
                9,
                21,
                3,
                0,
                0,
                TimeSpan.Zero));
}
