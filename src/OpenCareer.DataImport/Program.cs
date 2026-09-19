using OpenCareer.Infrastructure.AviationData;
using System.Text.Json;

if (args.Length < 2 || args.Length > 3 ||
    (args[0], args.Length) is not (("import", 3) or ("refresh", 2) or ("refresh-openaip", 3)))
{
    Console.Error.WriteLine("Usage: OpenCareer.DataImport import <database.sqlite> <csv-directory> | refresh <database.sqlite> | refresh-openaip <database.sqlite> <country-code>");
    return 2;
}

if (args[0] == "refresh-openaip")
{
    var key = Environment.GetEnvironmentVariable("OPENAIP_API_KEY");
    if (string.IsNullOrWhiteSpace(key))
    {
        Console.Error.WriteLine("OPENAIP_API_KEY is required for a manual OpenAIP refresh.");
        return 2;
    }
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var count = await new OpenAipAirspaceRefreshService(client).RefreshCountryAsync(args[1], args[2], key);
        Console.WriteLine($"OpenAIP airspaces for {args[2].ToUpperInvariant()}: {count:N0}. Local reference database updated.");
        return 0;
    }
    catch (Exception error) when (error is IOException or HttpRequestException or TaskCanceledException or
        Microsoft.Data.Sqlite.SqliteException or JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
    {
        Console.Error.WriteLine($"OpenAIP refresh failed; previous data was preserved: {error.Message}");
        return 1;
    }
}

var sourceDirectory = args.Length == 3 ? args[2] : Path.Combine(Path.GetTempPath(), "opencareer-ourairports-" + Guid.NewGuid().ToString("N"));
var downloaded = args[0] == "refresh";
try
{
    if (downloaded)
    {
        Directory.CreateDirectory(sourceDirectory);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        foreach (var file in OurAirportsImportService.RequiredFiles)
        {
            using var response = await client.GetAsync(new Uri("https://davidmegginson.github.io/ourairports-data/" + file), HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            await using var source = await response.Content.ReadAsStreamAsync();
            await using var destination = File.Create(Path.Combine(sourceDirectory, file));
            await source.CopyToAsync(destination);
        }
    }

    var counts = new OurAirportsImportService().Import(sourceDirectory, args[1]);
    foreach (var (name, count) in counts) Console.WriteLine($"{name}: {count:N0}");
    Console.WriteLine("OurAirports reference database updated.");
    return 0;
}
catch (Exception error) when (error is IOException or HttpRequestException or TaskCanceledException or Microsoft.Data.Sqlite.SqliteException or FormatException or UnauthorizedAccessException or ArgumentException or Microsoft.VisualBasic.FileIO.MalformedLineException)
{
    Console.Error.WriteLine($"Import failed; previous reference data was preserved: {error.Message}");
    return 1;
}
finally
{
    if (downloaded && Directory.Exists(sourceDirectory))
    {
        try { Directory.Delete(sourceDirectory, recursive: true); }
        catch (IOException) { /* Temporary files can be cleared by the OS. */ }
        catch (UnauthorizedAccessException) { /* Keep the original import result. */ }
    }
}
