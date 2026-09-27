namespace OpenCareer.SimConnect;

internal static class SimConnectEngineFailureDefinition
{
    internal const uint DefinitionId = 0x4F430050;
    internal const uint RequestId = 0x4F430051;
    internal const uint EventId = 0x4F430052;
    internal const string EventName = "TOGGLE_ENGINE1_FAILURE";
    internal const string SimVar = "GENERAL ENG FAILED:1";
    internal const string Units = "Bool";
    // Read once a second; these are transport freshness/ack bounds, not gameplay tuning.
    internal static readonly TimeSpan MaximumObservationAge = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan AcknowledgementWindow = TimeSpan.FromSeconds(10);
}
