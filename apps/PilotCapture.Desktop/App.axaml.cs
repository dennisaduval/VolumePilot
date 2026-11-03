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
            ShowChooser();
            void ReplaceWindow(Avalonia.Controls.Window window)
            {
                var previous = desktop.MainWindow;
                desktop.MainWindow = window;
                window.Show();
                previous?.Close();
            }
            void ShowChooser()
            {
                var chooser = new JobChooserWindow();
                chooser.JobSelected += type =>
                {
                    var fast = new FastCaptureWindow(type, _windowScope!.ServiceProvider.GetRequiredService<FastCaptureController>());
                    fast.ReturnRequested += ShowChooser;
                    ReplaceWindow(fast);
                };
                chooser.LegacySelected += () => ShowRosterWorkspace(false);
                chooser.TeamVolumeSelected += () => ShowRosterWorkspace(true);
                chooser.ResetLocalDataRequested += async () =>
                {
                    var dialog = new ResetLocalDataWindow(appDataPath);
                    if (!await dialog.ShowDialog<bool>(chooser)) return;
                    chooser.DisableJobOptions();
                    try
                    {
                        _windowScope?.Dispose();
                        _windowScope = null;
                        _serviceProvider?.Dispose();
                        _serviceProvider = null;
                        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                        LocalCaptureDataReset.Delete(appDataPath);
                        desktop.Shutdown();
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        await ResetLocalDataWindow.ShowFailureAsync(chooser, exception.Message);
                    }
                };
                void ShowRosterWorkspace(bool teamVolume)
                {
                    var window = new MainWindow(
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Rosters.IRosterImportService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Capture.ICaptureWorkflowService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageIngestService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageReviewService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageAssetStore>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IImageAssociationExportService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.Rosters.IRosterExportService>(),
                        _windowScope.ServiceProvider.GetRequiredService<WindowsPortraitFaceDetector>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IJobMediaService>(),
                        _windowScope.ServiceProvider.GetRequiredService<PilotCapture.Application.IOriginalPublicationService>(), teamVolume);
                    window.ReturnRequested += ShowChooser;
                    ReplaceWindow(window);
                }
                if (desktop.MainWindow is null) desktop.MainWindow = chooser;
                else ReplaceWindow(chooser);
            }
            desktop.Exit += (_, _) =>
            {
                _windowScope?.Dispose();
                _serviceProvider?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}


