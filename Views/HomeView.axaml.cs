using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GrinLauncher.Views;

public partial class HomeView : UserControl
{
    public event Action? PlayRequested;

    public HomeView()
    {
        InitializeComponent();
    }

    private void OnPlayButtonClick(object? sender, RoutedEventArgs e) => PlayRequested?.Invoke();

    public void SetVersionBadge(string text) => VersionBadgeText.Text = text;

    public void SetSummary(string text) => SummaryText.Text = text;

    public void SetModList(IEnumerable<ModEntry> mods) => ModListItems.ItemsSource = mods;

    public void SetPlayEnabled(bool enabled) => PlayButton.IsEnabled = enabled;

    public void SetProgressVisible(bool visible) => ProgressPanel.IsVisible = visible;

    public void SetProgressStatus(string text) => ProgressStatusText.Text = text;

    public void SetProgress(double percent)
    {
        Progress.Value = percent;
        ProgressPercentText.Text = $"{(int)percent}%";
    }

    public void AppendLog(string message)
    {
        LogBox.Text += message + Environment.NewLine;
        LogBox.CaretIndex = LogBox.Text.Length;
    }
}
