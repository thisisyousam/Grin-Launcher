using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace GrinLauncher.Views;

public partial class SettingsView : UserControl
{
    // Java 경로 / 게임 디렉토리는 읽기 전용 표시입니다 — 실제로 조정 가능한 설정은 메모리 할당뿐입니다.
    public int MemoryGb { get; private set; } = 6;

    public event Action? LogoutRequested;

    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnMemoryChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        MemoryGb = (int)e.NewValue;
        MemoryLabelText.Text = $"{MemoryGb}GB";
    }

    public void SetJavaPath(string path) => JavaPathText.Text = path;

    public void SetGameDirectory(string path) => GameDirText.Text = path;

    private void OnLogoutClick(object? sender, RoutedEventArgs e) => LogoutRequested?.Invoke();

    public void SetProfile(string nickname, string uuid)
    {
        NicknameText.Text = nickname;
        UuidText.Text = uuid;
    }

    public void SetAvatar(IImage face, IImage hat)
    {
        AvatarFace.Source = face;
        AvatarFace.IsVisible = true;
        AvatarHat.Source = hat;
        AvatarHat.IsVisible = true;
        AvatarPlaceholder.IsVisible = false;
    }

    public void ResetAvatar()
    {
        AvatarFace.IsVisible = false;
        AvatarHat.IsVisible = false;
        AvatarPlaceholder.IsVisible = true;
    }
}
