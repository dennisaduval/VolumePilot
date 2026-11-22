using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PilotCapture.Application.FastCapture;
using PilotCapture.Application.Rosters;

namespace PilotCapture.Desktop;

public sealed class FastRosterMappingWindow : Window
{
    private static readonly string[] Uses = ["Ignore", "Team name", "Performance number", "Details / division / level", "Day", "Hall / stage", "Entry type (Team / Break / Awards)"];
    private readonly string _jobId, _sourceName;
    private readonly byte[] _content;
    private readonly RosterCsvDocument _raw;
    private readonly StackPanel _columns = new() { Spacing = 10 };
    private readonly Dictionary<int, ComboBox> _uses = [];
    private readonly TextBlock _error = new() { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _preview = new() { TextWrapping = TextWrapping.Wrap, FontSize = 16 };
    public NumericUpDown HeaderRow { get; } = new() { Minimum = 1, Increment = 1, Value = 1, FormatString = "0", Width = 100 };
    public IReadOnlyDictionary<int, ComboBox> ColumnUses => _uses;

    public FastRosterMappingWindow(string jobId, string sourceName, byte[] content)
    {
        _jobId = jobId; _sourceName = sourceName; _content = content;
        using var stream = new MemoryStream(content);
        _raw = RosterCsvReader.Read(stream);
        HeaderRow.Maximum = _raw.Rows.Count + 1;
        Title = "Pilot Capture | Map CSV columns"; Width = 960; Height = 700; MinWidth = 700; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F5F7FA"); FontFamily = new FontFamily("Segoe UI");
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        root.Children.Add(new TextBlock { Text = sourceName + "\nChoose how to use each column. All original rows and columns are retained, including ignored fields.",
            FontSize = 18, TextWrapping = TextWrapping.Wrap });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        header.Children.Add(new TextBlock { Text = "Header row", VerticalAlignment = VerticalAlignment.Center }); header.Children.Add(HeaderRow);
        header.Children.Add(new TextBlock { Text = "Change this if the CSV starts with a title or notes.", VerticalAlignment = VerticalAlignment.Center });
        Grid.SetRow(header, 1); root.Children.Add(header);
        var scroll = new ScrollViewer { Content = _columns, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 2); root.Children.Add(scroll);
        var notes = new TextBlock { Text = "Map Day and Hall to select a schedule at capture time. Entry type excludes breaks, awards and open slots. Times can stay ignored. Details shows values such as program and division. Without a performance number, CSV order is used.", TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(notes, 3); root.Children.Add(notes);
        var preview = new ScrollViewer { Content = _preview, MaxHeight = 140 };
        Grid.SetRow(preview, 4); root.Children.Add(preview);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 12 };
        footer.Children.Add(_error);
        var cancel = new Button { Content = "Cancel", MinHeight = 52 }; cancel.Click += (_, _) => Close(); Grid.SetColumn(cancel, 1); footer.Children.Add(cancel);
        var import = new Button { Content = "Import with these choices", MinHeight = 52 };
        import.Click += (_, _) => { try { Close(BuildCommand()); } catch (ArgumentException ex) { _error.Text = ex.Message; } };
        Grid.SetColumn(import, 2); footer.Children.Add(import); Grid.SetRow(footer, 5); root.Children.Add(footer);
        Content = root;
        HeaderRow.ValueChanged += (_, _) => RenderColumns();
        RenderColumns();
    }

    public FastRosterImportCommand BuildCommand()
    {
        var team = _uses.Where(x => x.Value.SelectedIndex == 1).Select(x => (int?)x.Key).ToArray();
        var order = _uses.Where(x => x.Value.SelectedIndex == 2).Select(x => (int?)x.Key).ToArray();
        int? Single(int use, string label)
        {
            var matches = _uses.Where(x => x.Value.SelectedIndex == use).Select(x => (int?)x.Key).ToArray();
            if (matches.Length > 1) throw new ArgumentException($"Choose at most one {label} column.");
            return matches.SingleOrDefault();
        }
        if (team.Length > 1 || order.Length > 1) throw new ArgumentException("Choose at most one Team name and one Performance number column. Other columns can be Details or Ignore.");
        var details = _uses.Where(x => x.Value.SelectedIndex == 3).Select(x => x.Key).Order().ToArray();
        var command = new FastRosterImportCommand(_jobId, _sourceName, _content, team.SingleOrDefault(), null, order.SingleOrDefault(), details,
            (int)(HeaderRow.Value ?? 1), Single(4, "Day"), Single(5, "Hall / stage"), Single(6, "Entry type"));
        FastRosterPlan.Read(command); // The preview and committed import use the same parser.
        return command;
    }

    private void RenderColumns()
    {
        _uses.Clear(); _columns.Children.Clear(); _error.Text = "";
        var document = FastRosterColumns.WithHeader(_raw, (int)(HeaderRow.Value ?? 1));
        var count = FastRosterColumns.ColumnCount(document);
        var team = Suggest(document, ["TEAMNAME", "TEAM", "PERFORMER", "TEAMSCHOOL", "SCHOOLNAME", "NAME"]);
        var order = Suggest(document, ["PERFORMANCENUMBER", "PERFORMANCEORDER", "RUNORDER", "ORDER", "PERF", "PERFORMANCENO"]);
        var day = Suggest(document, ["DAY", "PERFORMANCEDAY", "EVENTDAY", "DATE", "PERFORMANCEDATE"]);
        var hall = Suggest(document, ["HALL", "STAGE", "VENUE", "ARENA", "LOCATION"]);
        var type = Suggest(document, ["ENTRYTYPE", "SCHEDULETYPE"]);
        foreach (var index in Enumerable.Range(0, count).OrderBy(i => FastRosterColumns.Label(document, i), StringComparer.OrdinalIgnoreCase))
        {
            var label = FastRosterColumns.Label(document, index);
            var sample = string.Join(" / ", document.Rows.Select(r => index < r.Cells.Count ? r.Cells[index].Value : "").Where(v => v.Length > 0).Take(3));
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,3*,2*"), ColumnSpacing = 12, Margin = new Thickness(0, 4) };
            row.Children.Add(new TextBlock { Text = $"{label} [column {index + 1}]", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold });
            var preview = new TextBlock { Text = sample.Length > 180 ? sample[..180] + "…" : sample, TextWrapping = TextWrapping.Wrap };
            ToolTip.SetTip(preview, sample); Grid.SetColumn(preview, 1); row.Children.Add(preview);
            var normalized = Normalize(label);
            var use = new ComboBox { ItemsSource = Uses, HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 48,
                SelectedIndex = index == team ? 1 : index == order ? 2 : index == day ? 4 : index == hall ? 5 : index == type ? 6 :
                    new[] { "PROGRAM", "PROGRAMNAME", "CLUBNAME", "CLUB", "DIVISION", "LEVEL", "GROUP", "CATEGORY", "SPORT", "STATE", "CLASS", "GRADE" }.Contains(normalized) ? 3 : 0 };
            _uses.Add(index, use); Grid.SetColumn(use, 2); row.Children.Add(use); _columns.Children.Add(row);
        }
        foreach (var use in _uses.Values) use.SelectionChanged += (_, _) => RenderPreview();
        RenderPreview();
    }
    private void RenderPreview()
    {
        try
        {
            var plan = FastRosterPlan.Read(BuildCommand());
            var groups = plan.Entries.GroupBy(e => (e.Day, e.Hall)).ToArray();
            _preview.Text = $"Preview: {plan.Entries.Count} performances in {groups.Length} schedule(s).\n" +
                string.Join("\n", groups.Take(12).Select(g => $"{(g.Key.Day.Length > 0 ? g.Key.Day : "No day")} / {(g.Key.Hall.Length > 0 ? g.Key.Hall : "No hall")}: {g.Count()} performances")) +
                "\n" + string.Join("\n", plan.Entries.Take(3).Select(e => FastPerformanceSearch.Label(new() { Name = e.Name, PerformanceLabel = e.Order, Details = e.Details })));
            _error.Text = "";
        }
        catch (ArgumentException ex) { _preview.Text = ""; _error.Text = ex.Message; }
    }
    private static int Suggest(RosterCsvDocument document, string[] aliases)
    {
        foreach (var alias in aliases)
            for (var i = 0; i < document.Headers.Count; i++) if (Normalize(document.Headers[i]) == alias) return i;
        return -1;
    }
    private static string Normalize(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}
