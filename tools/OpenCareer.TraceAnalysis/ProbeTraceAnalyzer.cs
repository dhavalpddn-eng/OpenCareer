using System.Text.Json;

namespace OpenCareer.TraceAnalysis;

/// <summary>Streaming diagnostics for the production live-probe JSONL format; never a flight detector.</summary>
public static class ProbeTraceAnalyzer
{
    public static ProbeTraceReport Analyze(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var analysis = new Analysis();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            analysis.Read(line);
        return analysis.Finish();
    }

    private sealed class Analysis
    {
        private static readonly (string Name, string Unit)[] NumericFields =
        [
            ("latitudeDegrees", "degrees"), ("longitudeDegrees", "degrees"),
            ("altitudeMslFeet", "feet MSL"), ("altitudeAglFeet", "feet above surface"),
            ("indicatedAirspeedKnots", "knots"), ("groundSpeedKnots", "knots"),
            ("verticalSpeedFeetPerMinute", "feet/minute"), ("headingDegrees", "degrees true"),
            ("pitchDegrees", "degrees"), ("bankDegrees", "degrees"), ("normalAccelerationG", "G"),
            ("enginesRunning", "count"), ("fuelTotalPounds", "pounds"),
            ("payloadPounds", "pounds"), ("flapsPositionPercent", "percent")
        ];

        private readonly List<TraceFinding> _findings = [];
        private readonly Dictionary<string, long> _states = new(StringComparer.Ordinal);
        private readonly Dictionary<string, long> _issues = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NumericRange> _ranges = new(StringComparer.Ordinal);
        private long _line, _records, _findingCount, _telemetryRecords, _validTelemetry;
        private int _starts, _ends, _connectedPeriods, _clears;
        private DateTimeOffset? _lastObservation, _lastSample;
        private string? _connectionState, _finalState;
        private bool? _finalCleared, _previousPaused, _previousSlew, _previousGround;
        private bool _sawPaused, _sawUnpaused, _sawResume, _sawSlew, _sawGround, _sawOffGround;
        private long _lastSequence, _duplicates, _regressions, _groundChanges, _intervalCount;
        private double _minimumInterval = double.PositiveInfinity, _maximumInterval, _meanInterval;

        internal void Read(string line)
        {
            _line++;
            if (string.IsNullOrWhiteSpace(line))
                return;
            _records++;
            if (_ends > 0)
                Find("record-after-end", "A record follows sessionEnd; analyze one capture per file.");
            try
            {
                using var document = JsonDocument.Parse(line);
                var record = document.RootElement;
                if (record.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Expected a JSON object.");
                string type = Text(record, "type");
                var observed = Timestamp(record, "observedAtUtc");
                if (_lastObservation is { } previous && observed < previous)
                {
                    Find("observation-time-regressed", "UTC observation time moved backward; check clock changes and trace order.");
                    ResetContinuity();
                }
                _lastObservation = observed;

                switch (type)
                {
                    case "sessionStart":
                        _starts++;
                        if (_records != 1)
                            Find("session-start-order", "sessionStart must be the first record of a single capture.");
                        ResetContinuity();
                        break;
                    case "connection":
                        ReadConnection(record);
                        break;
                    case "telemetry":
                        _telemetryRecords++;
                        ReadTelemetry(record);
                        break;
                    case "telemetryCleared":
                        Text(record, "connectionState");
                        _clears++;
                        ResetContinuity();
                        break;
                    case "sessionEnd":
                        ReadEnd(record);
                        break;
                    default:
                        Find("unknown-record", $"Unsupported record type: {type}.");
                        ResetContinuity();
                        break;
                }
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                Find("invalid-record", ex.Message);
                ResetContinuity();
            }
        }

        private void ReadConnection(JsonElement record)
        {
            string state = Text(record, "state");
            string issue = Text(record, "issue");
            Increment(_states, state);
            Increment(_issues, issue);
            if (state == "Connected" && _connectionState != "Connected")
                _connectedPeriods++;
            if (issue != "None")
                Find("connection-issue", $"Observed {state} / {issue}; this may be an intentional absence or restart test.");
            _connectionState = state;
            ResetContinuity();
        }

        private void ReadTelemetry(JsonElement record)
        {
            long sequence = Integer(record, "sequence");
            if (sequence < 1)
                throw new InvalidDataException("sequence must be positive.");
            var timestamp = Timestamp(record, "sampleTimestampUtc");
            double[] values = NumericFields.Select(field => Number(record, field.Name)).ToArray();
            bool ground = Boolean(record, "onGround");
            bool paused = Boolean(record, "paused");
            bool slew = Boolean(record, "slewActive");
            Boolean(record, "parkingBrakeSet");
            Boolean(record, "gearDown");
            if (Math.Abs(values[0]) > 90 || Math.Abs(values[1]) > 180)
                throw new InvalidDataException("Coordinates are outside degree ranges.");
            if (values[7] < 0 || values[7] >= 360)
                throw new InvalidDataException("Normalized heading must be in [0, 360).");
            if (values[11] < 0 || values[11] != Math.Truncate(values[11]))
                throw new InvalidDataException("enginesRunning must be a nonnegative integer.");
            if (values[14] < 0 || values[14] > 100)
                throw new InvalidDataException("flapsPositionPercent must be in [0, 100].");

            // A malformed sample contributes neither ranges nor an interval across its gap.
            _validTelemetry++;
            if (sequence != _lastSequence + 1)
            {
                Find("sequence-discontinuity", $"Expected sequence {_lastSequence + 1}, observed {sequence}.");
                ResetContinuity();
            }
            _lastSequence = sequence;
            if (_connectionState != "Connected")
            {
                Find("telemetry-outside-connected", "Telemetry was observed without a preceding Connected state; inspect polling order and connection loss.");
                ResetContinuity();
            }

            for (int index = 0; index < NumericFields.Length; index++)
            {
                var field = NumericFields[index];
                double value = values[index];
                _ranges[field.Name] = _ranges.TryGetValue(field.Name, out var range)
                    ? range with { Count = range.Count + 1, Minimum = Math.Min(range.Minimum, value), Maximum = Math.Max(range.Maximum, value) }
                    : new(field.Unit, 1, value, value);
            }
            _sawPaused |= paused;
            _sawUnpaused |= !paused;
            _sawResume |= _previousPaused == true && !paused;
            _sawSlew |= slew;
            _sawGround |= ground;
            _sawOffGround |= !ground;
            if (_previousGround.HasValue && _previousGround != ground)
                _groundChanges++;

            if (_lastSample is { } previous)
            {
                double seconds = (timestamp - previous).TotalSeconds;
                if (seconds == 0)
                    _duplicates++; // Repeated timestamps never count as acquisition intervals.
                else if (seconds < 0)
                {
                    _regressions++;
                    Find("sample-time-regressed", "Sample timestamp moved backward within a connected period.");
                    ResetContinuity();
                    return;
                }
                else if (_connectionState == "Connected" && !paused && !slew
                    && _previousPaused == false && _previousSlew == false)
                {
                    _intervalCount++;
                    _meanInterval += (seconds - _meanInterval) / _intervalCount;
                    _minimumInterval = Math.Min(_minimumInterval, seconds);
                    _maximumInterval = Math.Max(_maximumInterval, seconds);
                }
            }
            _lastSample = timestamp;
            _previousPaused = paused;
            _previousSlew = slew;
            _previousGround = ground;
        }

        private void ReadEnd(JsonElement record)
        {
            string finalState = Text(record, "finalConnectionState");
            bool cleared = Boolean(record, "telemetryCleared");
            long expectedSamples = Integer(record, "telemetrySamples");
            _ends++;
            _finalState = finalState;
            _finalCleared = cleared;
            if (expectedSamples != _telemetryRecords)
                Find("summary-count-mismatch", $"sessionEnd reports {expectedSamples} telemetry records; read {_telemetryRecords}.");
            if (finalState != "Disconnected" || !cleared)
                Find("unclean-stop", "sessionEnd did not confirm Disconnected with telemetry cleared.");
            ResetContinuity();
        }

        internal ProbeTraceReport Finish()
        {
            if (_starts != 1)
                Find("session-start-count", $"Expected one sessionStart; observed {_starts}.", summary: true);
            if (_ends != 1)
                Find("session-end-count", $"Expected one sessionEnd; observed {_ends}. The capture may be incomplete.", summary: true);
            if (_connectedPeriods == 0)
                Find("no-connected-period", "No Connected state was captured.", summary: true);
            if (_validTelemetry == 0)
                Find("no-valid-telemetry", "No complete, valid telemetry record was captured.", summary: true);

            return new()
            {
                LinesRead = _line, TelemetryRecords = _telemetryRecords, ValidTelemetryRecords = _validTelemetry,
                SessionStarts = _starts, SessionEnds = _ends, ConnectedPeriods = _connectedPeriods,
                TelemetryClearRecords = _clears, SawPaused = _sawPaused, SawUnpaused = _sawUnpaused,
                SawResume = _sawResume, SawSlew = _sawSlew, SawReportedOnGround = _sawGround,
                SawReportedOffGround = _sawOffGround, ReportedGroundContactChanges = _groundChanges,
                DuplicateSampleTimestamps = _duplicates, SampleTimestampRegressions = _regressions,
                FinalConnectionState = _finalState, FinalTelemetryCleared = _finalCleared,
                ConnectionStates = _states, ConnectionIssues = _issues, FieldRanges = _ranges,
                ActiveSampleCadence = new(_intervalCount, _intervalCount > 0 ? _minimumInterval : null,
                    _intervalCount > 0 ? _meanInterval : null, _intervalCount > 0 ? _maximumInterval : null),
                Findings = _findings, FindingCount = _findingCount
            };
        }

        private void ResetContinuity()
        {
            _lastSample = null;
            _previousPaused = _previousSlew = _previousGround = null;
        }

        private void Find(string code, string detail, bool summary = false)
        {
            _findingCount++;
            if (_findings.Count < 100)
                _findings.Add(new(summary ? null : _line, code, detail));
        }

        private static void Increment(Dictionary<string, long> counts, string key) =>
            counts[key] = counts.GetValueOrDefault(key) + 1;

        private static string Text(JsonElement record, string name) =>
            record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
                : throw new InvalidDataException($"{name} must be a nonempty string.");

        private static DateTimeOffset Timestamp(JsonElement record, string name) =>
            record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                && value.TryGetDateTimeOffset(out var result) ? result
                : throw new InvalidDataException($"{name} must be an ISO timestamp.");

        private static double Number(JsonElement record, string name) =>
            record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetDouble(out double result) && double.IsFinite(result) ? result
                : throw new InvalidDataException($"{name} must be a finite number.");

        private static long Integer(JsonElement record, string name) =>
            record.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt64(out long result) ? result
                : throw new InvalidDataException($"{name} must be an integer.");

        private static bool Boolean(JsonElement record, string name) =>
            record.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean() : throw new InvalidDataException($"{name} must be a boolean.");
    }
}
