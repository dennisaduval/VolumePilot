using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using PilotCapture.Desktop;
using PilotCapture.Infrastructure.Persistence;
using Xunit;

namespace PilotCapture.Desktop.Tests;

public sealed class MainWindowStartupTests
{
    [Fact]
    public async Task Window_constructor_initializes_named_controls_before_wiring_events()
    {
        // Use the production App resources and real capture services without opening
        // a native window or touching the operator's application data directory.
        var root = Path.Combine(Path.GetTempPath(), $"pilot-capture-startup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var services = new ServiceCollection();
            services.AddPilotCaptureInfrastructure(
                Path.Combine(root, "pilot-capture.db"),
                Path.Combine(root, "media"));
            services.AddSingleton<WindowsPortraitFaceDetector>();
            services.AddTransient<MainWindow>();
            using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>()
                .InitializeAsync(TestContext.Current.CancellationToken);

            var portraitBytes = await PortraitImageDecoder.DecodeAsync(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAQAAAACCAIAAADwyuo0AAAAEElEQVR4nGMwSJgARwzIHAByGgkBW9L7BQAAAABJRU5ErkJggg=="), 100);

            // Keep all UI operations on this thread after initializing the database.
            AppBuilder.Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();

            // This constructor threw at the first named-control event subscription
            // in the CAPTURE-10 preview when only AvaloniaXamlLoader.Load was called.
            var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
            try
            {
                var workflow = Assert.IsType<ComboBox>(window.FindControl<ComboBox>("WorkflowTypeCombo"));
                var station = Assert.IsType<ComboBox>(window.FindControl<ComboBox>("StationCodeCombo"));
                Assert.Equal(2, workflow.Items.Count);
                Assert.Equal(0, workflow.SelectedIndex);
                Assert.Equal(4, station.Items.Count);
                // Headless Avalonia substitutes a 1x1 bitmap. Inspect the real Windows
                // decoder's PNG IHDR instead of testing that rendering placeholder.
                Assert.Equal(2, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(portraitBytes.AsSpan(16, 4)));
                Assert.Equal(4, System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(portraitBytes.AsSpan(20, 4)));
                Assert.IsType<Button>(window.FindControl<Button>("SetSecondaryButton"));
                Assert.IsType<TextBox>(window.FindControl<TextBox>("SubjectSearchBox")).Text = "dav";
                Assert.IsType<TextBox>(window.FindControl<TextBox>("TeamSearchBox")).Text = "team";
                Assert.Equal(3, Assert.IsType<ComboBox>(window.FindControl<ComboBox>("ExportKindCombo")).Items.Count);

                // Exercise the event subscriptions too: these handlers access other
                // generated fields, so merely finding the XAML tree is insufficient.
                Assert.IsType<TextBox>(window.FindControl<TextBox>("EventName")).Text = "Startup test";
                Assert.IsType<TextBox>(window.FindControl<TextBox>("PhotographerName")).Text = "Test photographer";
                Assert.IsType<TextBox>(window.FindControl<TextBox>("ManualFirstName")).Text = "Riley";
                Assert.IsType<TextBox>(window.FindControl<TextBox>("UnidentifiedSubjectName")).Text = "Walk-in 1";
                Dispatcher.UIThread.RunJobs();

                Assert.False(Assert.IsType<Button>(window.FindControl<Button>("ImportRosterButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(window.FindControl<Button>("StartSessionButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(window.FindControl<Button>("CreateManualSubjectButton")).IsEnabled);
                Assert.False(Assert.IsType<Button>(window.FindControl<Button>("CreateUnidentifiedButton")).IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}

