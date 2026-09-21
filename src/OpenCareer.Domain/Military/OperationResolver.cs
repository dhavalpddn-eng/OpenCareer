namespace OpenCareer.Domain.Military;

public interface IOperationResolver
{
    OperationOutcome Resolve(OperationResolutionInput input);
}

public sealed class OperationResolver : IOperationResolver
{
    public OperationOutcome Resolve(OperationResolutionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        input.Validate();

        OperationOutcomeStatus status = input.MissionResult switch
        {
            MissionExecutionResult.Aborted =>
                OperationOutcomeStatus.Aborted,

            MissionExecutionResult.Failed =>
                OperationOutcomeStatus.Failure,

            MissionExecutionResult.Completed when !input.CrewSurvived =>
                OperationOutcomeStatus.Failure,

            MissionExecutionResult.Completed
                when input.ObjectivesCompleted == input.ObjectivesRequired
                     && input.AircraftSurvived =>
                OperationOutcomeStatus.Success,

            MissionExecutionResult.Completed
                when input.ObjectivesCompleted > 0 =>
                OperationOutcomeStatus.PartialSuccess,

            MissionExecutionResult.Completed =>
                OperationOutcomeStatus.Failure,

            _ => throw new ArgumentOutOfRangeException(
                nameof(input.MissionResult),
                input.MissionResult,
                "Unsupported mission execution result.")
        };

        var outcome = new OperationOutcome(
            input.MissionId,
            input.OperationId,
            status,
            input.ResolvedAt);

        outcome.Validate();
        return outcome;
    }
}
