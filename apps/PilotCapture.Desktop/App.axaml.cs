using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Infrastructure.Persistence;

namespace PilotCapture.Desktop;

public sealed partial class App : Avalonia.Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VolumePilot", "PilotCapture");
            Directory.CreateDirectory(appDataPath);

            var services = new ServiceCollection();
            services.AddPilotCaptureInfrastructure(Path.Combine(appDataPath, "pilot-capture.db"));
            using (var provider = services.BuildServiceProvider())
            using (var scope = provider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
                    .InitializeAsync().GetAwaiter().GetResult();
            }

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
