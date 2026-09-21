using Microsoft.Extensions.Logging;

namespace OpenCareer.App.Services;

public sealed class OpenCareerFileLoggerProvider : ILoggerProvider
{
    private const long MaximumLogBytes = 4 * 1024 * 1024;

    private readonly OpenCareerDataPaths _paths;
    private readonly object _gate = new();

    public OpenCareerFileLoggerProvider(OpenCareerDataPaths paths)
    {
        _paths = paths;
        _paths.EnsureDirectories();
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    private void Write(
        string category,
        LogLevel level,
        EventId eventId,
        string message,
        Exception? exception)
    {
        lock (_gate)
        {
            try
            {
                _paths.EnsureDirectories();
                RotateIfNeeded();

                string line =
                    $"{DateTimeOffset.Now:O} [{level}] {category}" +
                    (eventId.Id == 0 ? string.Empty : $" ({eventId.Id}:{eventId.Name})") +
                    $" {message}{Environment.NewLine}";

                if (exception is not null)
                    line += exception + Environment.NewLine;

                File.AppendAllText(_paths.LogFile, line);
            }
            catch
            {
                // Logging must never crash the application.
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_paths.LogFile))
            return;

        var info = new FileInfo(_paths.LogFile);
        if (info.Length < MaximumLogBytes)
            return;

        string archive = _paths.LogFile + ".1";
        File.Move(_paths.LogFile, archive, overwrite: true);
    }

    private sealed class FileLogger(
        OpenCareerFileLoggerProvider provider,
        string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NoopScope.Instance;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= LogLevel.Information;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            provider.Write(
                category,
                logLevel,
                eventId,
                formatter(state, exception),
                exception);
        }
    }

    private sealed class NoopScope : IDisposable
    {
        public static NoopScope Instance { get; } = new();
        public void Dispose() { }
    }
}
