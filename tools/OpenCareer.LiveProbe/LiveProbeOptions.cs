using System.Globalization;
using OpenCareer.Application.Simulator;

namespace OpenCareer.LiveProbe;

internal sealed record LiveEscortProbeOptions(
    string ContainerTitle,
    string Livery,
    string TailNumber,
    int FlightNumber,
    string FlightPlanPath,
    double FlightPlanPosition,
    bool TouchAndGo)
{
    internal SimulatorMissionAircraftSpawnRequest ToSpawnRequest() =>
        new(
            ActorKey: "side-work-protected-1",
            ContainerTitle,
            Livery,
            TailNumber,
            FlightNumber,
            FlightPlanPath,
            FlightPlanPosition,
            TouchAndGo);
}

internal sealed record LiveProbeOptions(
    string OutputPath,
    TimeSpan? Duration,
    LiveEscortProbeOptions? Escort)
{
    internal const string Usage =
        """
        OpenCareer.LiveProbe

        Usage:
          dotnet run --project tools/OpenCareer.LiveProbe/OpenCareer.LiveProbe.csproj -c Release -p:Platform=x64 -p:SimConnectNativePath="<path-to-SimConnect.dll>" -- [options]

        Options:
          --output <path>                    JSONL trace file. Defaults to LocalAppData\OpenCareer\Diagnostics.
          --duration-seconds <value>         Stop automatically after a positive number of seconds.

          Side Work escort live test:
          --escort-container-title <title>   Installed MSFS aircraft container title for the protected AI aircraft.
          --escort-flight-plan <path>        Path to a saved MSFS .PLN file.
          --escort-livery <name>             Optional livery. Defaults to empty.
          --escort-tail <tail>               Optional tail number. Defaults to OC001.
          --escort-flight-number <number>    Optional flight number. Defaults to -1 (none).
          --escort-position <value>           Flight-plan position. Defaults to 0.5.
          --escort-touch-and-go              Request touch-and-go instead of full-stop behavior.

          -h, --help                         Show this help.

        The escort container title and flight plan must be supplied together.
        Run without --duration-seconds for an interactive session and press Ctrl+C to stop.
        """;

    internal static LiveProbeOptions Parse(IReadOnlyList<string> args)
    {
        string? output = null;
        TimeSpan? duration = null;

        string? escortContainerTitle = null;
        string? escortFlightPlan = null;
        string escortLivery = string.Empty;
        string escortTail = "OC001";
        int escortFlightNumber = -1;
        double escortPosition = 0.5;
        bool escortTouchAndGo = false;

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

                case "--escort-container-title":
                    escortContainerTitle = ReadValue(args, ref index, arg);
                    break;

                case "--escort-flight-plan":
                    escortFlightPlan = ReadValue(args, ref index, arg);
                    break;

                case "--escort-livery":
                    escortLivery = ReadValue(args, ref index, arg);
                    break;

                case "--escort-tail":
                    escortTail = ReadValue(args, ref index, arg);
                    break;

                case "--escort-flight-number":
                    string rawFlightNumber = ReadValue(args, ref index, arg);
                    if (!int.TryParse(
                            rawFlightNumber,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out escortFlightNumber))
                    {
                        throw new ArgumentException("--escort-flight-number must be a whole number.");
                    }
                    break;

                case "--escort-position":
                    string rawPosition = ReadValue(args, ref index, arg);
                    if (!double.TryParse(
                            rawPosition,
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out escortPosition)
                        || !double.IsFinite(escortPosition)
                        || escortPosition < 0)
                    {
                        throw new ArgumentException("--escort-position must be a non-negative number.");
                    }
                    break;

                case "--escort-touch-and-go":
                    escortTouchAndGo = true;
                    break;

                default:
                    throw new ArgumentException($"Unknown live-probe option: {arg}");
            }
        }

        string outputPath = output is null
            ? CreateDefaultOutputPath()
            : Path.GetFullPath(output);

        bool hasEscortTitle = !string.IsNullOrWhiteSpace(escortContainerTitle);
        bool hasEscortPlan = !string.IsNullOrWhiteSpace(escortFlightPlan);
        if (hasEscortTitle != hasEscortPlan)
        {
            throw new ArgumentException(
                "--escort-container-title and --escort-flight-plan must be supplied together.");
        }

        LiveEscortProbeOptions? escort = null;
        if (hasEscortTitle && hasEscortPlan)
        {
            string planInput = Path.GetFullPath(escortFlightPlan!);
            string planFile = Path.HasExtension(planInput)
                ? planInput
                : planInput + ".pln";

            if (!File.Exists(planFile))
                throw new ArgumentException($"Escort flight plan was not found: {planFile}");

            string planWithoutExtension =
                string.Equals(
                    Path.GetExtension(planFile),
                    ".pln",
                    StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(
                        Path.GetDirectoryName(planFile) ?? string.Empty,
                        Path.GetFileNameWithoutExtension(planFile))
                    : planFile;

            escort = new LiveEscortProbeOptions(
                escortContainerTitle!,
                escortLivery,
                escortTail,
                escortFlightNumber,
                planWithoutExtension,
                escortPosition,
                escortTouchAndGo);

            escort.ToSpawnRequest().Validate();
        }

        return new(outputPath, duration, escort);
    }

    private static string ReadValue(
        IReadOnlyList<string> args,
        ref int index,
        string option)
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
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        string directory = Path.Combine(
            root,
            "OpenCareer",
            "Diagnostics");
        string fileName =
            $"simconnect-live-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.jsonl";
        return Path.Combine(directory, fileName);
    }
}
