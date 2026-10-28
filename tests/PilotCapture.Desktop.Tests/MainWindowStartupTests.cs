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
                var chooser = new JobChooserWindow();
                Assert.Equal("VolumePilot | Pilot Capture", chooser.Title);
                var fast = new FastCaptureWindow(PilotCapture.Domain.CaptureJobType.ActionPhotography,
                    new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>()));
                Assert.False(Assert.IsType<Border>(fast.FindControl<Border>("PreviewPanel")).IsVisible);
                Assert.False(Assert.IsType<Grid>(fast.FindControl<Grid>("PlayerPanel")).IsVisible);
                fast.Close(); chooser.Close();
                var portraitFast = new FastCaptureWindow(PilotCapture.Domain.CaptureJobType.PrintOnSiteTeamAndIndividual,
                    new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>()));
                portraitFast.FindControl<Expander>("SetupPanel")!.IsExpanded = false;
                portraitFast.FindControl<TextBlock>("CurrentPlayer")!.Text = "Falcons · Player 12";

                var surface = Assert.IsType<Grid>(portraitFast.Content);
                surface.Measure(new Size(1000, 760));
                surface.Arrange(new Rect(0, 0, 1000, 760));
                var reject = portraitFast.FindControl<Button>("RejectButton")!;
                var position = reject.TranslatePoint(new Point(0, 0), surface);
                Assert.True(position.HasValue);
                Assert.True(position.Value.Y + reject.Bounds.Height <= surface.Bounds.Height, "Reject must remain within the capture surface at minimum window size.");
                Assert.True(reject.Bounds.Height >= 64);
                var previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new AvaloniaSynchronizationContext(Dispatcher.UIThread, DispatcherPriority.Normal));
                try
                {
                    var incoming = Path.Combine(root, "incoming"); var ready = Path.Combine(root, "ready"); var individuals = Path.Combine(root, "individuals");
                    Directory.CreateDirectory(incoming); Directory.CreateDirectory(ready); Directory.CreateDirectory(individuals);
                    portraitFast.FindControl<TextBox>("JobName")!.Text = "UI thread regression";
                    portraitFast.FindControl<TextBox>("SourcePath")!.Text = incoming;
                    portraitFast.FindControl<TextBox>("OutputPath")!.Text = ready;
                    portraitFast.FindControl<TextBox>("IndividualsPath")!.Text = individuals;
                    Complete(portraitFast.CreateConfiguredJobAsync());
                    string jobId = "";
                    var controller = new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>());
                    Complete(controller.ExecuteAsync(async service =>
                    {
                        jobId = (await service.GetJobsAsync(PilotCapture.Domain.CaptureJobType.PrintOnSiteTeamAndIndividual)).Single().Id;
                    }));
                    Complete(portraitFast.ImportConfiguredRosterAsync(new(jobId, "schedule.csv",
                        System.Text.Encoding.UTF8.GetBytes("TEAMNAME,ORDER\r\nFalcons,2\r\nBears,1\r\n"), 0, null, 1)));
                    var slotButtons = portraitFast.FindControl<WrapPanel>("PlayerSlots")!.Children.OfType<Button>().ToArray();
                    Assert.Equal(40, slotButtons.Length);
                    Assert.Contains("Bears", portraitFast.FindControl<TextBlock>("PlayerTeamName")!.Text!);
                    Assert.True(portraitFast.FindControl<Button>("MonitorButton")!.IsEnabled);
                    Assert.DoesNotContain("calling thread", portraitFast.FindControl<TextBlock>("Status")!.Text ?? "", StringComparison.OrdinalIgnoreCase);

                    // Exercise the production controller/watcher with an existing copied JPEG
                    // whose timestamp is older than this job. No image decoding in Action mode.
                    var actionController = new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>());
                    var action = new FastCaptureWindow(PilotCapture.Domain.CaptureJobType.ActionPhotography, actionController);
                    action.FindControl<TextBox>("JobName")!.Text = "Action watcher UI regression";
                    action.FindControl<TextBox>("SourcePath")!.Text = incoming;
                    action.FindControl<TextBox>("OutputPath")!.Text = ready;
                    Complete(action.CreateConfiguredJobAsync());
                    Complete(controller.ExecuteAsync(async service => { jobId = (await service.GetJobsAsync(PilotCapture.Domain.CaptureJobType.ActionPhotography)).Single().Id; }));
                    Complete(action.ImportConfiguredRosterAsync(new(jobId, "action.csv", System.Text.Encoding.UTF8.GetBytes("TEAMNAME\r\nAction team\r\n"), 0, null, null)));
                    var photo = Path.Combine(incoming, "copied-old-timestamp.jpg");
                    File.WriteAllBytes(photo, [255,216,255,1]); File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddYears(-1));
                    action.FindControl<Button>("MonitorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => Directory.Exists(Path.Combine(ready, "Action team")) && Directory.GetFiles(Path.Combine(ready, "Action team"), "*.jpg").Length == 1);
                    Complete(actionController.StopAsync());
                    action.Close(); controller.Dispose();
                }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
                portraitFast.Close();
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
    private static void PumpUntil(Func<bool> done)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(15))
        { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
        Assert.True(done(), "Timed out waiting for UI/background work.");
    }
    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        Assert.False(task.IsFaulted, task.Exception?.ToString());
        Assert.False(task.IsCanceled);
    }

}

