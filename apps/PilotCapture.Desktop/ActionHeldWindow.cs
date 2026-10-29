using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using PilotCapture.Application.FastCapture;
using PilotCapture.Domain;
using PilotCapture.Infrastructure.Persistence;

namespace PilotCapture.Desktop;

public sealed class ActionHeldWindow : Window
{
    public ActionHeldWindow(FastCaptureController controller, FastCaptureSnapshot snapshot)
    {
        Title = "Pilot Capture | Held Action photos"; Width = 1000; Height = 700;
        var root = new Grid { RowDefinitions = new("Auto,*,Auto,Auto"), RowSpacing = 12, Margin = new Thickness(20) };
        var label = new TextBlock { Text = "Held photos are archived. Choose a photo and the correct performance to release it.", TextWrapping = TextWrapping.Wrap, FontSize = 18 };
        root.Children.Add(label);
        var list = new ListBox { SelectionMode = SelectionMode.Multiple, ItemTemplate = new FuncDataTemplate<ActionReceipt>((r, _) => new TextBlock
            { Text = r is null ? "" : $"{Path.GetFileName(r.SourcePath)} · {r.CameraKey}\n{r.Issue}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4), FontSize = 17 }) };
        Grid.SetRow(list, 1); root.Children.Add(list);
        var teams = new ComboBox { ItemsSource = snapshot.Teams.Select(t => new ComboBoxItem { Content = $"{t.Day} {t.Hall} · {FastPerformanceSearch.Label(t)}", Tag = t.Id }).ToArray(),
            MinHeight = 52, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetRow(teams, 2); root.Children.Add(teams);
        var assign = new Button { Content = "Assign selected photos to this performance", MinHeight = 52 };
        Grid.SetRow(assign, 3); root.Children.Add(assign);
        async Task Refresh()
        {
            var state = await controller.ActionAsync(s => s.GetStatusAsync(snapshot.Job.Id));
            list.ItemsSource = state.Held;
            label.Text = $"{state.HeldCount} held photo(s). Showing up to 100. Select photos and their correct performance.";
        }
        Loaded += async (_, _) => { try { await Refresh(); } catch (Exception ex) { label.Text = ex.Message; } };
        assign.Click += async (_, _) =>
        {
            if (teams.SelectedItem is not ComboBoxItem { Tag: string teamId }) { label.Text = "Choose a performance."; return; }
            var selected = list.SelectedItems?.OfType<ActionReceipt>().Select(r => r.Id).ToArray() ?? [];
            assign.IsEnabled = false;
            try
            {
                foreach (var id in selected) await controller.ActionAsync(s => s.AssignHeldAsync(snapshot.Job.Id, id, teamId));
                await controller.ExecuteAsync(s => s.ScanAsync(snapshot.Job.Id, actionPaths: []));
                await Refresh();
            }
            catch (Exception ex) { label.Text = ex.Message; }
            finally { assign.IsEnabled = true; }
        };
        Content = root;
    }
}
