using Microsoft.Extensions.Logging.Abstractions;
using OpenCareer.App.Services;
using OpenCareer.Application.Settings;

namespace OpenCareer.Tests;

public sealed class JsonAppSettingsServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        $"opencareer-settings-store-{Guid.NewGuid():N}");

    [Theory]
    [InlineData("{ not-valid-json")]
    [InlineData("""{"SchemaVersion":2,"Preferences":{}}""")]
    [InlineData("""{"SchemaVersion":1,"Preferences":null}""")]
    [InlineData("""{"SchemaVersion":1,"Preferences":{"MeasurementSystem":99}}""")]
    [InlineData("""{"SchemaVersion":1,"Preferences":{"InputHints":99}}""")]
    public async Task InvalidExistingSettingsRequireExplicitResetBeforeOverwrite(
        string invalidDocument)
    {
        var paths = new OpenCareerDataPaths(_root);
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(paths.SettingsFile, invalidDocument);
        JsonAppSettingsService service = CreateService(paths);
        int changes = 0;
        service.Changed += (_, _) => changes++;

        await Assert.ThrowsAnyAsync<Exception>(
            () => service.InitializeAsync());
        Assert.True(service.RequiresReset);
        await Assert.ThrowsAsync<InvalidDataException>(
            () => service.UpdateAsync(
                AppPreferences.Default with { ReduceMotion = true }));

        Assert.Equal(
            invalidDocument,
            await File.ReadAllTextAsync(paths.SettingsFile));

        await service.ResetAsync();
        Assert.False(service.RequiresReset);
        Assert.Equal(1, changes);
        await service.UpdateAsync(
            AppPreferences.Default with { ReduceMotion = true });

        Assert.True(service.Current.ReduceMotion);
        Assert.NotEqual(
            invalidDocument,
            await File.ReadAllTextAsync(paths.SettingsFile));
    }

    [Fact]
    public async Task MissingSettingsFileStartsFreshAndPersistsNormally()
    {
        var paths = new OpenCareerDataPaths(_root);
        JsonAppSettingsService service = CreateService(paths);

        await service.InitializeAsync();
        Assert.False(service.RequiresReset);
        AppPreferences saved = AppPreferences.Default with
        {
            MeasurementSystem = MeasurementSystem.Metric,
            ReduceMotion = true
        };
        await service.UpdateAsync(saved);

        JsonAppSettingsService reloaded = CreateService(paths);
        await reloaded.InitializeAsync();

        Assert.Equal(saved, reloaded.Current);
    }

    [Fact]
    public async Task SettingsPathCollisionCannotBeOverwrittenByReset()
    {
        var paths = new OpenCareerDataPaths(_root);
        Directory.CreateDirectory(paths.SettingsFile);
        JsonAppSettingsService service = CreateService(paths);

        Exception? readFailure = await Record.ExceptionAsync(
            () => service.InitializeAsync());
        Exception? resetFailure = await Record.ExceptionAsync(
            () => service.ResetAsync());

        Assert.True(
            readFailure is IOException or UnauthorizedAccessException,
            $"Expected a filesystem read failure, but received {readFailure?.GetType().Name ?? "no exception"}.");
        Assert.True(
            resetFailure is IOException or UnauthorizedAccessException,
            $"Expected a filesystem write failure, but received {resetFailure?.GetType().Name ?? "no exception"}.");
        Assert.True(service.RequiresReset);
        Assert.True(Directory.Exists(paths.SettingsFile));
        Assert.False(File.Exists(paths.SettingsFile + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static JsonAppSettingsService CreateService(OpenCareerDataPaths paths) =>
        new(
            NullLogger<JsonAppSettingsService>.Instance,
            paths);
}
