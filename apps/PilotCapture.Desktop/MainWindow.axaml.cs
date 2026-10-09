using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PilotCapture.Application;
using PilotCapture.Application.Capture;
using PilotCapture.Application.Rosters;
using PilotCapture.Domain;
using System.Security.Cryptography;

namespace PilotCapture.Desktop;

public sealed partial class MainWindow : Window
{
    private RosterCsvDocument? _rosterDocument;
    private string? _rosterFileName;
    private string? _rosterSha256;
    private bool _isPopulatingMappings;
    private bool _hasImportedCurrentFile;
    private readonly IRosterImportService _rosterImportService;
    private readonly ICaptureWorkflowService _captureWorkflowService;
    private readonly IImageIngestService _imageIngestService;
    private readonly IImageReviewService _imageReviewService;
    private readonly IImageAssetStore _imageAssetStore;
    private readonly IImageAssociationExportService _imageAssociationExportService;
    private readonly IRosterExportService _rosterExportService;
    private readonly WindowsPortraitFaceDetector _faceDetector;
    private readonly IJobMediaService _jobMediaService;
    private IReadOnlyList<CaptureGroupChoice> _allGroups = [];
    private int _searchVersion;
    private int _previewVersion;
    private readonly SemaphoreSlim _captureOperationLock = new(1, 1);
    private IReadOnlyList<CaptureImageReviewRow> _captureImageRows = [];
    private Bitmap? _selectedReviewBitmap;
    private CroppedBitmap? _selectedFaceCrop;
    private bool _isPopulatingReviewImages;
    private ActiveCaptureSession? _activeCaptureSession;
    private bool _isPopulatingCaptureChoices;
    private DispatcherTimer? _imageScanTimer;
    private bool _isScanningImageFolder;
    private bool _isMonitoringImageFolder;
    private bool _isAdvancingSubject;
    private readonly HashSet<string> _observedSourceVersions = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow(
        IRosterImportService rosterImportService,
        ICaptureWorkflowService captureWorkflowService,
        IImageIngestService imageIngestService,
        IImageReviewService imageReviewService,
        IImageAssetStore imageAssetStore,
        IImageAssociationExportService imageAssociationExportService,
        IRosterExportService rosterExportService,
        WindowsPortraitFaceDetector faceDetector,
        IJobMediaService jobMediaService)
    {
        _rosterImportService = rosterImportService;
        _captureWorkflowService = captureWorkflowService;
        _imageIngestService = imageIngestService;
        _imageReviewService = imageReviewService;
        _imageAssetStore = imageAssetStore;
        _imageAssociationExportService = imageAssociationExportService;
        _rosterExportService = rosterExportService;
        _faceDetector = faceDetector;
        _jobMediaService = jobMediaService;
        // The generated initializer loads XAML and assigns every named control field.
        // Loading XAML directly leaves those fields null before the event hookups.
        InitializeComponent();
        EventName.TextChanged += (_, _) => UpdateImportAvailability();
        PhotographerName.TextChanged += (_, _) => UpdateCaptureControls();
        UnidentifiedSubjectName.TextChanged += (_, _) => UpdateCaptureControls();
        ManualFirstName.TextChanged += (_, _) => UpdateCaptureControls();
        ManualLastName.TextChanged += (_, _) => UpdateCaptureControls();
        ManualRosterNumber.TextChanged += (_, _) => UpdateCaptureControls();
        ManualRole.TextChanged += (_, _) => UpdateCaptureControls();
        WorkflowTypeCombo.Items.Add(new ComboBoxItem { Content = "Portrait", Tag = CaptureWorkflowType.Portrait });
        WorkflowTypeCombo.Items.Add(new ComboBoxItem { Content = "Action", Tag = CaptureWorkflowType.Action });
        WorkflowTypeCombo.SelectedIndex = 0;
        ExportKindCombo.ItemsSource = new[] { "Original JPEGs · team folders", "Edited PNGs · team folders", "Batch editing · original photos" };
        ExportKindCombo.SelectedIndex = 0;
        foreach (var stationCode in new[] { "s10", "s20", "s30", "s40" })
            StationCodeCombo.Items.Add(new ComboBoxItem { Content = stationCode, Tag = stationCode });
        Loaded += async (_, _) => await LoadCaptureSetupAsync();
        Closed += (_, _) =>
        {
            StopImageMonitoring();
            ClearSelectedReviewPreview();
            foreach (var row in _captureImageRows)
                row.Thumbnail.Dispose();
        };
    }

    private async void OnOpenRosterClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a roster CSV",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }
            ]
        });

        if (files.Count == 0)
            return;

        try
        {
            await using var stream = await files[0].OpenReadAsync();
            using var bytes = new MemoryStream();
            await stream.CopyToAsync(bytes);
            var content = bytes.ToArray();
            _rosterSha256 = Convert.ToHexString(SHA256.HashData(content));
            _rosterDocument = RosterCsvReader.Read(new MemoryStream(content));
            _rosterFileName = files[0].Name;
            _hasImportedCurrentFile = false;

            var suggestions = RosterColumnSuggestionBuilder.Build(_rosterDocument.Headers);
            _isPopulatingMappings = true;
            SetOptions(FirstNameColumn, _rosterDocument.Headers, suggestions.FirstNameColumn);
            SetOptions(LastNameColumn, _rosterDocument.Headers, suggestions.LastNameColumn);
            SetOptions(TeamOrSchoolColumn, _rosterDocument.Headers, suggestions.TeamOrSchoolColumn);
            SetOptions(SportOrGroupColumn, _rosterDocument.Headers, suggestions.SportOrGroupColumn);
            SetOptions(RosterNumberColumn, _rosterDocument.Headers, suggestions.RosterNumberColumn);
            SetOptions(ClassOrCategoryColumn, _rosterDocument.Headers, suggestions.ClassOrCategoryColumn);
            _isPopulatingMappings = false;

            UpdatePreview(suggestions.Warnings);
        }
        catch (RosterCsvFormatException exception)
        {
            _rosterDocument = null;
            _rosterFileName = null;
            _rosterSha256 = null;
            RosterPreviewRows.ItemsSource = null;
            RosterSummary.Text = "The selected file could not be read as a roster CSV.";
            RosterWarnings.Text = exception.Message;
            UpdateImportAvailability();
        }
        catch (IOException exception)
        {
            _rosterDocument = null;
            _rosterFileName = null;
            _rosterSha256 = null;
            RosterPreviewRows.ItemsSource = null;
            RosterSummary.Text = "The selected file could not be opened.";
            RosterWarnings.Text = exception.Message;
            UpdateImportAvailability();
        }
    }

    private void OnColumnMappingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPopulatingMappings && _rosterDocument is not null)
            UpdatePreview([]);
    }

    private void UpdatePreview(IReadOnlyList<string> initialWarnings)
    {
        if (_rosterDocument is null)
            return;

        var mapping = new RosterColumnMapping(
            SelectedColumn(FirstNameColumn),
            SelectedColumn(LastNameColumn),
            SelectedColumn(TeamOrSchoolColumn),
            SelectedColumn(SportOrGroupColumn),
            SelectedColumn(RosterNumberColumn),
            SelectedColumn(ClassOrCategoryColumn));
        var rows = RosterImportPreviewBuilder.Build(_rosterDocument, mapping);
        PopulateMatchReview(rows);
        var warnings = initialWarnings.ToList();
        var mismatchedRows = rows.Count(row => !row.HasExpectedFieldCount);
        var possibleMatchRows = rows.Count(row => row.PotentialCrossGroupNameMatchCount > 0);

        if (mismatchedRows > 0)
            warnings.Add($"{mismatchedRows} row(s) have a different number of cells than the header. Their original cells are preserved.");
        if (possibleMatchRows > 0)
            warnings.Add($"{possibleMatchRows} row(s) share a name across groups. These are review suggestions only; no people are merged.");
        if (mapping.FirstNameColumn is null || mapping.LastNameColumn is null)
            warnings.Add("Map both name columns to preview subject names.");
        if (mapping.TeamOrSchoolColumn is null && mapping.SportOrGroupColumn is null)
            warnings.Add("Map a team, school, sport, or group column to preview memberships.");

        RosterSummary.Text = $"{_rosterFileName}: {_rosterDocument.Rows.Count} data rows, {_rosterDocument.Headers.Count} columns. Previewing the first {Math.Min(20, rows.Count)} rows.";
        RosterWarnings.Text = string.Join(Environment.NewLine, warnings.Distinct(StringComparer.Ordinal));
        RosterPreviewRows.ItemsSource = rows.Take(20).Select(row =>
        {
            var name = string.IsNullOrWhiteSpace(row.DisplayName) ? "(name not mapped)" : row.DisplayName;
            var group = row.GroupName ?? "(group not mapped)";
            var number = string.IsNullOrEmpty(row.RosterNumber) ? "(no number)" : row.RosterNumber;
            var category = string.IsNullOrEmpty(row.ClassOrCategory) ? "(no category)" : row.ClassOrCategory;
            var match = row.PotentialCrossGroupNameMatchCount > 0 ? " · possible same-name match" : string.Empty;
            var shape = row.HasExpectedFieldCount ? string.Empty : " · row width differs";
            return $"{row.SourceRecordNumber,3}. {name}  |  {group}  |  #{number}  |  {category}{match}{shape}";
        }).ToArray();
        UpdateImportAvailability();
    }

    private async void OnImportRosterClick(object? sender, RoutedEventArgs e)
    {
        if (_rosterDocument is null || _rosterFileName is null || _rosterSha256 is null)
            return;

        try
        {
            var mapping = new RosterColumnMapping(
                SelectedColumn(FirstNameColumn),
                SelectedColumn(LastNameColumn),
                SelectedColumn(TeamOrSchoolColumn),
                SelectedColumn(SportOrGroupColumn),
                SelectedColumn(RosterNumberColumn),
                SelectedColumn(ClassOrCategoryColumn));
            var rows = RosterImportPreviewBuilder.Build(_rosterDocument, mapping);
            var confirmed = MatchReviewPanel.Children
                .OfType<CheckBox>()
                .Where(checkBox => checkBox.IsChecked == true)
                .Select(checkBox => (string)checkBox.Tag!)
                .ToHashSet(StringComparer.Ordinal);
            var result = await _rosterImportService.ImportAsync(new RosterImportCommand(
                EventName.Text ?? string.Empty,
                _rosterFileName,
                _rosterSha256,
                rows,
                confirmed));

            RosterSummary.Text = $"Imported {result.RowsImported} of {result.RowsRead} roster rows into '{EventName.Text?.Trim()}'.";
            RosterWarnings.Text = result.RowsSkipped == 0
                ? "The event and roster are saved on this computer."
                : $"The event and roster are saved on this computer. {result.RowsSkipped} row(s) were preserved in the import record but skipped because a name or group was blank.";
            _hasImportedCurrentFile = true;
            ImportRosterButton.IsEnabled = false;
            await LoadCaptureSetupAsync();
        }
        catch (Exception exception)
        {
            RosterWarnings.Text = $"Roster import failed: {exception.Message}";
        }
    }

    private void PopulateMatchReview(IReadOnlyList<RosterPreviewRow> rows)
    {
        var previouslyConfirmed = MatchReviewPanel.Children
            .OfType<CheckBox>()
            .Where(checkBox => checkBox.IsChecked == true)
            .Select(checkBox => (string)checkBox.Tag!)
            .ToHashSet(StringComparer.Ordinal);
        MatchReviewPanel.Children.Clear();
        foreach (var cluster in rows.Where(row => row.PotentialMatchKey is not null)
                     .GroupBy(row => row.PotentialMatchKey!, StringComparer.Ordinal))
        {
            var entries = cluster.ToArray();
            var groups = string.Join(", ", entries.Select(row => row.GroupName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase));
            var checkbox = new CheckBox
            {
                Content = $"Link {entries[0].DisplayName} across {groups} ({entries.Length} rows)",
                Tag = cluster.Key,
                IsChecked = previouslyConfirmed.Contains(cluster.Key)
            };
            MatchReviewPanel.Children.Add(checkbox);
        }
    }

    private void UpdateImportAvailability()
    {
        var namesMapped = SelectedColumn(FirstNameColumn) is not null && SelectedColumn(LastNameColumn) is not null;
        var groupMapped = SelectedColumn(TeamOrSchoolColumn) is not null || SelectedColumn(SportOrGroupColumn) is not null;
        ImportRosterButton.IsEnabled = _rosterDocument is { Rows.Count: > 0 }
            && !_hasImportedCurrentFile
            && !string.IsNullOrWhiteSpace(EventName?.Text)
            && namesMapped && groupMapped;
    }

    private static void SetOptions(ComboBox comboBox, IReadOnlyList<string> headers, int? selectedColumn)
    {
        comboBox.Items.Clear();
        comboBox.Items.Add("(not mapped)");
        foreach (var item in headers.Select((header, index) => (header, index)).OrderBy(x => x.header, StringComparer.CurrentCultureIgnoreCase))
            comboBox.Items.Add(new ComboBoxItem { Content = $"{item.header} [{item.index}]", Tag = item.index });
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag is int column && column == selectedColumn)
            ?? comboBox.Items[0];
        comboBox.IsEnabled = true;
    }

    private static int? SelectedColumn(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as int?;

    private async Task LoadCaptureSetupAsync()
    {
        try
        {
            var events = await _captureWorkflowService.GetEventsAsync();
            _activeCaptureSession = await _captureWorkflowService.GetActiveSessionAsync();
            var stationCode = _activeCaptureSession?.StationCode ?? await _captureWorkflowService.GetStationCodeAsync();
            MasterFolderPath.Text = await _jobMediaService.GetMasterPathAsync() ?? string.Empty;
            SmartShooterFolderPath.Text = await _captureWorkflowService.GetSmartShooterOutputPathAsync() ?? string.Empty;
            _isPopulatingCaptureChoices = true;
            SetChoices(CaptureEventCombo, events, item => item.Name, _activeCaptureSession?.EventId);
            StationCodeCombo.SelectedItem = StationCodeCombo.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag as string, stationCode, StringComparison.Ordinal));
            _isPopulatingCaptureChoices = false;

            if (_activeCaptureSession is not null)
            {
                PhotographerName.Text = _activeCaptureSession.PhotographerName;
                var workflowName = _activeCaptureSession.ProfileName;
                WorkflowTypeCombo.SelectedIndex = workflowName.Equals("Action", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                if (_activeCaptureSession.CurrentCaptureSet is { } currentSet)
                    SelectedCaptureSubject.Text = $"Resumed with {currentSet.SubjectName} selected. Ready for this subject's images.";
            }

            UpdateCaptureControls();
            await RefreshCaptureGroupsAsync(_activeCaptureSession?.CurrentCaptureSet?.GroupId);
            await RefreshReviewImagesAsync();
        }
        catch (Exception exception)
        {
            SessionStatus.Text = $"Capture setup could not load: {exception.Message}";
        }
    }

    private async void OnCaptureEventChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPopulatingCaptureChoices)
            await RefreshCaptureGroupsAsync();
    }

    private async void OnExportRosterClick(object? sender, RoutedEventArgs e)
    {
        var captureEvent = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        if (captureEvent is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export a new VolumePilot roster",
            SuggestedFileName = $"{MakeSafeFileName(captureEvent.Name)}-roster.csv",
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
        });
        if (file is null)
            return;

        try
        {
            await using var output = await file.OpenWriteAsync();
            var exportedCount = await _rosterExportService.ExportEventAsync(captureEvent.Id, output);
            SessionStatus.Text = $"Exported {exportedCount} subject/group row(s) to {file.Name}. The imported roster file remains unchanged.";
        }
        catch (Exception exception)
        {
            SessionStatus.Text = $"Roster export failed: {exception.Message}";
        }
    }

    private async void OnExportImageAssociationsClick(object? sender, RoutedEventArgs e)
    {
        var captureEvent = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        if (captureEvent is null)
            return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export image-to-subject associations",
            SuggestedFileName = $"{MakeSafeFileName(captureEvent.Name)}-image-associations.csv",
            DefaultExtension = "csv",
            ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("CSV files") { Patterns = ["*.csv"] }]
        });
        if (file is null)
            return;

        try
        {
            await using var output = await file.OpenWriteAsync();
            var exportedCount = await _imageAssociationExportService.ExportEventAsync(captureEvent.Id, output);
            ImageIngestStatus.Text = $"Exported {exportedCount} image association(s) to {file.Name}. Missing image files are marked in the CSV.";
        }
        catch (Exception exception)
        {
            ImageIngestStatus.Text = $"Image association export failed: {exception.Message}";
        }
    }

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "event" : safe;
    }

    private async void OnCaptureGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPopulatingCaptureChoices)
            await RefreshCaptureSubjectsAsync();
    }

    private async void OnStationCodeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingCaptureChoices || StationCodeCombo.SelectedItem is not ComboBoxItem { Tag: string stationCode })
            return;
        try
        {
            await _captureWorkflowService.SetStationCodeAsync(stationCode);
        }
        catch (Exception exception)
        {
            SessionStatus.Text = $"Station code could not be saved: {exception.Message}";
        }
    }

    private async void OnChooseSmartShooterFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose Smart Shooter 5's JPEG output folder",
            AllowMultiple = false
        });
        if (folders.Count == 0)
            return;

        try
        {
            var path = folders[0].Path.LocalPath;
            await _captureWorkflowService.SetSmartShooterOutputPathAsync(path);
            SmartShooterFolderPath.Text = path;
            _observedSourceVersions.Clear();
            ImageIngestStatus.Text = "Output folder saved. Start monitoring after selecting a capture subject. Source files will be left in this folder.";
            UpdateCaptureControls();
        }
        catch (Exception exception)
        {
            ImageIngestStatus.Text = $"Output folder could not be saved: {exception.Message}";
        }
    }

    private void OnMonitorFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_isMonitoringImageFolder)
        {
            StopImageMonitoring();
            ImageIngestStatus.Text = "Smart Shooter folder monitoring is paused. Imported source files remain in place.";
            UpdateCaptureControls();
            return;
        }

        var path = SmartShooterFolderPath.Text;
        if (_activeCaptureSession?.CurrentCaptureSet is null || string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ImageIngestStatus.Text = "Choose a Smart Shooter folder and select a subject before starting monitoring.";
            return;
        }

        _isMonitoringImageFolder = true;
        _imageScanTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _imageScanTimer.Tick += OnImageScanTick;
        _imageScanTimer.Start();
        ImageIngestStatus.Text = $"Monitoring {path} for JPEGs newer than the current subject selection. Original files are not changed or deleted.";
        UpdateCaptureControls();
        _ = ScanSmartShooterFolderAsync();
    }

    private async void OnImageScanTick(object? sender, EventArgs e) => await ScanSmartShooterFolderAsync();

    private async Task ScanSmartShooterFolderAsync()
    {
        if (_isScanningImageFolder || !_isMonitoringImageFolder)
            return;
        var session = _activeCaptureSession;
        var captureSet = session?.CurrentCaptureSet;
        var folder = SmartShooterFolderPath.Text;
        if (session is null || captureSet is null || string.IsNullOrWhiteSpace(folder))
            return;

        _isScanningImageFolder = true;
        await _captureOperationLock.WaitAsync();
        try
        {
            var hasNewImages = false;
            string? latestNewCaptureImageId = null;
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
                .Where(path => Path.GetExtension(path).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || Path.GetExtension(path).Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(File.GetLastWriteTimeUtc)
                .ToArray();
            foreach (var path in files)
            {
                if (_activeCaptureSession?.CurrentCaptureSet?.CaptureSetId != captureSet.CaptureSetId)
                    break;

                var fullPath = Path.GetFullPath(path);
                var sourceVersion = GetSourceVersionKey(fullPath);
                if (_observedSourceVersions.Contains(sourceVersion))
                    continue;
                if (File.GetLastWriteTimeUtc(fullPath) < captureSet.StartedAtUtc.UtcDateTime)
                {
                    _observedSourceVersions.Add(sourceVersion);
                    continue;
                }

                try
                {
                    var result = await _imageIngestService.ImportJpegAsync(session.Id, captureSet.CaptureSetId, fullPath);
                    if (_activeCaptureSession?.CurrentCaptureSet?.CaptureSetId != captureSet.CaptureSetId)
                        break;
                    _observedSourceVersions.Add(sourceVersion);
                    if (!result.AlreadyImported)
                    {
                        hasNewImages = true;
                        latestNewCaptureImageId = result.CaptureImageId;
                        ImageIngestStatus.Text = $"Imported {result.OriginalFileName} as image {result.SequenceNumber + 1} for {captureSet.SubjectName}. Source kept intact.";
                    }
                }
                catch (IOException)
                {
                    // Smart Shooter may still be writing this file. Retry it on the next scan.
                }
                catch (InvalidDataException exception)
                {
                    _observedSourceVersions.Add(sourceVersion);
                    ImageIngestStatus.Text = $"Skipped {Path.GetFileName(fullPath)}: {exception.Message}";
                }
                catch (InvalidOperationException exception)
                {
                    _observedSourceVersions.Add(sourceVersion);
                    ImageIngestStatus.Text = $"Skipped {Path.GetFileName(fullPath)}: {exception.Message}";
                }
            }
            if (hasNewImages && _activeCaptureSession?.CurrentCaptureSet?.CaptureSetId == captureSet.CaptureSetId)
            {
                await RefreshReviewImagesAsync(latestNewCaptureImageId);
                await RefreshCaptureSubjectsAsync();
                if (await _jobMediaService.GetMasterPathAsync() is not null)
                {
                    try
                    {
                        var published = await _jobMediaService.PublishOriginalsAsync(session.EventId);
                        MediaStatus.Text = $"{published.Images} originals available in {published.Location}";
                    }
                    catch (Exception exception) { MediaStatus.Text = $"Captured locally. Master copy pending; use Publish originals / retry: {exception.Message}"; }
                }
            }
        }
        catch (Exception exception)
        {
            ImageIngestStatus.Text = $"Smart Shooter folder scan failed: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
            _isScanningImageFolder = false;
        }
    }

    private void StopImageMonitoring()
    {
        _imageScanTimer?.Stop();
        _imageScanTimer = null;
        _isMonitoringImageFolder = false;
    }

    private static string GetSourceVersionKey(string path)
    {
        var file = new FileInfo(path);
        return $"{Path.GetFullPath(path)}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
    }

    private async void OnReviewImageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isPopulatingReviewImages || CaptureImagesList.SelectedItem is not CaptureImageReviewRow selected)
            return;
        await ShowSelectedReviewImageAsync(selected);
    }

    private async Task RefreshReviewImagesAsync(string? selectedImageId = null)
    {
        var captureSet = _activeCaptureSession?.CurrentCaptureSet;
        ReviewSubjectTitle.Text = captureSet is null ? "Image review" : $"Image review · {captureSet.SubjectName}";
        ClearSelectedReviewPreview();
        foreach (var oldRow in _captureImageRows)
            oldRow.Thumbnail.Dispose();

        if (captureSet is null)
        {
            _captureImageRows = [];
            _isPopulatingReviewImages = true;
            CaptureImagesList.ItemsSource = _captureImageRows;
            CaptureImagesList.SelectedItem = null;
            _isPopulatingReviewImages = false;
            ReviewStatus.Text = "Select a subject in Capture to begin. Imported originals remain unchanged.";
            UpdateReviewControls();
            return;
        }

        try
        {
            var items = await _imageReviewService.GetImagesAsync(captureSet.CaptureSetId);
            var rows = new List<CaptureImageReviewRow>(items.Count);
            foreach (var item in items)
            {
                await using var assetStream = await _imageAssetStore.OpenReadAsync(item.RelativePath, CancellationToken.None);
                using var buffer = new MemoryStream();
                await assetStream.CopyToAsync(buffer);
                var portrait = await PortraitImageDecoder.DecodeAsync(buffer.ToArray(), 240);
                using var imageStream = new MemoryStream(portrait);
                var thumbnail = Bitmap.DecodeToHeight(imageStream, 136, BitmapInterpolationMode.HighQuality);
                rows.Add(new CaptureImageReviewRow(item, thumbnail));
            }

            _captureImageRows = rows;
            _isPopulatingReviewImages = true;
            CaptureImagesList.ItemsSource = _captureImageRows;
            var selected = rows.FirstOrDefault(row => row.Id == selectedImageId)
                ?? rows.LastOrDefault();
            CaptureImagesList.SelectedItem = selected;
            if (selected is not null) CaptureImagesList.ScrollIntoView(selected);
            _isPopulatingReviewImages = false;
            var observedSizes = ImageDimensionMonitor.GetDistinctCaptureSizes(
                rows.Select(row => (row.PixelWidth, row.PixelHeight)));
            var dimensionWarning = observedSizes.Count > 1
                ? $" Camera output warning: mixed JPEG dimensions detected ({string.Join(", ", observedSizes.Select(size => $"{size.Width:N0} × {size.Height:N0}"))}). Verify the Smart Shooter image-size setting."
                : string.Empty;
            ReviewStatus.Text = rows.Count == 0
                ? "No images yet. Start monitoring in Capture, then photograph this subject."
                : $"{rows.Count} image(s). Primary, Secondary and Banner are saved for this athlete and team; rejected images are retained.{dimensionWarning}";
            UpdateReviewControls();
            if (selected is not null)
                await ShowSelectedReviewImageAsync(selected);
        }
        catch (Exception exception)
        {
            _isPopulatingReviewImages = false;
            ReviewStatus.Text = $"Images could not be loaded: {exception.Message}";
            UpdateReviewControls();
        }
    }

    private async Task ShowSelectedReviewImageAsync(CaptureImageReviewRow selected)
    {
        ClearSelectedReviewPreview();
        var previewVersion = _previewVersion;
        try
        {
            await using var assetStream = await _imageAssetStore.OpenReadAsync(selected.RelativePath, CancellationToken.None);
            using var buffer = new MemoryStream();
            await assetStream.CopyToAsync(buffer);
            var jpegBytes = await PortraitImageDecoder.DecodeAsync(buffer.ToArray());
            if (previewVersion != _previewVersion) return;
            using (var imageStream = new MemoryStream(jpegBytes, writable: false))
                _selectedReviewBitmap = new Bitmap(imageStream);

            SelectedImageStatus.Text = $"{selected.OriginalFileName} · image {selected.SequenceNumber + 1}"
                + (selected.PixelWidth is { } width && selected.PixelHeight is { } height
                    ? $" · {width:N0} × {height:N0} px"
                    : " · dimensions unavailable")
                + $" · {selected.ByteLength / (1024d * 1024d):N1} MB"
                + (selected.IsPrimary ? " · Primary" : string.Empty)
                + (selected.IsSecondary ? " · Secondary" : string.Empty)
                + (selected.IsBanner ? " · Banner" : string.Empty)
                + (selected.ReviewState == CaptureImageReviewState.Rejected ? " · Rejected" : string.Empty);

            if (_activeCaptureSession?.WorkflowType == CaptureWorkflowType.Portrait)
            {
                Rect? face;
                var faceDetectionUnavailable = false;
                try
                {
                    face = await _faceDetector.DetectLargestFaceAsync(jpegBytes);
                }
                catch
                {
                    face = null;
                    faceDetectionUnavailable = true;
                }
                if (previewVersion != _previewVersion) return;
                if (face is { } faceBox)
                {
                    _selectedFaceCrop = new CroppedBitmap(
                        _selectedReviewBitmap,
                        CreateExpandedFaceCrop(_selectedReviewBitmap.PixelSize, faceBox));
                    SelectedImagePreview.Source = _selectedFaceCrop;
                    SelectedImageStatus.Text += " · on-device face crop";
                }
                else
                {
                    SelectedImagePreview.Source = _selectedReviewBitmap;
                    SelectedImageStatus.Text += faceDetectionUnavailable
                        ? " · face detection unavailable; showing full image"
                        : " · no face detected; showing full image";
                }
            }
            else
            {
                SelectedImagePreview.Source = _selectedReviewBitmap;
                SelectedImageStatus.Text += " · action workflow";
            }

            UpdateReviewControls();
        }
        catch (Exception exception)
        {
            SelectedImagePreview.Source = null;
            SelectedImageStatus.Text = $"Preview unavailable: {exception.Message}";
        }
    }

    private static PixelRect CreateExpandedFaceCrop(PixelSize imageSize, Rect normalizedFace)
    {
        var faceX = normalizedFace.X * imageSize.Width;
        var faceY = normalizedFace.Y * imageSize.Height;
        var faceWidth = normalizedFace.Width * imageSize.Width;
        var faceHeight = normalizedFace.Height * imageSize.Height;
        var left = Math.Clamp((int)Math.Floor(faceX - faceWidth * 0.55), 0, imageSize.Width - 1);
        var top = Math.Clamp((int)Math.Floor(faceY - faceHeight * 0.75), 0, imageSize.Height - 1);
        var right = Math.Clamp((int)Math.Ceiling(faceX + faceWidth * 1.55), left + 1, imageSize.Width);
        var bottom = Math.Clamp((int)Math.Ceiling(faceY + faceHeight * 1.55), top + 1, imageSize.Height);
        var height = Math.Min(imageSize.Height, Math.Max(bottom - top, (int)Math.Ceiling((right - left) * 4d / 3)));
        var width = Math.Min(imageSize.Width, (int)(height * 3d / 4));
        left = Math.Clamp((int)(faceX + faceWidth / 2 - width / 2d), 0, imageSize.Width - width);
        top = Math.Clamp((int)(faceY + faceHeight / 2 - height / 2d), 0, imageSize.Height - height);
        return new PixelRect(left, top, width, height);
    }

    private async void OnSetPrimaryClick(object? sender, RoutedEventArgs e) =>
        await ApplyReviewActionAsync(CaptureImageReviewAction.SetPrimary);

    private async void OnSetSecondaryClick(object? sender, RoutedEventArgs e) =>
        await ApplyReviewActionAsync(CaptureImageReviewAction.SetSecondary);

    private async void OnToggleBannerClick(object? sender, RoutedEventArgs e) =>
        await ApplyReviewActionAsync(CaptureImageReviewAction.ToggleBanner);

    private async void OnRejectImageClick(object? sender, RoutedEventArgs e) =>
        await ApplyReviewActionAsync(CaptureImageReviewAction.Reject);

    private async Task ApplyReviewActionAsync(CaptureImageReviewAction action)
    {
        if (CaptureImagesList.SelectedItem is not CaptureImageReviewRow selected)
            return;

        await _captureOperationLock.WaitAsync();
        try
        {
            await _imageReviewService.ApplyActionAsync(selected.Id, action);
            await RefreshReviewImagesAsync(selected.Id);
        }
        catch (Exception exception)
        {
            ReviewStatus.Text = $"The image review change could not be saved: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
        }
    }

    private async void OnNextSubjectClick(object? sender, RoutedEventArgs e)
    {
        if (_isAdvancingSubject)
            return;

        var activeSession = _activeCaptureSession;
        if (activeSession?.CurrentCaptureSet is null)
            return;

        _isAdvancingSubject = true;
        UpdateReviewControls();
        await _captureOperationLock.WaitAsync();
        try
        {
            var nextCaptureSet = await _captureWorkflowService.NextSubjectAsync(activeSession.Id);
            _observedSourceVersions.Clear();
            if (nextCaptureSet is null)
            {
                StopImageMonitoring();
                _activeCaptureSession = activeSession with { CurrentCaptureSet = null };
                CaptureSubjectCombo.SelectedItem = null;
                SelectedCaptureSubject.Text = "This subject is complete. Select another rostered subject, add a subject, or create an unidentified subject.";
            }
            else
            {
                _activeCaptureSession = activeSession with { CurrentCaptureSet = nextCaptureSet };
                await RefreshCaptureGroupsAsync(nextCaptureSet.GroupId);
                SelectedCaptureSubject.Text = $"Selected {nextCaptureSet.SubjectName}. Ready for this subject's images.";
            }

            UpdateCaptureControls();
            await RefreshReviewImagesAsync();
            MainTabs.SelectedItem = ImageReviewTab;
        }
        catch (Exception exception)
        {
            ReviewStatus.Text = $"Could not move to the next subject: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
            _isAdvancingSubject = false;
            UpdateReviewControls();
        }
    }

    private void ClearSelectedReviewPreview()
    {
        _previewVersion++;
        SelectedImagePreview.Source = null;
        _selectedFaceCrop?.Dispose();
        _selectedFaceCrop = null;
        _selectedReviewBitmap?.Dispose();
        _selectedReviewBitmap = null;
    }

    private void UpdateReviewControls()
    {
        var selected = CaptureImagesList.SelectedItem as CaptureImageReviewRow;
        SetPrimaryButton.IsEnabled = selected is not null;
        SetSecondaryButton.IsEnabled = selected is not null;
        ToggleBannerButton.IsEnabled = selected is not null;
        RejectImageButton.IsEnabled = selected is not null;
        NextSubjectButton.IsEnabled = !_isAdvancingSubject
            && _activeCaptureSession?.CurrentCaptureSet is not null;
        ToggleBannerButton.Content = "Banner";
        RejectImageButton.Content = selected?.ReviewState == CaptureImageReviewState.Rejected ? "Restore image" : "Reject image";
    }

    private async Task RefreshCaptureGroupsAsync(string? selectedGroupId = null)
    {
        var selectedEvent = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        var groups = selectedEvent is null
            ? Array.Empty<CaptureGroupChoice>()
            : await _captureWorkflowService.GetGroupsAsync(selectedEvent.Id);
        _allGroups = groups;
        _isPopulatingCaptureChoices = true;
        var league = LeagueCombo.SelectedItem as string;
        LeagueCombo.ItemsSource = new[] { "All leagues / divisions" }.Concat(groups.Select(x => x.LeagueName).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        LeagueCombo.SelectedItem = league is not null && LeagueCombo.Items.Contains(league) ? league : "All leagues / divisions";
        SetChoices(CaptureGroupCombo, FilterGroups(), item => item.Name,
            selectedGroupId ?? _activeCaptureSession?.CurrentCaptureSet?.GroupId);
        _isPopulatingCaptureChoices = false;
        await RefreshCaptureSubjectsAsync();
    }

    private async Task RefreshCaptureSubjectsAsync()
    {
        var selectedGroup = SelectedChoice<CaptureGroupChoice>(CaptureGroupCombo);
        var subjects = selectedGroup is null
            ? Array.Empty<CaptureSubjectChoice>()
            : await _captureWorkflowService.GetSubjectsAsync(selectedGroup.Id);
        AthleteCount.Text = $"{subjects.Count(x => x.HasPhotos)} photographed / {subjects.Count} athletes";
        _isPopulatingCaptureChoices = true;
        SetChoices(CaptureSubjectCombo, subjects, item => FormatSubject(item),
            _activeCaptureSession?.CurrentCaptureSet?.MembershipId);
        _isPopulatingCaptureChoices = false;
        UpdateCaptureControls();
    }

    private async void OnStartSessionClick(object? sender, RoutedEventArgs e)
    {
        var selectedEvent = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        if (selectedEvent is null || WorkflowTypeCombo.SelectedItem is not ComboBoxItem workflowItem
            || workflowItem.Tag is not CaptureWorkflowType workflowType)
            return;

        try
        {
            _activeCaptureSession = await _captureWorkflowService.StartSessionAsync(
                selectedEvent.Id,
                PhotographerName.Text ?? string.Empty,
                workflowType);
            UpdateCaptureControls();
            await RefreshReviewImagesAsync();
        }
        catch (Exception exception)
        {
            SessionStatus.Text = $"Session could not start: {exception.Message}";
        }
    }

    private async void OnEndSessionClick(object? sender, RoutedEventArgs e)
    {
        var activeSession = _activeCaptureSession;
        if (activeSession is null)
            return;
        await _captureOperationLock.WaitAsync();
        try
        {
            await _captureWorkflowService.EndSessionAsync(activeSession.Id);
            StopImageMonitoring();
            _activeCaptureSession = null;
            SelectedCaptureSubject.Text = "No subject selected.";
            UpdateCaptureControls();
            await RefreshReviewImagesAsync();
        }
        catch (Exception exception)
        {
            SessionStatus.Text = $"Session could not end: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
        }
    }

    private async void OnSelectSubjectClick(object? sender, RoutedEventArgs e)
    {
        if (SelectedChoice<CaptureSubjectChoice>(CaptureSubjectCombo) is { } subject)
            await SelectCaptureSubjectAsync(subject);
    }

    private async Task SelectCaptureSubjectAsync(CaptureSubjectChoice subject)
    {
        var activeSession = _activeCaptureSession;
        if (activeSession is null || activeSession.CurrentCaptureSet?.MembershipId == subject.MembershipId) return;
        await _captureOperationLock.WaitAsync();
        try
        {
            var captureSet = await _captureWorkflowService.SelectSubjectAsync(activeSession.Id, subject.MembershipId);
            SelectedCaptureSubject.Text = $"Selected {captureSet.SubjectName}. Ready for this subject's images.";
            _observedSourceVersions.Clear();
            if (_activeCaptureSession?.Id == activeSession.Id)
                _activeCaptureSession = activeSession with { CurrentCaptureSet = captureSet };
            _isPopulatingCaptureChoices = true;
            LeagueCombo.SelectedIndex = 0;
            TeamSearchBox.Text = "";
            _isPopulatingCaptureChoices = false;
            await RefreshCaptureGroupsAsync(captureSet.GroupId);
            UpdateCaptureControls();
            await RefreshReviewImagesAsync();
            MainTabs.SelectedItem = ImageReviewTab;
        }
        catch (Exception exception)
        {
            SelectedCaptureSubject.Text = $"Subject could not be selected: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
        }
    }

    private async void OnCreateManualSubjectClick(object? sender, RoutedEventArgs e)
    {
        var activeSession = _activeCaptureSession;
        if (activeSession is null)
            return;
        await _captureOperationLock.WaitAsync();
        try
        {
            var group = SelectedChoice<CaptureGroupChoice>(CaptureGroupCombo);
            var captureSet = await _captureWorkflowService.CreateManualSubjectAsync(
                activeSession.Id,
                new CaptureSubjectDetails(
                    ManualFirstName.Text,
                    ManualLastName.Text,
                    ManualRosterNumber.Text,
                    ManualRole.Text),
                group?.Id);
            _observedSourceVersions.Clear();
            if (_activeCaptureSession?.Id == activeSession.Id)
                _activeCaptureSession = activeSession with { CurrentCaptureSet = captureSet };
            SelectedCaptureSubject.Text = $"Selected {captureSet.SubjectName}. Ready for this subject's images.";
            ManualFirstName.Text = string.Empty;
            ManualLastName.Text = string.Empty;
            ManualRosterNumber.Text = string.Empty;
            ManualRole.Text = string.Empty;
            UpdateCaptureControls();
            _isPopulatingCaptureChoices = true;
            LeagueCombo.SelectedIndex = 0;
            TeamSearchBox.Text = "";
            _isPopulatingCaptureChoices = false;
            await RefreshCaptureGroupsAsync(captureSet.GroupId);
            await RefreshReviewImagesAsync();
        }
        catch (Exception exception)
        {
            SelectedCaptureSubject.Text = $"Subject could not be added: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
        }
    }

    private async void OnCreateUnidentifiedClick(object? sender, RoutedEventArgs e)
    {
        var activeSession = _activeCaptureSession;
        if (activeSession is null)
            return;
        await _captureOperationLock.WaitAsync();
        try
        {
            var group = SelectedChoice<CaptureGroupChoice>(CaptureGroupCombo);
            var captureSet = await _captureWorkflowService.CreateUnidentifiedSubjectAsync(
                activeSession.Id,
                UnidentifiedSubjectName.Text ?? string.Empty,
                group?.Id);
            SelectedCaptureSubject.Text = $"Selected unidentified subject: {captureSet.SubjectName}. Ready for this subject's images.";
            _observedSourceVersions.Clear();
            if (_activeCaptureSession?.Id == activeSession.Id)
                _activeCaptureSession = activeSession with { CurrentCaptureSet = captureSet };
            UpdateCaptureControls();
            UnidentifiedSubjectName.Text = string.Empty;
            await RefreshReviewImagesAsync();
        }
        catch (Exception exception)
        {
            SelectedCaptureSubject.Text = $"Subject could not be created: {exception.Message}";
        }
        finally
        {
            _captureOperationLock.Release();
        }
    }

    private void UpdateCaptureControls()
    {
        var active = _activeCaptureSession is not null;
        StartSessionButton.IsEnabled = !active
            && SelectedChoice<CaptureEventChoice>(CaptureEventCombo) is not null
            && !string.IsNullOrWhiteSpace(PhotographerName.Text);
        ExportAssociationsButton.IsEnabled = SelectedChoice<CaptureEventChoice>(CaptureEventCombo) is not null;
        ExportRosterButton.IsEnabled = SelectedChoice<CaptureEventChoice>(CaptureEventCombo) is not null;
        EndSessionButton.IsEnabled = active;
        CaptureEventCombo.IsEnabled = !active;
        PhotographerName.IsEnabled = !active;
        WorkflowTypeCombo.IsEnabled = !active;
        StationCodeCombo.IsEnabled = !active;
        MonitorFolderButton.IsEnabled = _isMonitoringImageFolder
            || (active && _activeCaptureSession?.CurrentCaptureSet is not null
                && !string.IsNullOrWhiteSpace(SmartShooterFolderPath.Text));
        MonitorFolderButton.Content = _isMonitoringImageFolder ? "Stop monitoring" : "Start monitoring";
        SelectSubjectButton.IsEnabled = active && SelectedChoice<CaptureSubjectChoice>(CaptureSubjectCombo) is not null;
        CreateManualSubjectButton.IsEnabled = active
            && (!string.IsNullOrWhiteSpace(ManualFirstName.Text) || !string.IsNullOrWhiteSpace(ManualLastName.Text));
        CreateUnidentifiedButton.IsEnabled = active && !string.IsNullOrWhiteSpace(UnidentifiedSubjectName.Text);
        CaptureGroupCombo.IsEnabled = CaptureGroupCombo.Items.Count > 0;
        CaptureSubjectCombo.IsEnabled = CaptureSubjectCombo.Items.Count > 0;
        SessionStatus.Text = _activeCaptureSession is { } activeSession
            ? $"Active: {activeSession.EventName} · {activeSession.PhotographerName} · {activeSession.ProfileName} · station {activeSession.StationCode}. This session resumes after restart."
            : "No active capture session.";
    }

    private static void SetChoices<T>(ComboBox comboBox, IEnumerable<T> choices, Func<T, string> label, string? selectedId)
    {
        comboBox.Items.Clear();
        foreach (var choice in choices.OrderBy(label, StringComparer.CurrentCultureIgnoreCase))
            comboBox.Items.Add(new ComboBoxItem { Content = label(choice), Tag = choice });
        var items = comboBox.Items.OfType<ComboBoxItem>().ToArray();
        comboBox.SelectedItem = items.FirstOrDefault(item => GetChoiceId(item.Tag)?.Equals(selectedId, StringComparison.Ordinal) == true)
            ?? items.FirstOrDefault();
    }

    private static T? SelectedChoice<T>(ComboBox comboBox) where T : class =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as T;

    private static string? GetChoiceId(object? choice) => choice switch
    {
        CaptureEventChoice item => item.Id,
        CaptureGroupChoice item => item.Id,
        CaptureSubjectChoice item => item.MembershipId,
        _ => null
    };

    private static string? SelectedStationCode(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;

    private static string FormatSubject(CaptureSubjectChoice subject)
    {
        var details = new[] { subject.RosterNumber, subject.Role }
            .Where(value => !string.IsNullOrWhiteSpace(value));
        var suffix = string.Join(" · ", details);
        var name = suffix.Length == 0 ? subject.DisplayName : $"{subject.DisplayName} · {suffix}";
        return subject.HasPhotos ? $"{name} · ✓ photographed" : name;
    }

    private IEnumerable<CaptureGroupChoice> FilterGroups() => _allGroups.Where(x =>
        (LeagueCombo.SelectedIndex <= 0 || x.LeagueName == LeagueCombo.SelectedItem as string)
        && x.Name.Contains(TeamSearchBox.Text ?? "", StringComparison.OrdinalIgnoreCase));

    private async void OnTeamSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isPopulatingCaptureChoices) return;
        _isPopulatingCaptureChoices = true;
        SetChoices(CaptureGroupCombo, FilterGroups(), x => x.Name, _activeCaptureSession?.CurrentCaptureSet?.GroupId);
        _isPopulatingCaptureChoices = false;
        await RefreshCaptureSubjectsAsync();
    }
    private void OnLeagueChanged(object? sender, SelectionChangedEventArgs e) => OnTeamSearchChanged(sender, null!);
    private async void OnAthleteSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_isPopulatingCaptureChoices && SelectedChoice<CaptureSubjectChoice>(CaptureSubjectCombo) is { } subject)
            await SelectCaptureSubjectAsync(subject);
    }
    private async void OnSubjectSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var version = ++_searchVersion;
        var query = SubjectSearchBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(query)) { SubjectSearchResults.IsVisible = false; return; }
        var job = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        if (job is null) return;
        await _captureOperationLock.WaitAsync();
        try
        {
            var matches = await _captureWorkflowService.SearchSubjectsAsync(job.Id, query);
            if (version != _searchVersion) return;
            SubjectSearchResults.ItemsSource = matches.Select(x => new ListBoxItem { Content = FormatSubject(x) + " · " + x.GroupName, Tag = x, MinHeight = 48 }).ToArray();
            SubjectSearchResults.IsVisible = true;
        }
        catch (Exception exception) { ReviewStatus.Text = $"Search unavailable: {exception.Message}"; }
        finally { _captureOperationLock.Release(); }
    }
    private async void OnSearchResultSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (SubjectSearchResults.SelectedItem is ListBoxItem { Tag: CaptureSubjectChoice subject })
        {
            SubjectSearchBox.Text = "";
            await SelectCaptureSubjectAsync(subject);
        }
    }
    private async void OnChooseMasterFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose master VP database folder", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        await _captureOperationLock.WaitAsync();
        try { await _jobMediaService.SetMasterPathAsync(path); MasterFolderPath.Text = path; MediaStatus.Text = "Master folder saved. Publish originals to populate it."; }
        catch (Exception exception) { MediaStatus.Text = exception.Message; }
        finally { _captureOperationLock.Release(); }
    }
    private async Task RunMediaActionAsync(Func<string, Task<JobMediaResult>> action)
    {
        var job = SelectedChoice<CaptureEventChoice>(CaptureEventCombo);
        if (job is null) { MediaStatus.Text = "Select a job first."; return; }
        await _captureOperationLock.WaitAsync();
        try { var result = await action(job.Id); MediaStatus.Text = $"{result.Images} image(s): {result.Location}"; }
        catch (Exception exception) { MediaStatus.Text = $"Image operation could not finish: {exception.Message}"; }
        finally { _captureOperationLock.Release(); }
    }
    private async void OnPublishOriginalsClick(object? sender, RoutedEventArgs e) =>
        await RunMediaActionAsync(id => _jobMediaService.PublishOriginalsAsync(id));
    private async void OnAssociateEditedClick(object? sender, RoutedEventArgs e) =>
        await RunMediaActionAsync(id => _jobMediaService.AssociateEditedAsync(id));
    private async void OnExportJobClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose export destination", AllowMultiple = false });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
        var kind = ExportKindCombo.SelectedIndex switch { 1 => JobImageExportKind.EditedPng, 2 => JobImageExportKind.BatchEditingOriginals, _ => JobImageExportKind.Originals };
        await RunMediaActionAsync(id => _jobMediaService.ExportAsync(id, path, kind));
    }

}

