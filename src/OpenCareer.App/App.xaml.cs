using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using OpenCareer.App.ViewModels;

namespace OpenCareer.App;

public partial class App : Application
{
    private readonly ServiceProvider _services;

    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        _services = services.BuildServiceProvider();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = _services.GetRequiredService<MainWindow>();
        window.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        var logger = _services.GetRequiredService<ILogger<App>>();
        logger.LogError(e.Exception, "Unhandled OpenCareer UI exception.");
    }
}
