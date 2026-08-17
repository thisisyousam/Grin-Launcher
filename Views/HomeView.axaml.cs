using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace GrinLauncher.Views;

public partial class HomeView : UserControl
{
    public event Action? PlayRequested;
    public event Action? UpdateRequested;

    private bool _updateMode;

    public HomeView()
    {
        InitializeComponent();
    }

    private void OnPlayButtonClick(object? sender, RoutedEventArgs e)
    {
        if (_updateMode) UpdateRequested?.Invoke();
        else PlayRequested?.Invoke();
    }

    public void SetVersionBadge(string text) => VersionBadgeText.Text = text;

    public void SetSummary(string text) => SummaryText.Text = text;

    public void SetModList(IEnumerable<ModEntry> mods) => ModListItems.ItemsSource = mods;

    public void SetPlayEnabled(bool enabled) => PlayButton.IsEnabled = enabled;

    // 새 모드/버전이 있으면 실행 버튼을 업데이트 버튼으로 바꾼다 — 클릭 시 게임을
    // 바로 켜는 대신 모드부터 새로 받도록 강제.
    public void SetUpdateMode(bool needsUpdate)
    {
        _updateMode = needsUpdate;
        PlayButton.Content = needsUpdate ? "업데이트" : "실행";
    }

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
