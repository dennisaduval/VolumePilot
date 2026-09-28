using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using PilotCapture.Application.Rosters;
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

    public MainWindow(IRosterImportService rosterImportService)
    {
        _rosterImportService = rosterImportService;
        AvaloniaXamlLoader.Load(this);
        EventName.TextChanged += (_, _) => UpdateImportAvailability();
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
        for (var index = 0; index < headers.Count; index++)
            comboBox.Items.Add($"[{index}] {headers[index]}");

        comboBox.SelectedIndex = selectedColumn is int column ? column + 1 : 0;
        comboBox.IsEnabled = true;
    }

    private static int? SelectedColumn(ComboBox comboBox) =>
        comboBox.SelectedIndex > 0 ? comboBox.SelectedIndex - 1 : null;
}
