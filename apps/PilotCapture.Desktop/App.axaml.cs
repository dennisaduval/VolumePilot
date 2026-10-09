using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Infrastructure.Persistence;

namespace PilotCapture.Desktop;

public sealed partial class App : Avalonia.Application
{
    private ServiceProvider? _serviceProvider;
    private IServiceScope? _windowScope;

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
            services.AddPilotCaptureInfrastructure(
                Path.Combine(appDataPath, "pilot-capture.db"),
                Path.Combine(appDataPath, "media"));
            services.AddSingleton<WindowsPortraitFaceDetector>();
            _serviceProvider = services.BuildServiceProvider();
            using (var scope = _serviceProvider.CreateScope())
            {
                scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
                    .InitializeAsync().GetAwaiter().GetResult();
            }

            _windowScope = _serviceProvider.CreateScope();
            desktop.MainWindow = new MainWindow(
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Rosters.IRosterImportService>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Capture.ICaptureWorkflowService>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageIngestService>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageReviewService>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageAssetStore>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageAssociationExportService>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Rosters.IRosterExportService>(),
                _windowScope.ServiceProvider.GetRequiredService<WindowsPortraitFaceDetector>(),
                _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IJobMediaService>());
            desktop.Exit += (_, _) =>
            {
                _windowScope?.Dispose();
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}

