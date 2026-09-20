using System.Text.Json;
using OpenCareer.TraceAnalysis;

namespace OpenCareer.Tests;

public sealed class ProbeTraceAnalyzerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 18, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CompleteSyntheticCaptureReportsCadenceAndRangesWithoutClassifyingFlight()
    {
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1),
            Sample(2, 2, ground: false), Sample(3, 3), End(4, 3));
        Assert.False(report.NeedsReview);
        Assert.Equal(3, report.ValidTelemetryRecords);
        Assert.Equal(1, report.ConnectedPeriods);
        Assert.Equal(new SampleCadence(2, 1, 1, 1), report.ActiveSampleCadence);
        Assert.Equal(new NumericRange("degrees", 3, 0, 0), report.FieldRanges["latitudeDegrees"]);
        Assert.Equal(new NumericRange("feet/minute", 3, -600, -600), report.FieldRanges["verticalSpeedFeetPerMinute"]);
        Assert.True(report.SawReportedOnGround);
        Assert.True(report.SawReportedOffGround);
        Assert.Equal(2, report.ReportedGroundContactChanges);
        Assert.True(report.FinalTelemetryCleared);
        Assert.Contains("No live acceptance", report.Scope);
    }

    [Fact]
    public void PauseRepublishingAndSlewDoNotInflateAcquisitionCadence()
    {
        var paused = Sample(2, 2, paused: true);
        paused["sampleTimestampUtc"] = Epoch.AddSeconds(1); // Deliberately repeated timestamp in this synthetic fixture.
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1), paused,
            Sample(3, 100, paused: true), Sample(4, 101), Sample(5, 102),
            Sample(6, 103, slew: true), Sample(7, 200), Sample(8, 201), End(202, 8));
        Assert.False(report.NeedsReview);
        Assert.True(report.SawPaused);
        Assert.True(report.SawResume);
        Assert.True(report.SawSlew);
        Assert.Equal(1, report.DuplicateSampleTimestamps);
        Assert.Equal(new SampleCadence(2, 1, 1, 1), report.ActiveSampleCadence);
    }

    [Fact]
    public void ReconnectAndClearingBreakContinuityAcrossLongOutage()
    {
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1), Sample(2, 2),
            Connection(3, "Reconnecting"), Record("telemetryCleared", 3, ("connectionState", "Reconnecting")),
            Connection(1000, "Connected"), Sample(3, 1001, ground: false), Sample(4, 1002, ground: false), End(1003, 4));
        Assert.False(report.NeedsReview);
        Assert.Equal(2, report.ConnectedPeriods);
        Assert.Equal(1, report.TelemetryClearRecords);
        Assert.Equal(1, report.ConnectionStates["Reconnecting"]);
        Assert.Equal(0, report.ReportedGroundContactChanges);
        Assert.Equal(new SampleCadence(2, 1, 1, 1), report.ActiveSampleCadence);
    }

    [Theory]
    [InlineData("latitudeDegrees", "91")]
    [InlineData("longitudeDegrees", "-181")]
    [InlineData("headingDegrees", "360")]
    [InlineData("enginesRunning", "1.5")]
    [InlineData("flapsPositionPercent", "101")]
    [InlineData("fuelTotalPounds", "1e400")]
    [InlineData("onGround", "1")]
    [InlineData("groundSpeedKnots", "null")]
    public void InvalidFieldsAreReportedByLineAndNeverSubstitutedWithZeros(string name, string json)
    {
        var invalid = Sample(2, 2);
        invalid[name] = JsonSerializer.Deserialize<JsonElement>(json);
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1), invalid,
            Sample(3, 3), Sample(4, 4), End(5, 4));
        Assert.True(report.NeedsReview);
        Assert.Equal(4, report.TelemetryRecords);
        Assert.Equal(3, report.ValidTelemetryRecords);
        Assert.Contains(report.Findings, finding => finding.Line == 4 && finding.Code == "invalid-record");
        Assert.Equal(new SampleCadence(1, 1, 1, 1), report.ActiveSampleCadence);
        Assert.Equal(3, report.FieldRanges["latitudeDegrees"].Count);
    }

    [Fact]
    public void MissingFieldsAndMalformedJsonDoNotStopLaterAnalysis()
    {
        var missing = Sample(1, 1);
        missing.Remove("paused");
        string trace = string.Join('\n', Json(Start()), Json(Connection(0, "Connected")), Json(missing),
            "{incomplete", Json(Sample(2, 3)), Json(End(4, 2)));
        var report = ProbeTraceAnalyzer.Analyze(new StringReader(trace));
        Assert.Equal(1, report.ValidTelemetryRecords);
        Assert.Equal(2, report.Findings.Count(finding => finding.Code == "invalid-record"));
        Assert.Null(report.ActiveSampleCadence.MeanSeconds);
    }

    [Fact]
    public void TruncatedOrEmptyCaptureCannotReportCleanCompletion()
    {
        var partial = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1));
        Assert.True(partial.NeedsReview);
        Assert.Equal(0, partial.SessionEnds);
        Assert.Null(partial.FinalTelemetryCleared);
        Assert.Contains(partial.Findings, finding => finding.Code == "session-end-count");
        var empty = Analyze();
        Assert.True(empty.NeedsReview);
        Assert.Contains(empty.Findings, finding => finding.Code == "no-valid-telemetry");
        Assert.Empty(empty.FieldRanges);
    }

    [Fact]
    public void BadFinalSummaryAndRecordsAfterEndAreVisible()
    {
        var end = End(2, 99);
        end["finalConnectionState"] = "Connected";
        end["telemetryCleared"] = false;
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1), end, Sample(2, 3));
        Assert.Contains(report.Findings, finding => finding.Code == "summary-count-mismatch");
        Assert.Contains(report.Findings, finding => finding.Code == "unclean-stop");
        Assert.Contains(report.Findings, finding => finding.Code == "record-after-end");
    }

    [Fact]
    public void SequenceGapsAndBackwardSampleTimeBreakCadence()
    {
        var backwards = Sample(5, 20);
        backwards["sampleTimestampUtc"] = Epoch.AddSeconds(2);
        var report = Analyze(Start(), Connection(0, "Connected"), Sample(1, 1), Sample(3, 3),
            Sample(4, 4), backwards, Sample(6, 21), Sample(7, 22), End(23, 6));
        Assert.Equal(1, report.SampleTimestampRegressions);
        Assert.Contains(report.Findings, finding => finding.Code == "sequence-discontinuity");
        Assert.Contains(report.Findings, finding => finding.Code == "sample-time-regressed");
        Assert.Equal(new SampleCadence(2, 1, 1, 1), report.ActiveSampleCadence);
    }

    [Fact]
    public void ClockReversalUnknownRecordsAndConnectionIssuesRequireReview()
    {
        var failed = Connection(0, "WaitingForSimulator");
        failed["issue"] = "ConnectionFailed";
        var report = Analyze(Start(), failed, Connection(1, "Connected"), Sample(1, 3),
            Sample(2, 2), Record("future-format-event", 4), Sample(3, 5), End(6, 3));
        Assert.Contains(report.Findings, finding => finding.Code == "observation-time-regressed");
        Assert.Contains(report.Findings, finding => finding.Code == "unknown-record");
        Assert.Contains(report.Findings, finding => finding.Code == "connection-issue");
        Assert.Equal(0, report.ActiveSampleCadence.IntervalCount);
    }

    [Fact]
    public void TelemetryWithoutConnectionIsReportedAndFindingsStayBounded()
    {
        var report = Analyze(Start(), Sample(1, 1), End(2, 1));
        Assert.Contains(report.Findings, finding => finding.Code == "telemetry-outside-connected");
        var many = ProbeTraceAnalyzer.Analyze(new StringReader(string.Join('\n', Enumerable.Repeat("null", 200))));
        Assert.Equal(100, many.Findings.Count);
        Assert.True(many.FindingCount > 200);
    }

    private static ProbeTraceReport Analyze(params Dictionary<string, object?>[] records) =>
        ProbeTraceAnalyzer.Analyze(new StringReader(string.Join('\n', records.Select(Json))));

    private static string Json(Dictionary<string, object?> record) => JsonSerializer.Serialize(record);
    private static Dictionary<string, object?> Start() => Record("sessionStart", 0);
    private static Dictionary<string, object?> Connection(int time, string state) =>
        Record("connection", time, ("state", state), ("issue", "None"));
    private static Dictionary<string, object?> End(int time, int samples) =>
        Record("sessionEnd", time, ("telemetrySamples", samples), ("finalConnectionState", "Disconnected"), ("telemetryCleared", true));

    private static Dictionary<string, object?> Sample(int sequence, int time, bool ground = true,
        bool paused = false, bool slew = false) => Record("telemetry", time,
        ("sequence", sequence), ("sampleTimestampUtc", Epoch.AddSeconds(time)),
        ("latitudeDegrees", 0), ("longitudeDegrees", 0), ("altitudeMslFeet", -10), ("altitudeAglFeet", 5),
        ("indicatedAirspeedKnots", 120), ("groundSpeedKnots", 125), ("verticalSpeedFeetPerMinute", -600),
        ("headingDegrees", 180), ("pitchDegrees", -2), ("bankDegrees", 0), ("normalAccelerationG", 1),
        ("onGround", ground), ("parkingBrakeSet", false), ("enginesRunning", 2), ("fuelTotalPounds", 1000),
        ("payloadPounds", 200), ("flapsPositionPercent", 0), ("gearDown", true), ("paused", paused), ("slewActive", slew));

    private static Dictionary<string, object?> Record(string type, int time, params (string Name, object? Value)[] fields)
    {
        var record = new Dictionary<string, object?> { ["type"] = type, ["observedAtUtc"] = Epoch.AddSeconds(time) };
        foreach (var field in fields)
            record.Add(field.Name, field.Value);
        return record;
    }
}
