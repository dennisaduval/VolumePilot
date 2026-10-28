using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
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
    private string? _jobId, _displayedImageId, _displaySignature;
    private Bitmap? _bitmap;
    public event Action? ReturnRequested;
    private bool UsesPlayers => _type == CaptureJobType.PrintOnSiteTeamAndIndividual;
    public FastCaptureWindow(CaptureJobType type, FastCaptureController controller)
    {
        _type = type; _controller = controller; InitializeComponent();
        ModeTitle.Text = type switch { CaptureJobType.PrintOnSiteTeamsOnly => "Print on site · Team Only", CaptureJobType.ActionPhotography => "Action · automatic image routing", _ => "Print on site · Team and Individuals" };
        NextPlayerButton.IsVisible = CurrentPlayer.IsVisible = PlayerSlots.IsVisible = PrimaryButton.IsVisible = IndividualsSetup.IsVisible = UsesPlayers;
        PreviewPanel.IsVisible = type != CaptureJobType.ActionPhotography;
        if (type == CaptureJobType.ActionPhotography) Grid.SetColumnSpan(CaptureControls, 2);
        _timer.Tick += async (_, _) => { if (!_busy && !_refreshing && _jobId is not null) await RefreshAsync(); };
        Loaded += async (_, _) => { await LoadJobsAsync(); _timer.Start(); };
        Closed += (_, _) => { _closed = true; _timer.Stop(); _controller.Dispose(); _bitmap?.Dispose(); };
    }
    private async Task RunAsync(Func<Task> action)
    {
        if (_busy) return; _busy = true; UpdateButtons();
        try { await action(); await RefreshAsync(); }
        catch (Exception exception) { Status.Text = exception.Message; }
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
            _snapshot = await _controller.ExecuteAsync(s => s.GetSnapshotAsync(_jobId));
            var job = _snapshot.Job;
            JobName.Text = job.Name; SourcePath.Text = job.SourceFolder; OutputPath.Text = job.OutputFolder; IndividualsPath.Text = job.IndividualsFolder;
            _settingChoices = true;
            var teamsSignature = string.Join('|', _snapshot.Teams.Select(t => t.Id));
            if (Teams.Tag as string != teamsSignature)
            { Teams.ItemsSource = _snapshot.Teams.Select(t => t.Name).ToArray(); Teams.Tag = teamsSignature; }
            Teams.SelectedIndex = _snapshot.Teams.ToList().FindIndex(x => x.Id == job.CurrentTeamId);
            _settingChoices = false;
            var team = _snapshot.Teams.FirstOrDefault(x => x.Id == job.CurrentTeamId);
            CurrentPlayer.Text = team is null ? "Select a team" : $"{team.Name} · Player {team.CurrentPlayer}";
            PlayerSlots.Text = "Used slots: " + string.Join("   ", _snapshot.UsedPlayers.Select(x => $"✓ Player {x}"));
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
            if (_controller.Error is { } error) Status.Text = error;
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
    private async void OnCreateJobClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _jobId = await _controller.ExecuteAsync(s => s.CreateJobAsync(new(_type, JobName.Text ?? "", SourcePath.Text ?? "", OutputPath.Text ?? "", IndividualsPath.Text)));
        SetupPanel.IsExpanded = false;
    });
    private async void OnSavedJobChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_settingChoices || SavedJobs.SelectedIndex < 0) return;
        await RunAsync(async () => { await _controller.StopAsync(); _jobId = _jobs[SavedJobs.SelectedIndex].Id; _displaySignature = null; });
    }
    private async void OnNewJobClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    { await _controller.StopAsync(); _jobId = null; _snapshot = null; _displaySignature = null; JobName.Text = ""; SetupPanel.IsExpanded = true; });
    private async void OnSwitchTeamClick(object? sender, RoutedEventArgs e) => await SwitchTeamAsync(TeamName.Text ?? "");
    private async void OnTeamChanged(object? sender, SelectionChangedEventArgs e)
    { if (!_settingChoices && _snapshot is not null && Teams.SelectedIndex >= 0) await SwitchTeamAsync(_snapshot.Teams[Teams.SelectedIndex].Name); }
    private async Task SwitchTeamAsync(string name) => await RunAsync(async () =>
    { if (_jobId is not null) await _controller.ExecuteAsync(s => s.SelectTeamAsync(_jobId, name)); });
    private async void OnMonitorClick(object? sender, RoutedEventArgs e) => await RunAsync(async () =>
    { if (_controller.IsRunning) await _controller.StopAsync(); else if (_snapshot is not null) await _controller.StartAsync(_snapshot.Job.Id, _snapshot.Job.SourceFolder); });
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
}
