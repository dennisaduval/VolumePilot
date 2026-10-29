using Avalonia;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Controls.Templates;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;
namespace PilotCapture.Desktop;

public sealed partial class FastCaptureWindow : Window
{
    private readonly CaptureJobType _type;
    private readonly FastCaptureController _controller;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private IReadOnlyList<FastCaptureJob> _jobs = [];
    private FastCaptureSnapshot? _snapshot;
    private bool _busy, _refreshing, _settingChoices, _closed;
    private TaskCompletionSource<bool>? _refreshCompletion;
    private string? _jobId, _displayedImageId, _displaySignature, _actionError;
    private Bitmap? _bitmap;
    private readonly SemaphoreSlim _previewLock = new(1, 1);
    private long _previewRequest;
    private bool _previewInverted;
    private string? _slotSignature;
    private IReadOnlyList<FastCaptureTeam> _visibleTeams = [];
    private string? _scheduleSignature;
    private string? _actionProgressJob;
    private DateTime _nextActionRefresh;
    public event Action? ReturnRequested;
    private bool UsesPlayers => _type == CaptureJobType.PrintOnSiteTeamAndIndividual;
    public FastCaptureWindow(CaptureJobType type, FastCaptureController controller)
    {
        _type = type; _controller = controller; _controller.ActionMode = type == CaptureJobType.ActionPhotography; InitializeComponent();
        ActionSetup.IsVisible = _controller.ActionMode;
        ImagesList.ItemTemplate = new FuncDataTemplate<FastCaptureImage>((image, _) => image is null ? null : ImageRow(image));
        TeamSuggestions.PlacementTarget = TeamName;
        PerformanceSuggestions.AddHandler(InputElement.PointerReleasedEvent, OnSuggestionPicked, RoutingStrategies.Bubble, handledEventsToo: true);
        PerformanceSuggestions.ItemTemplate = new FuncDataTemplate<FastCaptureTeam>((team, _) => new TextBlock
            { Text = team is null ? "" : FastPerformanceSearch.Label(team), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), MinHeight = 44 });
        ModeTitle.Text = type switch { CaptureJobType.PrintOnSiteTeamsOnly => "Print on site · Team Only", CaptureJobType.ActionPhotography => "Action · automatic image routing", _ => "Print on site · Team and Individuals" };
        PlayerPanel.IsVisible = PrimaryButton.IsVisible = IndividualsSetup.IsVisible = UsesPlayers;
        PreviewPanel.IsVisible = type != CaptureJobType.ActionPhotography;
        if (!UsesPlayers) { Grid.SetColumn(RejectButton, 0); Grid.SetColumnSpan(RejectButton, 2); }
        if (!UsesPlayers) { Grid.SetColumn(CaptureControls, 0); Grid.SetColumnSpan(CaptureControls, 2); }
        if (type == CaptureJobType.ActionPhotography) Grid.SetColumnSpan(CaptureControls, 3);
        _timer.Tick += async (_, _) => { if (!_busy && !_refreshing && _jobId is not null) await RefreshAsync(); };
        Loaded += async (_, _) => { await LoadJobsAsync(); _timer.Start(); };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _controller.Dispose(); Interlocked.Increment(ref _previewRequest); Preview.Source = null; Closeup.Source = null; _bitmap?.Dispose(); };
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; _actionError = null; UpdateButtons();
        try { await action(); await RefreshAfterActionAsync(); }
        catch (Exception exception)
        {
            _actionError = exception.Message;
            // Restore the actual persisted selection if an in-flight camera file
            // prevents the requested team/player boundary from completing.
            await RefreshAfterActionAsync();
            Status.Text = _actionError;
        }
        finally { _busy = false; UpdateButtons(); }
    }
    private async Task LoadJobsAsync()
    {
        _jobs = await _controller.ExecuteAsync(s => s.GetJobsAsync(_type));
        _settingChoices = true; SavedJobs.ItemsSource = _jobs.Select(x => x.Name).ToArray(); SavedJobs.SelectedIndex = _jobs.Count > 0 ? 0 : -1; _settingChoices = false;
        if (_jobs.Count > 0)
        {
            var jobId = _jobs[0].Id; _jobId = jobId;
            await RunAsync(() => UsesPlayers ? _controller.ExecuteAsync(s => s.RepairPrimaryFilenameAsync(jobId)) : Task.CompletedTask);
            SetupPanel.IsExpanded = false;
        }
    }
    private async Task RefreshAfterActionAsync()
    {
        // A timer refresh may already be decoding an older snapshot. Finish it,
        // then read the state after this action instead of silently skipping it.
        while (_refreshCompletion is { } pending) await pending.Task;
        await RefreshAsync();
    }
    private async Task RefreshAsync()
    {
        if (_jobId is null || _refreshing || _closed) return;
        _refreshing = true;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _refreshCompletion = completion;
        try
        {
            var selected = SelectedImage()?.Id;
            var previousNewest = _snapshot?.Images.FirstOrDefault()?.Id;
            var requestedJob = _jobId;
            var snapshot = await _controller.ExecuteAsync(s => s.GetSnapshotAsync(requestedJob));
            if (_closed || _jobId != requestedJob) return;
            _snapshot = snapshot;
            var job = _snapshot.Job;
            JobName.Text = job.Name; SourcePath.Text = job.SourceFolder; OutputPath.Text = job.OutputFolder; IndividualsPath.Text = job.IndividualsFolder;
            _settingChoices = true;
            UpdateScheduleFilters();
            UpdatePerformanceChoices();
            _settingChoices = false;
            var team = _snapshot.Teams.FirstOrDefault(x => x.Id == job.CurrentTeamId);
            PlayerTeamName.Text = team?.Name ?? "Select a team";
            CurrentPlayer.Text = team is null ? "" : $"Player {team.CurrentPlayer}";
            RenderPlayerSlots(team);
            ImageListTitle.Text = UsesPlayers && team is not null ? $"Player {team.CurrentPlayer} images" : "Incoming images";
            PhotoCount.Text = UsesPlayers && team is not null ? $"{_snapshot.Images.Count} image(s) · Player {team.CurrentPlayer}" : $"{_snapshot.Images.Count} recent image(s)";
            if (_controller.ActionMode && (_actionProgressJob != job.Id || DateTime.UtcNow >= _nextActionRefresh))
            {
                var action = await _controller.ActionAsync(s => s.GetStatusAsync(job.Id));
                if (_closed || _jobId != job.Id) return;
                _actionProgressJob = job.Id; _nextActionRefresh = DateTime.UtcNow.AddSeconds(1);
                ActionClockStatus.Text = _controller.ActionConnectionStatus + "\n" + string.Join(" · ", action.Cameras.Select(camera =>
                {
                    var clock = action.Clocks.Where(c => c.CameraKey == camera.CameraKey && c.UntilUtcTicks > DateTime.UtcNow.Ticks).OrderByDescending(c => c.FromUtcTicks).FirstOrDefault();
                    return $"{(camera.Name.Length > 0 ? camera.Name : camera.CameraKey)}: " + (clock is null ? "verification needed" : $"verified, correction {TimeSpan.FromTicks(clock.OffsetTicks).TotalSeconds:0.0}s") +
                        (camera.Issue.Length > 0 ? " (" + camera.Issue + ")" : "");
                }));
                ActionTransferStatus.Text = $"{action.Progress.Sum(p => p.Delivered)} delivered · {action.Progress.Sum(p => p.Pending)} pending · {action.HeldCount} held for review · {_controller.QueuedActionFiles} incoming files queued. Camera buffer total is unknown.\n" +
                    string.Join(" · ", action.Progress.Where(p => p.Pending > 0 || p.TeamId == job.CurrentTeamId).OrderBy(p => p.TeamId == job.CurrentTeamId ? 0 : 1).ThenByDescending(p => p.Pending).Take(8)
                        .Select(p => $"{p.Team}: {p.Delivered} delivered, {p.Pending} pending"));
            }
            var signature = job.CurrentTeamId + ":" + (UsesPlayers ? team?.CurrentPlayer : 0) + "|" +
                string.Join('|', _snapshot.Images.Select(x => $"{x.Id}:{x.IsPrimary}:{x.IsRejected}:{x.Released}:{x.NeedsDelivery}:{x.IndividualDeliveredPath}:{x.DeliveredPath}"));
            if (_displaySignature != signature)
            {
                _displaySignature = signature;
                _settingChoices = true;
                ImagesList.ItemsSource = _snapshot.Images;
                var index = previousNewest != _snapshot.Images.FirstOrDefault()?.Id ? 0 : _snapshot.Images.ToList().FindIndex(x => x.Id == selected);
                ImagesList.SelectedIndex = _snapshot.Images.Count > 0 ? Math.Max(0, index) : -1;
                _settingChoices = false;
                await ShowPreviewAsync();
            }
            if ((_controller.Error ?? _actionError) is { } error) Status.Text = error;
            else Status.Text = _controller.IsRunning ? $"Watching · {team?.Name ?? "select a team"} · photos route automatically" : "Paused · select a team and start watching.";
            UpdateButtons();
        }
        catch (Exception exception) { Status.Text = exception.Message; }
        finally { _refreshing = false; _refreshCompletion = null; completion.TrySetResult(true); }
    }
    private FastCaptureImage? SelectedImage() => ImagesList.SelectedIndex >= 0 && _snapshot is not null && ImagesList.SelectedIndex < _snapshot.Images.Count ? _snapshot.Images[ImagesList.SelectedIndex] : null;
    private void UpdateButtons()
    {
        MonitorButton.Content = _controller.IsRunning ? "Pause watching" : "Start watching";
        MonitorButton.IsEnabled = !_busy && _jobId is not null && _snapshot?.Job.CurrentTeamId is not null && (!_controller.ActionMode || _snapshot.Job.ActionTimingEnabled);
        NextPlayerButton.IsEnabled = !_busy && _snapshot?.Job.CurrentTeamId is not null;
        PrimaryButton.IsEnabled = !_busy && UsesPlayers && SelectedImage() is not null;
        RejectButton.IsEnabled = !_busy && SelectedImage() is not null;
        RejectButton.Content = SelectedImage()?.IsRejected == true ? "Restore" : "Reject";
        PreviewImageStatus.Text = SelectedImage() is { } selected ? ImageState(selected) : "No image selected";
        SaveJobButton.IsEnabled = !_busy && _jobId is null;
        ImportRosterButton.IsEnabled = !_busy && _jobId is not null;
        NextPerformanceButton.IsEnabled = !_busy && _snapshot is { Teams.Count: > 0 };
        PlayerSlots.IsEnabled = !_busy;
        ScheduleDays.IsEnabled = ScheduleHalls.IsEnabled = !_busy;
        ConnectActionButton.IsEnabled = VerifyActionButton.IsEnabled = ReviewActionButton.IsEnabled = !_busy && _jobId is not null;
    }
    private async Task ShowPreviewAsync()
    {
        if (_type == CaptureJobType.ActionPhotography) return;
        var image = SelectedImage();
        if (image?.Id == _displayedImageId) return;
        _displayedImageId = image?.Id;
        var request = Interlocked.Increment(ref _previewRequest);
        var inverted = _previewInverted;
        Preview.Source = null; Closeup.Source = null; _bitmap?.Dispose(); _bitmap = null;
        if (image is null) return;
        // All IO/EXIF decode/bitmap work is independent of the ingest controller.
        // Serialize decoding and skip obsolete selections before doing expensive work.
        var bitmap = await Task.Run(async () =>
        {
            await _previewLock.WaitAsync();
            try
            {
                if (Interlocked.Read(ref _previewRequest) != request) return null;
                var bytes = await File.ReadAllBytesAsync(image.StoredPath);
                var portrait = await PortraitImageDecoder.DecodeAsync(bytes, 2400, inverted);
                using var source = new MemoryStream(portrait);
                return new Bitmap(source);
            }
            finally { _previewLock.Release(); }
        });
        if (_closed || Interlocked.Read(ref _previewRequest) != request) { bitmap?.Dispose(); return; }
        _bitmap = bitmap; Preview.Source = bitmap; Closeup.Source = bitmap;
    }
    private void OnCloseupZoomChanged(object? sender, RangeBaseValueChangedEventArgs e)
    { if (Closeup is not null) Closeup.Zoom = e.NewValue; }
    private void PositionCloseup(Point point)
    {
        if (_bitmap is not null && PortraitCropView.ImagePoint(_bitmap.Size, Preview.Bounds.Size, point) is { } center)
            Closeup.Center = center;
    }
    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    { PositionCloseup(e.GetPosition(Preview)); e.Handled = true; }
    private void OnPreviewPointerMoved(object? sender, PointerEventArgs e)
    { if (e.GetCurrentPoint(Preview).Properties.IsLeftButtonPressed) PositionCloseup(e.GetPosition(Preview)); }
    private async void OnInvertPreviewClick(object? sender, RoutedEventArgs e)
    {
        _previewInverted = !_previewInverted; _displayedImageId = null;
        try { await ShowPreviewAsync(); } catch (Exception ex) { Status.Text = ex.Message; }
    }
    private async void OnCreateJobClick(object? sender, RoutedEventArgs e) => await CreateConfiguredJobAsync();
    public Task CreateConfiguredJobAsync() => RunAsync(async () =>
    {
        Dispatcher.UIThread.VerifyAccess();
        var settings = new FastJobSettings(_type, JobName.Text ?? "", SourcePath.Text ?? "", OutputPath.Text ?? "", IndividualsPath.Text);
        _jobId = await _controller.ExecuteAsync(s => s.CreateJobAsync(settings));
        SetupPanel.IsExpanded = false;
    });
    private async void OnSavedJobChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || SavedJobs.SelectedIndex < 0) return;
        var jobId = _jobs[SavedJobs.SelectedIndex].Id;
        await RunAsync(async () =>
        {
            await _controller.StopAsync(); await _controller.DisconnectActionAsync(); _jobId = jobId; _displaySignature = null; ClearTeamSearch();
            if (UsesPlayers) await _controller.ExecuteAsync(s => s.RepairPrimaryFilenameAsync(jobId));
        });
    }
    private async void OnNewJobClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _controller.StopAsync(); await _controller.DisconnectActionAsync(); _jobId = null; _snapshot = null; _displaySignature = null; JobName.Text = ""; SetupPanel.IsExpanded = true;
        _settingChoices = true; Teams.ItemsSource = null; ImagesList.ItemsSource = null; _settingChoices = false;
        Interlocked.Increment(ref _previewRequest); Preview.Source = null; Closeup.Source = null; _bitmap?.Dispose(); _bitmap = null; _displayedImageId = null;
        CurrentPlayer.Text = PhotoCount.Text = ActivePerformance.Text = ""; PlayerSlots.Children.Clear(); _slotSignature = null; ClearTeamSearch();
        ScheduleFilters.IsVisible = false; _scheduleSignature = null;
    });
    private async void OnSwitchTeamClick(object? sender, RoutedEventArgs e) => await SelectOrAddTeamAsync();
    private async void OnTeamChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || _snapshot is null || Teams.SelectedIndex < 0 || Teams.SelectedIndex >= _visibleTeams.Count) return;
        var jobId = _snapshot.Job.Id; var teamId = _visibleTeams[Teams.SelectedIndex].Id;
        await SelectPerformanceAsync(jobId, teamId);
    }
    private Task SelectPerformanceAsync(string jobId, string teamId)
    {
        var at = DateTime.UtcNow.Ticks;
        return RunAsync(async () =>
        {
            await _controller.ExecuteAsync(s => s.SelectPerformanceAsync(jobId, teamId, changeoverUtcTicks: at));
            ClearTeamSearch();
        });
    }
    private Task SelectOrAddTeamAsync() => RunAsync(async () =>
    {
        if (_snapshot is not { } snapshot) return;
        var at = DateTime.UtcNow.Ticks;
        var name = TeamName.Text?.Trim() ?? "";
        if (name.Length == 0) throw new ArgumentException("Type a team name or performance number.");
        var matches = FastPerformanceSearch.Find(ScheduleTeams(snapshot), name);
        if (matches.Count > 1) throw new ArgumentException("Several teams match. Choose the performance / division in the list.");
        if (matches.Count == 1) await _controller.ExecuteAsync(s => s.SelectPerformanceAsync(snapshot.Job.Id, matches[0].Id, changeoverUtcTicks: at));
        else await _controller.ExecuteAsync(s => s.SelectTeamAsync(snapshot.Job.Id, name, changeoverUtcTicks: at));
        ClearTeamSearch();
    });
    private void OnTeamSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_settingChoices || _snapshot is null) return;
        UpdatePerformanceChoices();
        TeamSuggestions.Width = Math.Max(300, TeamName.Bounds.Width);
        TeamSuggestions.IsOpen = !string.IsNullOrWhiteSpace(TeamName.Text) && _visibleTeams.Count > 0 && IsVisible;
    }
    private async void OnTeamSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            if (PerformanceSuggestions.SelectedItem is FastCaptureTeam selected && _snapshot is { } snapshot)
                await SelectPerformanceAsync(snapshot.Job.Id, selected.Id);
            else await SelectOrAddTeamAsync();
        }
        else if (e.Key == Key.Down || e.Key == Key.Up)
        {
            e.Handled = true;
            if (_visibleTeams.Count > 0) PerformanceSuggestions.SelectedIndex = Math.Clamp(
                PerformanceSuggestions.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, _visibleTeams.Count - 1);
        }
        else if (e.Key == Key.Escape) { ClearTeamSearch(); UpdatePerformanceChoices(); e.Handled = true; }
    }
    private async void OnSuggestionPicked(object? sender, PointerReleasedEventArgs e)
    {
        if (PerformanceSuggestions.SelectedItem is FastCaptureTeam selected && _snapshot is { } snapshot)
        { e.Handled = true; await SelectPerformanceAsync(snapshot.Job.Id, selected.Id); }
    }
    private async void OnSuggestionKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PerformanceSuggestions.SelectedItem is FastCaptureTeam selected && _snapshot is { } snapshot)
        { e.Handled = true; await SelectPerformanceAsync(snapshot.Job.Id, selected.Id); }
    }
    private void ClearTeamSearch()
    {
        var setting = _settingChoices; _settingChoices = true;
        TeamName.Text = ""; Teams.Tag = null; Teams.IsDropDownOpen = false; TeamSuggestions.IsOpen = false;
        _settingChoices = setting;
    }
    private void UpdatePerformanceChoices()
    {
        if (_snapshot is not { } snapshot) return;
        var query = TeamName.Text ?? "";
        var schedule = ScheduleTeams(snapshot);
        var signature = snapshot.Job.CurrentTeamId + "|" + query + "|" + string.Join('|', schedule.Select(t => $"{t.Id}:{t.SortOrder}:{t.Name}:{t.PerformanceLabel}:{t.Details}"));
        ActivePerformance.Text = "Current: " + (snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId) is { } current ? FastPerformanceSearch.Label(current) : "select a team / performance");
        if (Teams.Tag as string == signature &&
            Teams.SelectedIndex == _visibleTeams.ToList().FindIndex(t => t.Id == snapshot.Job.CurrentTeamId)) return;
        var setting = _settingChoices; _settingChoices = true;
        _visibleTeams = FastPerformanceSearch.Find(schedule, query);
        Teams.ItemsSource = _visibleTeams.Select(t => new ComboBoxItem { Content = new TextBlock
            { Text = FastPerformanceSearch.Label(t), TextWrapping = TextWrapping.Wrap, FontSize = 20 } }).ToArray();
        Teams.SelectedIndex = _visibleTeams.ToList().FindIndex(t => t.Id == snapshot.Job.CurrentTeamId);
        PerformanceSuggestions.ItemsSource = _visibleTeams;
        PerformanceSuggestions.SelectedIndex = -1;
        Teams.PlaceholderText = _visibleTeams.Count == 0 ? "No matches. Add a new team, or change your search." : "Choose a matching team / performance";
        Teams.Tag = signature; _settingChoices = setting;
    }
    private static IReadOnlyList<FastCaptureTeam> ScheduleTeams(FastCaptureSnapshot snapshot)
    {
        var current = snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId);
        return FastPerformanceSearch.Schedule(snapshot.Teams, current?.Day ?? "", current?.Hall ?? "");
    }
    private static string[] Choices(IEnumerable<string> values) => values.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    private static ComboBoxItem ScheduleItem(string value) => new() { Content = value.Length == 0 ? "Unspecified" : value, Tag = value };
    private void UpdateScheduleFilters()
    {
        if (_snapshot is not { } snapshot) return;
        ScheduleFilters.IsVisible = snapshot.Teams.Any(t => t.Day.Length > 0 || t.Hall.Length > 0);
        var current = snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId);
        var day = current?.Day ?? ""; var hall = current?.Hall ?? "";
        var signature = snapshot.Job.Id + "|" + day + "|" + hall + "|" + string.Join('|', snapshot.Teams.Select(t => t.Day + ":" + t.Hall));
        if (_scheduleSignature == signature && ((ScheduleDays.SelectedItem as ComboBoxItem)?.Tag as string) == day
            && ((ScheduleHalls.SelectedItem as ComboBoxItem)?.Tag as string) == hall) return;
        var setting = _settingChoices; _settingChoices = true;
        var days = Choices(snapshot.Teams.Select(t => t.Day));
        var halls = Choices(snapshot.Teams.Where(t => t.Day.Equals(day, StringComparison.OrdinalIgnoreCase)).Select(t => t.Hall));
        ScheduleDays.ItemsSource = days.Select(ScheduleItem).ToArray(); ScheduleDays.SelectedIndex = Array.FindIndex(days, d => d.Equals(day, StringComparison.OrdinalIgnoreCase));
        ScheduleHalls.ItemsSource = halls.Select(ScheduleItem).ToArray(); ScheduleHalls.SelectedIndex = Array.FindIndex(halls, h => h.Equals(hall, StringComparison.OrdinalIgnoreCase));
        _scheduleSignature = signature; _settingChoices = setting;
    }
    private async void OnScheduleDayChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || _snapshot is not { } snapshot || ScheduleDays.SelectedItem is not ComboBoxItem { Tag: string day }) return;
        var current = snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId);
        var halls = Choices(snapshot.Teams.Where(t => t.Day.Equals(day, StringComparison.OrdinalIgnoreCase)).Select(t => t.Hall));
        var hall = halls.FirstOrDefault(h => h.Equals(current?.Hall, StringComparison.OrdinalIgnoreCase)) ?? halls.FirstOrDefault();
        if (hall is null) return;
        var at = DateTime.UtcNow.Ticks;
        await RunAsync(async () => { await _controller.ExecuteAsync(s => s.SelectScheduleAsync(snapshot.Job.Id, day, hall, changeoverUtcTicks: at)); ClearTeamSearch(); });
    }
    private async void OnScheduleHallChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || _snapshot is not { } snapshot || ScheduleHalls.SelectedItem is not ComboBoxItem { Tag: string hall }) return;
        var day = snapshot.Teams.FirstOrDefault(t => t.Id == snapshot.Job.CurrentTeamId)?.Day ?? "";
        var at = DateTime.UtcNow.Ticks;
        await RunAsync(async () => { await _controller.ExecuteAsync(s => s.SelectScheduleAsync(snapshot.Job.Id, day, hall, changeoverUtcTicks: at)); ClearTeamSearch(); });
    }
    private static string ImageState(FastCaptureImage image) =>
        (image.PlayerNumber > 0 ? $"Player {image.PlayerNumber} · " : "") + $"{image.Sequence:0000} · " +
        (image.IsRejected ? "REJECTED" : image.IsPrimary ? "PRIMARY" : "Accepted") + (image.NeedsDelivery ? " · pending" : "");
    private static Control ImageRow(FastCaptureImage image)
    {
        var row = new StackPanel { Spacing = 4, Margin = new Thickness(0, 6) };
        var output = image.IndividualDeliveredPath ?? image.DeliveredPath;
        row.Children.Add(new TextBlock { Text = output is null ? "Awaiting output" : Path.GetFileName(output), TextWrapping = TextWrapping.Wrap, FontSize = 17 });
        row.Children.Add(new TextBlock { Text = "Camera: " + Path.GetFileName(image.SourcePath), TextWrapping = TextWrapping.Wrap, FontSize = 14 });
        row.Children.Add(new TextBlock { Text = ImageState(image), TextWrapping = TextWrapping.Wrap, FontSize = 15,
            FontWeight = FontWeight.SemiBold, Foreground = image.IsRejected ? Brushes.Firebrick : Brushes.DarkBlue });
        ToolTip.SetTip(row, (output ?? "No output copy acknowledged yet") + "\nCamera: " + Path.GetFileName(image.SourcePath));
        return row;
    }
    private async void OnConnectActionClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_jobId is { } jobId) await _controller.ConnectActionAsync(jobId);
    });
    private void OnVerifyActionClick(object? sender, RoutedEventArgs e) => _controller.VerifyActionCameras(RepeatActionClockChecks.IsChecked == true);
    private void OnRepeatActionClockChecksChanged(object? sender, RoutedEventArgs e) => _controller.SetRepeatActionClockChecks((sender as CheckBox)?.IsChecked == true);
    private async void OnReviewActionClick(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is not { } snapshot) return;
        var review = new ActionHeldWindow(_controller, snapshot);
        await review.ShowDialog(this);
        await RefreshAfterActionAsync();
    }
    private async void OnMonitorClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_controller.IsRunning) await _controller.StopAsync();
        else if (_snapshot is { } snapshot)
        {
            var includeExisting = IncludeExistingPhotos.IsChecked == true;
            if (includeExisting) await _controller.ExecuteAsync(s => s.IncludeExistingAsync(snapshot.Job.Id));
            await _controller.StartAsync(snapshot.Job.Id, snapshot.Job.SourceFolder);
        }
    });
    private async void OnNextPlayerClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (_jobId is not null) await _controller.ExecuteAsync(s => s.NextPlayerAsync(_jobId)); });
    private async void OnPrimaryClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (SelectedImage() is { } image) await _controller.ExecuteAsync(s => s.SetPrimaryAsync(image.Id)); });
    private async void OnRejectClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (SelectedImage() is { } image) await _controller.ExecuteAsync(s => s.ReviewAsync(image.Id, !image.IsRejected)); });
    private async void OnImageChanged(object? sender, SelectionChangedEventArgs e)
    { if (!_settingChoices) { try { await ShowPreviewAsync(); UpdateButtons(); } catch (Exception ex) { Status.Text = ex.Message; } } }
    private async void OnReturnClick(object? sender, RoutedEventArgs e) => await RunAsync(async () => { await _controller.StopAsync(); ReturnRequested?.Invoke(); });
    private async Task PickAsync(TextBox box)
    {
        if (_jobId is not null) { Status.Text = "Choose New job to configure different folders."; return; }
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        if (folders.Count > 0) box.Text = folders[0].TryGetLocalPath();
    }
    private async void OnSourceFolderClick(object? sender, RoutedEventArgs e) => await PickAsync(SourcePath);
    private async void OnOutputFolderClick(object? sender, RoutedEventArgs e) => await PickAsync(OutputPath);
    private async void OnIndividualsFolderClick(object? sender, RoutedEventArgs e) => await PickAsync(IndividualsPath);
    private void RenderPlayerSlots(FastCaptureTeam? team)
    {
        if (!UsesPlayers || _snapshot is null) return;
        var signature = team?.Id + ":" + team?.CurrentPlayer + ":" + string.Join(',', _snapshot.UsedPlayers);
        if (_slotSignature == signature) return;
        _slotSignature = signature; PlayerSlots.Children.Clear();
        if (team is null) return;
        var maximum = Math.Max(40, Math.Max(team.CurrentPlayer, _snapshot.UsedPlayers.DefaultIfEmpty(0).Max()));
        maximum = ((maximum + 9) / 10) * 10;
        for (var n = 1; n <= maximum; n++)
        {
            var used = _snapshot.UsedPlayers.Contains(n); var current = team.CurrentPlayer == n;
            var button = new Button { Tag = n, Content = $"{(used ? "✓ " : "")}Player {n}", Width = 84, MinHeight = 48,
                IsEnabled = true, FontWeight = current ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal };
            if (current) { button.Background = Avalonia.Media.Brushes.DarkBlue; button.Foreground = Avalonia.Media.Brushes.White; }
            ToolTip.SetTip(button, used ? $"Reopen Player {n} to review or add photos" : $"Select Player {n}");
            button.Click += OnPlayerSlotClick; PlayerSlots.Children.Add(button);
        }
    }
    private async void OnPlayerSlotClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: int number } || _jobId is not { } jobId) return;
        await RunAsync(() => _controller.ExecuteAsync(s => s.SelectPlayerAsync(jobId, number)));
    }
    private async void OnNextPerformanceClick(object? sender, RoutedEventArgs e)
    {
        if (_snapshot is not { } snapshot) return;
        var at = DateTime.UtcNow.Ticks;
        await RunAsync(async () => { await _controller.ExecuteAsync(s => s.NextPerformanceAsync(snapshot.Job.Id, changeoverUtcTicks: at)); ClearTeamSearch(); });
    }
    private async void OnImportRosterClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_jobId is not { } jobId) throw new InvalidOperationException("Create or resume a job before importing its roster.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Choose roster / performance order CSV", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] });
        if (files.Count == 0) return;
        await using var input = await files[0].OpenReadAsync(); using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        var mapping = new FastRosterMappingWindow(jobId, files[0].Name, buffer.ToArray());
        var command = await mapping.ShowDialog<FastRosterImportCommand?>(this);
        if (command is not null) await ImportRosterCoreAsync(command);
    });
    public Task ImportConfiguredRosterAsync(FastRosterImportCommand command) => RunAsync(() => ImportRosterCoreAsync(command));
    private async Task ImportRosterCoreAsync(FastRosterImportCommand command)
    {
        var result = await _controller.ExecuteAsync(s => s.ImportRosterAsync(command));
        _actionError = $"Imported {result.Teams} teams / performances from {result.Rows} source rows.";
    }

}
