namespace OpenCareer.TraceAnalysis;

public sealed record TraceFinding(long? Line, string Code, string Detail);
public sealed record NumericRange(string Unit, long Count, double Minimum, double Maximum);
public sealed record SampleCadence(long IntervalCount, double? MinimumSeconds, double? MeanSeconds, double? MaximumSeconds);

public sealed record ProbeTraceReport
{
    public string Scope => "Diagnostics only. No live acceptance, unit correctness, flight classification or threshold calibration is established by this report.";
    public long LinesRead { get; init; }
    public long TelemetryRecords { get; init; }
    public long ValidTelemetryRecords { get; init; }
    public int SessionStarts { get; init; }
    public int SessionEnds { get; init; }
    public int ConnectedPeriods { get; init; }
    public int TelemetryClearRecords { get; init; }
    public bool SawPaused { get; init; }
    public bool SawUnpaused { get; init; }
    public bool SawResume { get; init; }
    public bool SawSlew { get; init; }
    public bool SawReportedOnGround { get; init; }
    public bool SawReportedOffGround { get; init; }
    public long ReportedGroundContactChanges { get; init; }
    public long DuplicateSampleTimestamps { get; init; }
    public long SampleTimestampRegressions { get; init; }
    public string? FinalConnectionState { get; init; }
    public bool? FinalTelemetryCleared { get; init; }
    public required IReadOnlyDictionary<string, long> ConnectionStates { get; init; }
    public required IReadOnlyDictionary<string, long> ConnectionIssues { get; init; }
    public required IReadOnlyDictionary<string, NumericRange> FieldRanges { get; init; }
    public required SampleCadence ActiveSampleCadence { get; init; }
    public required IReadOnlyList<TraceFinding> Findings { get; init; }
    public long FindingCount { get; init; }
    public bool NeedsReview => FindingCount > 0;
}
