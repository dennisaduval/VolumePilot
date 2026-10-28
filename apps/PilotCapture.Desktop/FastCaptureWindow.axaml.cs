using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Application.Rosters;
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
    private string? _jobId, _displayedImageId, _displaySignature, _actionError;
    private Bitmap? _bitmap;
    private string? _slotSignature;
    private byte[]? _rosterBytes;
    private RosterCsvDocument? _rosterDocument;
    private string _rosterName = "";
    public event Action? ReturnRequested;
    private bool UsesPlayers => _type == CaptureJobType.PrintOnSiteTeamAndIndividual;
    public FastCaptureWindow(CaptureJobType type, FastCaptureController controller)
    {
        _type = type; _controller = controller; InitializeComponent();
        ModeTitle.Text = type switch { CaptureJobType.PrintOnSiteTeamsOnly => "Print on site · Team Only", CaptureJobType.ActionPhotography => "Action · automatic image routing", _ => "Print on site · Team and Individuals" };
        PlayerPanel.IsVisible = PrimaryButton.IsVisible = IndividualsSetup.IsVisible = UsesPlayers;
        PreviewPanel.IsVisible = type != CaptureJobType.ActionPhotography;
        if (!UsesPlayers) { Grid.SetColumn(RejectButton, 0); Grid.SetColumnSpan(RejectButton, 2); }
        if (!UsesPlayers) { Grid.SetColumn(CaptureControls, 0); Grid.SetColumnSpan(CaptureControls, 2); }
        if (type == CaptureJobType.ActionPhotography) Grid.SetColumnSpan(CaptureControls, 3);
        _timer.Tick += async (_, _) => { if (!_busy && !_refreshing && _jobId is not null) await RefreshAsync(); };
        Loaded += async (_, _) => { await LoadJobsAsync(); _timer.Start(); };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _controller.Dispose(); _bitmap?.Dispose(); };
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; _actionError = null; UpdateButtons();
        try { await action(); await RefreshAsync(); }
        catch (Exception exception) { _actionError = exception.Message; Status.Text = _actionError; }
        finally { _busy = false; UpdateButtons(); }
    }
    private async Task LoadJobsAsync()
    {
        _jobs = await _controller.ExecuteAsync(s => s.GetJobsAsync(_type));
        _settingChoices = true; SavedJobs.ItemsSource = _jobs.Select(x => x.Name).ToArray(); SavedJobs.SelectedIndex = _jobs.Count > 0 ? 0 : -1; _settingChoices = false;
        if (_jobs.Count > 0) { _jobId = _jobs[0].Id; await RefreshAsync(); SetupPanel.IsExpanded = false; }
    }
    private async Task RefreshAsync()
    {
        if (_jobId is null || _refreshing || _closed) return;
        _refreshing = true;
        try
        {
            var selected = SelectedImage()?.Id;
            var requestedJob = _jobId;
            var snapshot = await _controller.ExecuteAsync(s => s.GetSnapshotAsync(requestedJob));
            if (_closed || _jobId != requestedJob) return;
            _snapshot = snapshot;
            var job = _snapshot.Job;
            JobName.Text = job.Name; SourcePath.Text = job.SourceFolder; OutputPath.Text = job.OutputFolder; IndividualsPath.Text = job.IndividualsFolder;
            _settingChoices = true;
            var teamsSignature = string.Join('|', _snapshot.Teams.Select(t => $"{t.Id}:{t.SortOrder}:{t.PerformanceLabel}"));
            if (Teams.Tag as string != teamsSignature)
            { Teams.ItemsSource = _snapshot.Teams.Select(FormatPerformance).ToArray(); Teams.Tag = teamsSignature; }
            Teams.SelectedIndex = _snapshot.Teams.ToList().FindIndex(x => x.Id == job.CurrentTeamId);
            _settingChoices = false;
            var team = _snapshot.Teams.FirstOrDefault(x => x.Id == job.CurrentTeamId);
            PlayerTeamName.Text = team?.Name ?? "Select a team";
            CurrentPlayer.Text = team is null ? "" : $"Player {team.CurrentPlayer}";
            RenderPlayerSlots(team);
            PhotoCount.Text = $"{_snapshot.Images.Count} recent image(s)";
            var signature = string.Join('|', _snapshot.Images.Select(x => $"{x.Id}:{x.IsPrimary}:{x.IsRejected}:{x.Released}:{x.NeedsDelivery}"));
            if (_displaySignature != signature)
            {
                var previousNewest = _displaySignature?.Split('|')[0].Split(':')[0];
                _displaySignature = signature;
                _settingChoices = true;
                ImagesList.ItemsSource = _snapshot.Images.Select(x => $"{(x.PlayerNumber > 0 ? "Player " + x.PlayerNumber + " · " : "")}{x.Sequence:0000} · {Path.GetFileName(x.SourcePath)}{(x.IsPrimary ? " · PRIMARY" : "")}{(x.IsRejected ? " · REJECTED" : "")}{(x.NeedsDelivery ? " · pending" : "")}").ToArray();
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
        finally { _refreshing = false; }
    }
    private FastCaptureImage? SelectedImage() => ImagesList.SelectedIndex >= 0 && _snapshot is not null && ImagesList.SelectedIndex < _snapshot.Images.Count ? _snapshot.Images[ImagesList.SelectedIndex] : null;
    private void UpdateButtons()
    {
        MonitorButton.Content = _controller.IsRunning ? "Pause watching" : "Start watching";
        MonitorButton.IsEnabled = !_busy && _jobId is not null && _snapshot?.Job.CurrentTeamId is not null;
        NextPlayerButton.IsEnabled = !_busy && _snapshot?.Job.CurrentTeamId is not null;
        PrimaryButton.IsEnabled = !_busy && SelectedImage() is { Released: false };
        RejectButton.IsEnabled = !_busy && SelectedImage() is not null;
        RejectButton.Content = SelectedImage()?.IsRejected == true ? "Restore image" : "Reject image";
        SaveJobButton.IsEnabled = !_busy && _jobId is null;
        ImportRosterButton.IsEnabled = !_busy && _jobId is not null;
        NextPerformanceButton.IsEnabled = !_busy && _snapshot is { Teams.Count: > 0 };
        PlayerSlots.IsEnabled = !_busy;
    }
    private async Task ShowPreviewAsync()
    {
        if (_type == CaptureJobType.ActionPhotography) return;
        var image = SelectedImage();
        if (image?.Id == _displayedImageId) return;
        _displayedImageId = image?.Id;
        Preview.Source = null; _bitmap?.Dispose(); _bitmap = null;
        if (image is null) return;
        var bytes = await File.ReadAllBytesAsync(image.StoredPath);
        if (_closed || SelectedImage()?.Id != image.Id) return;
        using var source = new MemoryStream(bytes);
        _bitmap = Bitmap.DecodeToHeight(source, 700);
        Preview.Source = _bitmap;
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
        await RunAsync(async () => { await _controller.StopAsync(); _jobId = _jobs[SavedJobs.SelectedIndex].Id; _displaySignature = null; });
    }
    private async void OnNewJobClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        await _controller.StopAsync(); _jobId = null; _snapshot = null; _displaySignature = null; JobName.Text = ""; SetupPanel.IsExpanded = true;
        _settingChoices = true; Teams.ItemsSource = null; ImagesList.ItemsSource = null; _settingChoices = false;
        Preview.Source = null; _bitmap?.Dispose(); _bitmap = null; _displayedImageId = null;
        CurrentPlayer.Text = PhotoCount.Text = ""; PlayerSlots.Children.Clear(); _slotSignature = null; RosterMappingPanel.IsVisible = false;
    });
    private async void OnSwitchTeamClick(object? sender, RoutedEventArgs e) => await SwitchTeamAsync(TeamName.Text ?? "");
    private async void OnTeamChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || _snapshot is null || Teams.SelectedIndex < 0) return;
        var jobId = _snapshot.Job.Id; var teamId = _snapshot.Teams[Teams.SelectedIndex].Id;
        await RunAsync(() => _controller.ExecuteAsync(s => s.SelectPerformanceAsync(jobId, teamId)));
    }
    private async Task SwitchTeamAsync(string name) => await RunAsync(async () =>
    { if (_jobId is not null) await _controller.ExecuteAsync(s => s.SelectTeamAsync(_jobId, name)); });
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
    private static string FormatPerformance(FastCaptureTeam team) => $"{team.SortOrder}. {team.Name}" + (team.PerformanceLabel.Length > 0 ? $" · {team.PerformanceLabel}" : "");
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
                IsEnabled = !used || current, FontWeight = current ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal };
            if (current) { button.Background = Avalonia.Media.Brushes.DarkBlue; button.Foreground = Avalonia.Media.Brushes.White; }
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
        var index = snapshot.Teams.ToList().FindIndex(t => t.Id == snapshot.Job.CurrentTeamId);
        if (index + 1 >= snapshot.Teams.Count) { _actionError = "This is the last performance in the imported order."; Status.Text = _actionError; return; }
        var next = snapshot.Teams[index + 1].Id;
        await RunAsync(() => _controller.ExecuteAsync(s => s.SelectPerformanceAsync(snapshot.Job.Id, next)));
    }
    private async void OnImportRosterClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_jobId is null) throw new InvalidOperationException("Create or resume a job before importing its roster.");
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        { Title = "Choose roster / performance order CSV", AllowMultiple = false, FileTypeFilter = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }] });
        if (files.Count == 0) return;
        _rosterName = files[0].Name;
        await using var input = await files[0].OpenReadAsync(); using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer); _rosterBytes = buffer.ToArray();
        using var source = new MemoryStream(_rosterBytes); _rosterDocument = RosterCsvReader.Read(source);
        SetColumnOptions(RosterTeamColumn, _rosterDocument.Headers, ["TEAMNAME", "TEAMSCHOOL", "TEAM", "PERFORMER", "SCHOOLNAME", "NAME"], true);
        SetColumnOptions(RosterGroupColumn, _rosterDocument.Headers, ["SPORT", "DIVISION", "GROUP", "CATEGORY"], false);
        if ((RosterTeamColumn.SelectedItem as ComboBoxItem)?.Tag is int selectedTeam &&
            (RosterGroupColumn.SelectedItem as ComboBoxItem)?.Tag is int selectedGroup && selectedTeam == selectedGroup)
            RosterGroupColumn.SelectedIndex = 0;
        SetColumnOptions(RosterOrderColumn, _rosterDocument.Headers, ["PERFORMANCEORDER", "RUNORDER", "ORDER", "PERFORMANCENUMBER"], false);
        RosterFileSummary.Text = $"{_rosterName} · {_rosterDocument.Rows.Count} rows. Confirm the columns below.";
        RosterMappingPanel.IsVisible = true;
    });
    private async void OnConfirmRosterClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        if (_jobId is not { } jobId || _rosterBytes is null) return;
        var teamColumn = (RosterTeamColumn.SelectedItem as ComboBoxItem)?.Tag as int? ?? throw new ArgumentException("Choose a team / performer column.");
        var command = new FastRosterImportCommand(jobId, _rosterName, _rosterBytes, teamColumn,
            (RosterGroupColumn.SelectedItem as ComboBoxItem)?.Tag as int?, (RosterOrderColumn.SelectedItem as ComboBoxItem)?.Tag as int?);
        await ImportRosterCoreAsync(command);
    });
    public Task ImportConfiguredRosterAsync(FastRosterImportCommand command) => RunAsync(() => ImportRosterCoreAsync(command));
    private async Task ImportRosterCoreAsync(FastRosterImportCommand command)
    {
        var result = await _controller.ExecuteAsync(s => s.ImportRosterAsync(command));
        RosterMappingPanel.IsVisible = false;
        _actionError = $"Imported {result.Teams} teams / performances from {result.Rows} source rows.";
    }
    private static void SetColumnOptions(ComboBox box, IReadOnlyList<string> headers, string[] aliases, bool required)
    {
        box.Items.Clear(); if (!required) box.Items.Add(new ComboBoxItem { Content = "(not mapped)" });
        foreach (var item in headers.Select((text, index) => (text, index)).OrderBy(x => x.text, StringComparer.OrdinalIgnoreCase))
            box.Items.Add(new ComboBoxItem { Content = $"{item.text} [{item.index}]", Tag = item.index });
        var suggested = headers.Select((h, i) => (h, i)).FirstOrDefault(x => aliases.Contains(string.Concat(x.h.Where(char.IsLetterOrDigit)).ToUpperInvariant()));
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag is int i && suggested.h is not null && i == suggested.i)
            ?? (required ? null : box.Items[0]);
    }

}
