using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace PilotCapture.Desktop;

public sealed partial class MainWindow : Window
{
    public MainWindow() => AvaloniaXamlLoader.Load(this);
}
