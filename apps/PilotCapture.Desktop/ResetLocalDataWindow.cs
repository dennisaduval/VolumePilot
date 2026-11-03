using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace PilotCapture.Desktop;

internal static class LocalCaptureDataReset
{
    public static void Delete(string appDataPath)
    {
        // Move the whole store first so an open database cannot leave a half-reset
        // active store. Only this app-owned folder and its reset staging path are used.
        var staging = appDataPath + ".reset-pending";
        DeleteStaged(staging);
        if (!Directory.Exists(appDataPath)) return;
        RejectReparsePoint(appDataPath);
        Directory.Move(appDataPath, staging);
        DeleteStaged(staging);
    }

    private static void DeleteStaged(string path)
    {
        if (!Directory.Exists(path)) return;
        RejectReparsePoint(path);
        Directory.Delete(path, recursive: true);
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException($"The data folder is redirected: {path}");
    }
}

internal sealed class ResetLocalDataWindow : Window
{
    public ResetLocalDataWindow(string path)
    {
        Title = "Permanently delete local Pilot Capture data";
        Width = 580;
        Height = 430;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "This permanently deletes all local Pilot Capture information on this PC.", FontSize = 20, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = "All jobs, rosters, photo assignments, settings and local photo copies will be removed. Copies in external Smart Shooter, export and master folders stay in place. Export or back up anything you need before continuing. Close any other copy of Pilot Capture first.",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Type DELETE LOCAL DATA to confirm." });
        var phrase = new TextBox();
        panel.Children.Add(phrase);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinHeight = 44 };
        var confirm = new Button { Content = "Delete local data and close", IsEnabled = false, MinHeight = 44 };
        phrase.TextChanged += (_, _) => confirm.IsEnabled = phrase.Text == "DELETE LOCAL DATA";
        cancel.Click += (_, _) => Close(false);
        confirm.Click += (_, _) =>
        {
            if (phrase.Text == "DELETE LOCAL DATA") Close(true);
        };
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        Content = new ScrollViewer { Content = panel };
    }

    internal static Task ShowFailureAsync(Window owner, string detail)
    {
        var dialog = new Window
        {
            Title = "Local reset could not finish", Width = 520, Height = 270,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var panel = new StackPanel { Margin = new Thickness(22), Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Close other copies of Pilot Capture and retry the reset. The app may have already removed some local data.\n\n{detail}",
            TextWrapping = TextWrapping.Wrap
        });
        var close = new Button { Content = "OK", MinHeight = 44 };
        close.Click += (_, _) => dialog.Close();
        panel.Children.Add(close);
        dialog.Content = panel;
        return dialog.ShowDialog(owner);
    }
}
