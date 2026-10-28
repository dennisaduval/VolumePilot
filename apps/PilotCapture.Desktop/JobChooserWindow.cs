using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using PilotCapture.Domain;
namespace PilotCapture.Desktop;

public sealed class JobChooserWindow : Window
{
    public event Action<CaptureJobType>? JobSelected;
    public event Action? LegacySelected;
    public JobChooserWindow()
    {
        Title = "VolumePilot | Pilot Capture"; Width = 780; Height = 720; MinWidth = 640; MinHeight = 640;
        Background = new SolidColorBrush(Color.Parse("#F5F7FA"));
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(36), VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock { Text = "VolumePilot · Pilot Capture", FontSize = 18 });
        panel.Children.Add(new TextBlock { Text = "What type of job are we shooting?", FontSize = 28, FontWeight = FontWeight.SemiBold });
        Add("Print on site · Team Only", CaptureJobType.PrintOnSiteTeamsOnly, true);
        Add("Print on site · Team and Individuals", CaptureJobType.PrintOnSiteTeamAndIndividual, true);
        Add("Action", CaptureJobType.ActionPhotography, true);
        Add("Leagues and Schools · coming later", CaptureJobType.TeamAndIndividual, false);
        Add("Individual Teams · coming later", CaptureJobType.IndividualTeams, false);
        var legacy = new Button { Content = "Open previous roster workspace", HorizontalAlignment = HorizontalAlignment.Left };
        legacy.Click += (_, _) => LegacySelected?.Invoke(); panel.Children.Add(legacy);
        Content = new ScrollViewer { Content = panel };
        void Add(string label, CaptureJobType type, bool enabled)
        {
            var button = new Button { Content = label, MinHeight = 68, FontSize = 20, HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = enabled };
            button.Click += (_, _) => JobSelected?.Invoke(type); panel.Children.Add(button);
        }
    }
}
