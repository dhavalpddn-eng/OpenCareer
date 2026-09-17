namespace OpenCareer.LiveProbe;

internal sealed record LiveProbeOptions(string OutputPath, TimeSpan? Duration)
{
    internal const string Usage =
        """
        OpenCareer.LiveProbe

        Usage:
          dotnet run --project tools/OpenCareer.LiveProbe/OpenCareer.LiveProbe.csproj -c Release -p:Platform=x64 -p:SimConnectNativePath="<path-to-SimConnect.dll>" -- [options]

        Options:
          --output <path>             JSONL trace file. Defaults to LocalAppData\OpenCareer\Diagnostics.
          --duration-seconds <value>  Stop automatically after a positive number of seconds.
          -h, --help                  Show this help.

        Run without --duration-seconds for an interactive session and press Ctrl+C to stop.
        """;

    internal static LiveProbeOptions Parse(IReadOnlyList<string> args)
    {
        string? output = null;
        TimeSpan? duration = null;

        for (int index = 0; index < args.Count; index++)
        {
            string arg = args[index];
            switch (arg)
            {
                case "--output":
                    output = ReadValue(args, ref index, arg);
                    break;

                case "--duration-seconds":
                    string rawDuration = ReadValue(args, ref index, arg);
                    if (!int.TryParse(rawDuration, out int seconds) || seconds <= 0)
                        throw new ArgumentException("--duration-seconds must be a positive whole number.");

                    duration = TimeSpan.FromSeconds(seconds);
                    break;

                default:
                    throw new ArgumentException($"Unknown live-probe option: {arg}");
            }
        }

        string outputPath = output is null ? CreateDefaultOutputPath() : Path.GetFullPath(output);
        return new(outputPath, duration);
    }

    private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
            throw new ArgumentException($"{option} requires a value.");

        index++;
        string value = args[index];
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{option} requires a non-empty value.");

        return value;
    }

    private static string CreateDefaultOutputPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        string directory = Path.Combine(root, "OpenCareer", "Diagnostics");
        string fileName = $"simconnect-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jsonl";
        return Path.Combine(directory, fileName);
    }
}
