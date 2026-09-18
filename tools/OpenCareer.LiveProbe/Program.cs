using Microsoft.Extensions.Logging;
using OpenCareer.LiveProbe;
using OpenCareer.SimConnect;

if (args.Any(static arg => arg is "-h" or "--help"))
{
    Console.WriteLine(LiveProbeOptions.Usage);
    return 0;
}

try
{
    LiveProbeOptions options = LiveProbeOptions.Parse(args);

    NativeRuntimeDiagnostics.ReportSimConnect();

    using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.TimestampFormat = "HH:mm:ss.fff ";
        });
    });

    await using var connection = new SimConnectConnection(
        loggerFactory.CreateLogger<SimConnectConnection>());

    var session = new LiveProbeSession(connection, options);
    return await session.RunAsync().ConfigureAwait(false);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(LiveProbeOptions.Usage);
    return 64;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Live probe failed: {ex}");
    return 1;
}
