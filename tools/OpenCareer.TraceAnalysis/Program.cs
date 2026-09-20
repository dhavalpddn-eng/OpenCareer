using System.Text.Json;
using OpenCareer.TraceAnalysis;

if (args.Length == 1 && args[0] is "-h" or "--help")
{
    Console.WriteLine("OpenCareer.TraceAnalysis <live-probe.jsonl>");
    Console.WriteLine("Writes a diagnostic JSON report. Exit 0: no structural findings; 2: review needed; 64: usage error; 1: I/O failure.");
    Console.WriteLine("This does not certify live MSFS behavior, units, flight phases or threshold calibration.");
    return 0;
}

if (args.Length != 1 || string.IsNullOrWhiteSpace(args[0]))
{
    Console.Error.WriteLine("Usage: OpenCareer.TraceAnalysis <live-probe.jsonl>");
    return 64;
}

try
{
    using var reader = File.OpenText(args[0]);
    var report = ProbeTraceAnalyzer.Analyze(reader);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    }));
    return report.NeedsReview ? 2 : 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
{
    Console.Error.WriteLine($"Trace analysis failed: {ex.Message}");
    return 1;
}
