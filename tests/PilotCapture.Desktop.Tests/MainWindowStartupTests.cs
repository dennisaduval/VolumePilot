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

                    // Exercise the fast print screen's real native orientation decode and
                    // shared bitmap, not only the standalone decoder helper.
                    File.WriteAllBytes(Path.Combine(incoming, "portrait-preview-" + new string('x', 70) + ".jpg"), Convert.FromBase64String(PortraitPreviewTests.LandscapeJpeg));
                    Complete(controller.ExecuteAsync(service => service.ScanAsync(jobId)));
                    var schedule = new PilotCapture.Application.FastCapture.FastRosterImportCommand(jobId, "schedule.csv",
                        System.Text.Encoding.UTF8.GetBytes("TEAMNAME,ORDER\r\nFalcons,2\r\nBears,1\r\n"), 0, null, 1);
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    var fullPreview = portraitFast.FindControl<Image>("Preview")!;
                    var closeup = portraitFast.FindControl<PortraitCropView>("Closeup")!;
                    Assert.True(fullPreview.Source is not null, $"Print preview missing. Status: {portraitFast.FindControl<TextBlock>("Status")!.Text}; items: {portraitFast.FindControl<ListBox>("ImagesList")!.Items.Count}; selection: {portraitFast.FindControl<ListBox>("ImagesList")!.SelectedIndex}");
                    Assert.Same(fullPreview.Source, closeup.Source);
                    Assert.Contains("PRIMARY", portraitFast.FindControl<TextBlock>("PreviewImageStatus")!.Text!);
                    var imageList = portraitFast.FindControl<ListBox>("ImagesList")!;
                    var previewPhoto = imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single();
                    var imageRow = Assert.IsType<StackPanel>(imageList.ItemTemplate!.Build(previewPhoto));
                    imageRow.Measure(new Size(280, double.PositiveInfinity)); imageRow.Arrange(new Rect(0, 0, 280, imageRow.DesiredSize.Height));
                    var fileLabel = Assert.IsType<TextBlock>(imageRow.Children[0]);
                    var stateLabel = Assert.IsType<TextBlock>(imageRow.Children[1]);
                    Assert.Contains(new string('x', 70), fileLabel.Text!);
                    Assert.Equal(Avalonia.Media.TextWrapping.Wrap, fileLabel.TextWrapping);
                    Assert.Contains("PRIMARY", stateLabel.Text!); Assert.True(fileLabel.Bounds.Width <= 280);
                    portraitFast.FindControl<Slider>("CloseupZoom")!.Value = 4;
                    closeup.Center = new Point(0.5, 0.4);
                    File.WriteAllBytes(Path.Combine(incoming, "next-portrait.jpg"), Convert.FromBase64String(PortraitPreviewTests.LandscapeJpeg));
                    Complete(controller.ExecuteAsync(service => service.ScanAsync(jobId)));
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    Assert.Same(fullPreview.Source, closeup.Source);
                    Assert.Equal(4, closeup.Zoom); Assert.Equal(new Point(0.5, 0.4), closeup.Center);

                    // All source columns are visible with optional usage choices, including
                    // unfamiliar names and a title record before the real header.
                    var adaptable = System.Text.Encoding.UTF8.GetBytes("Event title\r\nRun code,Squad,Age band,Organization,Unused\r\n103,Rockets,Junior,Club A,keep\r\n104,Rockets,Senior,Club A,also keep\r\n105,Stars,Senior,Club B,raw\r\n");
                    var mapping = new FastRosterMappingWindow(jobId, "custom.csv", adaptable);
                    mapping.HeaderRow.Value = 2;
                    Assert.Equal(5, mapping.ColumnUses.Count);
                    foreach (var use in mapping.ColumnUses.Values) use.SelectedIndex = 0;
                    mapping.ColumnUses[0].SelectedIndex = 2;
                    mapping.ColumnUses[1].SelectedIndex = 1;
                    mapping.ColumnUses[2].SelectedIndex = mapping.ColumnUses[3].SelectedIndex = 3;
                    var mapped = mapping.BuildCommand();
                    Assert.Equal(2, mapped.HeaderRecordNumber); Assert.Equal(new[] { 2, 3 }, mapped.DetailColumns);
                    mapping.ColumnUses[4].SelectedIndex = 1;
                    Assert.Throws<ArgumentException>(() => mapping.BuildCommand());
                    mapping.ColumnUses[4].SelectedIndex = 0;
                    Complete(portraitFast.ImportConfiguredRosterAsync(mapped)); mapping.Close();
                    var search = portraitFast.FindControl<TextBox>("TeamName")!;
                    var suggestions = portraitFast.FindControl<ComboBox>("Teams")!;
                    search.Text = "rock";
                    PumpUntil(() => suggestions.Items.Count == 2);
                    Assert.Equal(2, suggestions.Items.Count);
                    var labels = suggestions.Items.OfType<ComboBoxItem>().Select(i => Assert.IsType<TextBlock>(i.Content).Text!).ToArray();
                    Assert.Contains(labels, label => label.Contains("Junior")); Assert.Contains(labels, label => label.Contains("Senior"));
                    Assert.Contains("Bears", portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text!);
                    search.Text = "104";
                    PumpUntil(() => suggestions.Items.Count == 1);
                    Assert.Single(suggestions.Items);
                    suggestions.SelectedIndex = 0;
                    PumpUntil(() => search.Text == "" && (portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text ?? "").Contains("104"));
                    Assert.Contains("Senior", portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text!);
                    Assert.Equal("Rockets", portraitFast.FindControl<TextBlock>("PlayerTeamName")!.Text);
                    surface.Measure(new Size(1000, 760)); surface.Arrange(new Rect(0, 0, 1000, 760));
                    var capture = portraitFast.FindControl<Grid>("CaptureControls")!;
                    var previewPanel = portraitFast.FindControl<Border>("PreviewPanel")!;
                    Assert.True(previewPanel.Bounds.X >= capture.Bounds.Right, "Preview must not overlap image filenames / review controls.");
                    Assert.True(capture.Bounds.Width >= 280);
                    Assert.True(portraitFast.FindControl<Button>("PrimaryButton")!.Bounds.Width >= 100);
                    Assert.True(reject.Bounds.Width >= 100);
                    Assert.True(fullPreview.Bounds.Width <= 80);
                    Assert.True(closeup.Bounds.Width > fullPreview.Bounds.Width);

                    // Exercise the production controller/watcher with an existing copied JPEG
                    // whose timestamp is older than this job. No image decoding in Action mode.
                    var actionController = new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>());
                    var action = new FastCaptureWindow(PilotCapture.Domain.CaptureJobType.ActionPhotography, actionController);
                    action.FindControl<TextBox>("JobName")!.Text = "Action watcher UI regression";
                    action.FindControl<TextBox>("SourcePath")!.Text = incoming;
                    action.FindControl<TextBox>("OutputPath")!.Text = ready;
                    Complete(action.CreateConfiguredJobAsync());
                    Complete(controller.ExecuteAsync(async service => { jobId = (await service.GetJobsAsync(PilotCapture.Domain.CaptureJobType.ActionPhotography)).Single().Id; }));
                    Complete(action.ImportConfiguredRosterAsync(new(jobId, "action.csv", System.Text.Encoding.UTF8.GetBytes("TEAMNAME\r\nAction team\r\nNext team\r\n"), 0, null, null)));
                    var photo = Path.Combine(incoming, "copied-old-timestamp.jpg");
                    File.WriteAllBytes(photo, [255,216,255,1]); File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddYears(-1));
                    action.FindControl<Button>("MonitorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => Directory.Exists(Path.Combine(ready, "001_Action team")) && Directory.GetFiles(Path.Combine(ready, "001_Action team"), "*.jpg").Length == 1);
                    using (var writer = new FileStream(Path.Combine(incoming, "still-writing.jpg"), FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        writer.Write(new byte[] { 255, 216, 255, 1 }); writer.Flush();
                        action.FindControl<ComboBox>("Teams")!.SelectedIndex = 1;
                        PumpUntil(() => (action.FindControl<TextBlock>("Status")!.Text ?? "").Contains("still being written", StringComparison.OrdinalIgnoreCase));
                        Assert.Equal(0, action.FindControl<ComboBox>("Teams")!.SelectedIndex);
                    }
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

