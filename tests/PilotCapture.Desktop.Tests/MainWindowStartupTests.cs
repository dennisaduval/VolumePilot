using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
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
                    var cameraLabel = Assert.IsType<TextBlock>(imageRow.Children[1]);
                    var stateLabel = Assert.IsType<TextBlock>(imageRow.Children[2]);
                    Assert.EndsWith("_Primary.jpg", fileLabel.Text!);
                    Assert.True(File.Exists(previewPhoto.IndividualDeliveredPath));
                    Assert.Contains(new string('x', 70), cameraLabel.Text!);
                    Assert.Equal(Avalonia.Media.TextWrapping.Wrap, fileLabel.TextWrapping);
                    Assert.Contains("PRIMARY", stateLabel.Text!); Assert.True(fileLabel.Bounds.Width <= 280);
                    // Reproduce an older bare copy while role/rejection/pending flags
                    // stay unchanged. Pressing Primary must repair disk AND refresh its name.
                    var markedOutput = previewPhoto.IndividualDeliveredPath!;
                    var bareOutput = markedOutput[..^"_Primary.jpg".Length] + ".jpg";
                    var cancellationToken = TestContext.Current.CancellationToken;
                    Complete(Task.Run(async () =>
                    {
                        using var repairScope = provider.CreateScope();
                        var repairDb = repairScope.ServiceProvider.GetRequiredService<PilotCaptureDbContext>();
                        var old = await repairDb.FastImages.SingleAsync(p => p.Id == previewPhoto.Id, cancellationToken);
                        File.Move(markedOutput, bareOutput); old.IndividualDeliveredPath = bareOutput;
                        await repairDb.SaveChangesAsync(cancellationToken);
                    }, cancellationToken));
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    var barePhoto = imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single();
                    var bareRow = Assert.IsType<StackPanel>(imageList.ItemTemplate!.Build(barePhoto));
                    Assert.Equal(Path.GetFileName(bareOutput), Assert.IsType<TextBlock>(bareRow.Children[0]).Text);
                    portraitFast.FindControl<Button>("PrimaryButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single().IndividualDeliveredPath == markedOutput
                        && portraitFast.FindControl<Button>("PrimaryButton")!.IsEnabled);
                    var markedPhoto = imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single();
                    var markedRow = Assert.IsType<StackPanel>(imageList.ItemTemplate!.Build(markedPhoto));
                    Assert.EndsWith("_Primary.jpg", Assert.IsType<TextBlock>(markedRow.Children[0]).Text!);
                    Assert.True(File.Exists(markedOutput)); Assert.False(File.Exists(bareOutput));
                    portraitFast.FindControl<Slider>("CloseupZoom")!.Value = 4;
                    closeup.Center = new Point(0.5, 0.4);
                    File.WriteAllBytes(Path.Combine(incoming, "next-portrait.jpg"), Convert.FromBase64String(PortraitPreviewTests.LandscapeJpeg));
                    Complete(controller.ExecuteAsync(service => service.ScanAsync(jobId)));
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    Assert.Same(fullPreview.Source, closeup.Source);
                    Assert.Equal(4, closeup.Zoom); Assert.Equal(new Point(0.5, 0.4), closeup.Center);

                    // Completing a player clears both views. Used slots remain selectable,
                    // and reopening restores only that player's images and primary controls.
                    portraitFast.FindControl<Button>("NextPlayerButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => portraitFast.FindControl<TextBlock>("CurrentPlayer")!.Text == "Player 2" && imageList.Items.Count == 0);
                    Assert.Null(fullPreview.Source); Assert.Null(closeup.Source);
                    var usedPlayer = portraitFast.FindControl<WrapPanel>("PlayerSlots")!.Children.OfType<Button>().Single(b => b.Tag is 1);
                    Assert.True(usedPlayer.IsEnabled); Assert.Contains("✓", usedPlayer.Content!.ToString()!);
                    usedPlayer.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => portraitFast.FindControl<TextBlock>("CurrentPlayer")!.Text == "Player 1" && imageList.Items.Count == 2 && fullPreview.Source is not null);
                    Assert.All(imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>(), p => Assert.Equal(1, p.PlayerNumber));
                    Assert.True(portraitFast.FindControl<Button>("PrimaryButton")!.IsEnabled);
                    var changedPrimaryId = imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().First().Id;
                    portraitFast.FindControl<Button>("PrimaryButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single(p => p.IsPrimary).Id == changedPrimaryId);
                    File.WriteAllBytes(Path.Combine(incoming, "p1-return-extra.jpg"), Convert.FromBase64String(PortraitPreviewTests.LandscapeJpeg));
                    Complete(controller.ExecuteAsync(service => service.ScanAsync(jobId)));
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    Assert.Equal(3, imageList.Items.Count);
                    Assert.Equal(changedPrimaryId, imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>().Single(p => p.IsPrimary).Id);
                    portraitFast.FindControl<Button>("NextPlayerButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => portraitFast.FindControl<TextBlock>("CurrentPlayer")!.Text == "Player 2" && imageList.Items.Count == 0);
                    File.WriteAllBytes(Path.Combine(incoming, "p2-photo.jpg"), Convert.FromBase64String(PortraitPreviewTests.LandscapeJpeg));
                    Complete(controller.ExecuteAsync(service => service.ScanAsync(jobId)));
                    Complete(portraitFast.ImportConfiguredRosterAsync(schedule));
                    Assert.Equal(2, Assert.Single(imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>()).PlayerNumber);
                    portraitFast.FindControl<WrapPanel>("PlayerSlots")!.Children.OfType<Button>().Single(b => b.Tag is 1)
                        .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => portraitFast.FindControl<TextBlock>("CurrentPlayer")!.Text == "Player 1" && imageList.Items.Count == 3 && fullPreview.Source is not null);
                    Assert.All(imageList.ItemsSource!.Cast<PilotCapture.Domain.FastCaptureImage>(), p => Assert.Equal(1, p.PlayerNumber));
                    Assert.Equal("Player 1 images", portraitFast.FindControl<TextBlock>("ImageListTitle")!.Text);

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

                    // Use the real mapping controls and day/hall selectors for a
                    // weekend schedule. Search and Next stay inside the chosen hall.
                    var weekend = new FastRosterMappingWindow(jobId, "weekend.csv", System.Text.Encoding.UTF8.GetBytes(
                        "DAY,HALL,PERFORMANCEORDER,TEAMNAME,PROGRAM,DIVISION,ENTRYTYPE\nSaturday,F,10,Rockets,Club A,Junior,Team\nSaturday,F,2,Stars,Club B,Senior,Team\nSaturday,F,3,Break,,,Break\nSaturday,G,2,Rockets,Club A,Junior,Team\nSunday,F,2,Rockets,Club A,Junior,Team\n"));
                    var weekendCommand = weekend.BuildCommand();
                    Assert.Equal(0, weekendCommand.DayColumn); Assert.Equal(1, weekendCommand.HallColumn); Assert.Equal(6, weekendCommand.EntryTypeColumn);
                    Complete(portraitFast.ImportConfiguredRosterAsync(weekendCommand)); weekend.Close();
                    var days = portraitFast.FindControl<ComboBox>("ScheduleDays")!;
                    var halls = portraitFast.FindControl<ComboBox>("ScheduleHalls")!;
                    Assert.True(portraitFast.FindControl<Grid>("ScheduleFilters")!.IsVisible);
                    days.SelectedIndex = days.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == "Saturday");
                    PumpUntil(() => days.IsEnabled && (portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text ?? "").Contains("Stars"));
                    Assert.Equal("F", (halls.SelectedItem as ComboBoxItem)?.Tag);
                    Assert.Equal(2, suggestions.Items.Count);
                    Assert.All(suggestions.Items.OfType<ComboBoxItem>(), i => Assert.DoesNotContain("Division:", Assert.IsType<TextBlock>(i.Content).Text!));
                    portraitFast.FindControl<Button>("NextPerformanceButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => days.IsEnabled && (portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text ?? "").Contains("10 · Rockets"));
                    halls.SelectedIndex = halls.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == "G");
                    PumpUntil(() => halls.IsEnabled && suggestions.Items.Count == 1 && (portraitFast.FindControl<TextBlock>("ActivePerformance")!.Text ?? "").Contains("2 · Rockets"));
                    search.Text = "rock"; Assert.Single(suggestions.Items);
                    days.SelectedIndex = days.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => (string?)i.Tag == "Sunday");
                    PumpUntil(() => days.IsEnabled && search.Text == "" && (halls.SelectedItem as ComboBoxItem)?.Tag as string == "F");
                    Assert.Single(halls.Items); Assert.Single(suggestions.Items);
                    portraitFast.FindControl<Button>("NextPerformanceButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    PumpUntil(() => (portraitFast.FindControl<TextBlock>("Status")!.Text ?? "").Contains("last performance"));
                    Assert.Equal("Sunday", (days.SelectedItem as ComboBoxItem)?.Tag);
                    surface.Measure(new Size(1000, 760)); surface.Arrange(new Rect(0, 0, 1000, 760));
                    Assert.True(portraitFast.FindControl<Button>("NextPlayerButton")!.Bounds.Height >= 64);

                    // Exercise the production controller/watcher with an existing copied JPEG
                    // whose timestamp is older than this job. No image decoding in Action mode.
                    var actionController = new FastCaptureController(provider.GetRequiredService<IServiceScopeFactory>());
                    var action = new FastCaptureWindow(PilotCapture.Domain.CaptureJobType.ActionPhotography, actionController);
                    try
                    {
                        action.FindControl<TextBox>("JobName")!.Text = "Action watcher UI regression";
                        action.FindControl<TextBox>("SourcePath")!.Text = incoming;
                        action.FindControl<TextBox>("OutputPath")!.Text = ready;
                        Complete(action.CreateConfiguredJobAsync());
                        Complete(controller.ExecuteAsync(async service => { jobId = (await service.GetJobsAsync(PilotCapture.Domain.CaptureJobType.ActionPhotography)).Single().Id; }));
                        var actionSchedule = new PilotCapture.Application.FastCapture.FastRosterImportCommand(jobId, "action.csv", System.Text.Encoding.UTF8.GetBytes("TEAMNAME\r\nAction team\r\nNext team\r\n"), 0, null, null);
                        Complete(action.ImportConfiguredRosterAsync(actionSchedule));
                        Assert.True(action.FindControl<StackPanel>("ActionSetup")!.IsVisible);
                        Assert.False(action.FindControl<Button>("MonitorButton")!.IsEnabled);
                        var actionStart = DateTime.UtcNow.AddMinutes(-5).Ticks;
                        Complete(actionController.ActionAsync(async service =>
                        {
                            await service.EnableAsync(jobId, actionStart);
                            await service.ReceiveSmartShooterAsync(jobId, System.Text.Json.JsonSerializer.Serialize(new { msg_id = "CameraUpdated", CameraKey = "r6-ui", CameraName = "Canon R6 II", CameraStatus = "Ready" }), actionStart);
                            await service.ReceiveSmartShooterAsync(jobId, System.Text.Json.JsonSerializer.Serialize(new { msg_id = "PhotoUpdated", PhotoKey = "clock-ui", CameraKey = "r6-ui", PhotoOrigin = ActionCaptureService.ClockOrigin + "ui",
                                PhotoDateCaptured = new DateTime(actionStart).ToString("yyyyMMdd HH:mm:ss.fff") }), actionStart);
                            await service.VerifyClockAsync(jobId, "clock-ui", actionStart, actionStart + TimeSpan.FromMilliseconds(100).Ticks);
                        }));
                        Complete(action.ImportConfiguredRosterAsync(actionSchedule));
                        Assert.True(action.FindControl<Button>("MonitorButton")!.IsEnabled);
                        Assert.Contains("verified", action.FindControl<TextBlock>("ActionClockStatus")!.Text!);
                        action.Show();
                        PumpUntil(() => action.FindControl<ComboBox>("SavedJobs")!.Items.Count == 1 && action.FindControl<Button>("MonitorButton")!.IsEnabled,
                            () => $"Action loaded={action.IsLoaded}, saved jobs={action.FindControl<ComboBox>("SavedJobs")!.Items.Count}, status={action.FindControl<TextBlock>("Status")!.Text}");
                        var photo = Path.Combine(incoming, "copied-old-timestamp.jpg");
                        File.WriteAllBytes(photo, [255,216,255,1]); File.SetLastWriteTimeUtc(photo, DateTime.UtcNow.AddYears(-1));
                        void Metadata(string path, string key, long time, string camera = "r6-ui") => Complete(actionController.ActionAsync(service => service.ReceiveSmartShooterAsync(jobId,
                            System.Text.Json.JsonSerializer.Serialize(new { msg_id = "PhotoUpdated", PhotoKey = key, CameraKey = camera, PhotoFilename = path,
                                PhotoDateCaptured = new DateTime(time).ToString("yyyyMMdd HH:mm:ss.fff") }), DateTime.UtcNow.Ticks)));
                        Metadata(photo, "first-ui", actionStart + TimeSpan.FromSeconds(5).Ticks);
                        action.FindControl<Button>("MonitorButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        PumpUntil(() => Directory.Exists(Path.Combine(ready, "001_Action team")) && Directory.GetFiles(Path.Combine(ready, "001_Action team"), "*.jpg").Length == 1);
                        using (var writer = new FileStream(Path.Combine(incoming, "still-writing.jpg"), FileMode.Create, FileAccess.Write, FileShare.Read))
                        {
                            writer.Write(new byte[] { 255, 216, 255, 1 }); writer.Flush();
                            Metadata(Path.Combine(incoming, "still-writing.jpg"), "open-ui", actionStart + TimeSpan.FromSeconds(10).Ticks);
                            action.FindControl<ComboBox>("Teams")!.SelectedIndex = 1;
                            PumpUntil(() => (action.FindControl<TextBlock>("ActivePerformance")!.Text ?? "").Contains("Next team"));
                            Assert.Equal(1, action.FindControl<ComboBox>("Teams")!.SelectedIndex);
                            var late = Path.Combine(incoming, "late-previous-team.jpg"); File.WriteAllBytes(late, [255,216,255,1]);
                            Metadata(late, "late-ui", actionStart + TimeSpan.FromSeconds(15).Ticks);
                            PumpUntil(() => Directory.GetFiles(Path.Combine(ready, "001_Action team"), "*.jpg").Length == 2);
                        }
                        PumpUntil(() => Directory.GetFiles(Path.Combine(ready, "001_Action team"), "*.jpg").Length == 3);
                        var nextPhoto = Path.Combine(incoming, "next-team.jpg"); File.WriteAllBytes(nextPhoto, [255,216,255,1]);
                        Metadata(nextPhoto, "next-ui", DateTime.UtcNow.AddSeconds(5).Ticks);
                        PumpUntil(() => Directory.Exists(Path.Combine(ready, "002_Next team")) && Directory.GetFiles(Path.Combine(ready, "002_Next team"), "*.jpg").Length == 1);
                        var unknown = Path.Combine(incoming, "unverified-camera.jpg"); File.WriteAllBytes(unknown, [255,216,255,1]);
                        Metadata(unknown, "unknown-ui", DateTime.UtcNow.AddSeconds(10).Ticks, "unverified-camera");
                        var heldCount = 0;
                        PumpUntil(() =>
                        {
                            Complete(actionController.ActionAsync(async service => heldCount = (await service.GetStatusAsync(jobId)).HeldCount));
                            return heldCount == 1;
                        });
                        // The headless dispatcher does not run the host's periodic
                        // timer. Use the existing import action to refresh the UI,
                        // just as earlier player/review assertions do above.
                        Complete(action.ImportConfiguredRosterAsync(actionSchedule));
                        Assert.Contains("1 held", action.FindControl<TextBlock>("ActionTransferStatus")!.Text!);
                        Assert.Single(Directory.GetFiles(Path.Combine(ready, "002_Next team"), "*.jpg"));
                    }
                    finally { Complete(action.StopCaptureAsync()); action.Close(); controller.Dispose(); }

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
    private static void PumpUntil(Func<bool> done, Func<string>? diagnostics = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (!done() && elapsed.Elapsed < TimeSpan.FromSeconds(15))
        { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(5); }
        Assert.True(done(), "Timed out waiting for UI/background work. " + diagnostics?.Invoke());
    }
    private static void Complete(Task task)
    {
        PumpUntil(() => task.IsCompleted);
        Assert.False(task.IsFaulted, task.Exception?.ToString());
        Assert.False(task.IsCanceled);
    }

}

