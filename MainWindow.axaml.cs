using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CmlLib.Core.Auth;
using GrinLauncher.Views;

namespace GrinLauncher;

public partial class MainWindow : Window
{
    private static readonly HttpClient AvatarHttpClient = new();

    private readonly LauncherService _launcherService = new();
    private MSession? _session;
    private string _currentPage = "home";
    private (Button Button, Border Circle, Avalonia.Controls.Shapes.Path Icon, string Key)[] _navItems = [];

    public MainWindow()
    {
        InitializeComponent();

        _navItems =
        [
            (NavHomeButton, NavHomeCircle, NavHomeIcon, "home"),
            (NavSkinsButton, NavSkinsCircle, NavSkinsIcon, "skins"),
            (NavSettingsButton, NavSettingsCircle, NavSettingsIcon, "settings"),
            (NavAccountButton, NavAccountCircle, NavAccountPlaceholder, "settings"),
        ];

        HomePage.PlayRequested += OnPlayRequested;
        SettingsPage.LogoutRequested += OnLogoutRequested;
        SkinsPage.SkinApplyRequested += OnSkinApplyRequested;

        _launcherService.LogMessage += message => Dispatcher.UIThread.Post(() => HomePage.AppendLog(message));
        _launcherService.FileProgressChanged += (_, args) => Dispatcher.UIThread.Post(() =>
        {
            HomePage.SetProgressStatus($"{args.Name} ({args.ProgressedTasks}/{args.TotalTasks})");
        });
        _launcherService.ByteProgressChanged += (_, args) => Dispatcher.UIThread.Post(() =>
        {
            if (args.TotalBytes <= 0) return;
            var percent = (double)args.ProgressedBytes / args.TotalBytes * 100;
            HomePage.SetProgress(percent);
        });

        SettingsPage.SetGameDirectory(System.IO.Path.GetFullPath(_launcherService.Path.BasePath));
        SettingsPage.SetJavaPath(DetectJavaPath());
        SkinsPage.SetSkinsDirectory(System.IO.Path.Combine(_launcherService.Path.BasePath, "skins"));

        ApplyNavHighlight();
        LoadModList();
    }

    private static string DetectJavaPath()
    {
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (string.IsNullOrEmpty(javaHome)) return "시스템 기본값 사용";

        var exeName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        return System.IO.Path.Combine(javaHome, "bin", exeName);
    }

    private async void LoadModList()
    {
        try
        {
            var manifest = await _launcherService.GetManifestAsync();
            HomePage.SetVersionBadge($"Fabric · {manifest.mcVersion}");
            HomePage.SetSummary($"모드 {manifest.mods.Count}개 설치됨");
            HomePage.SetModList(manifest.mods);
        }
        catch (Exception ex)
        {
            HomePage.SetSummary("모드 목록을 불러오지 못했습니다");
            HomePage.AppendLog("모드 목록 로드 실패: " + ex.Message);
        }
    }

    private void OnNavHomeClick(object? sender, RoutedEventArgs e) => NavigateTo("home");
    private void OnNavSkinsClick(object? sender, RoutedEventArgs e) => NavigateTo("skins");
    private void OnNavSettingsClick(object? sender, RoutedEventArgs e) => NavigateTo("settings");
    private void OnNavAccountClick(object? sender, RoutedEventArgs e) => NavigateTo("settings");

    private void NavigateTo(string page)
    {
        _currentPage = page;
        HomePage.IsVisible = page == "home";
        SkinsPage.IsVisible = page == "skins";
        SettingsPage.IsVisible = page == "settings";
        ApplyNavHighlight();
    }

    private void ApplyNavHighlight()
{
    // 선택 아이콘도 유리 재질로 (2026-08-08 4차, design.md 참고): 기존 불투명 흰/회색
    // 원 대신 NavIconActiveGlassBrush(75% 알파) + SidebarGlassBorderBrush 테두리로
    // "떠 보이는 유리 캡슐" 톤을 사이드바 배경과 맞춤
    this.TryFindResource("NavIconActiveGlassBrush", out var activeCircleRes);
    this.TryFindResource("SidebarGlassBorderBrush", out var activeBorderRes);
    var activeCircle = (IBrush)activeCircleRes!;
    var activeBorder = (IBrush)activeBorderRes!;
    var iconActive = Brush.Parse("#1A1B1E");
    var iconInactive = Brush.Parse("#6B7280");

    foreach (var (button, circle, icon, key) in _navItems)
    {
        // 계정 아바타(NavAccountButton)는 설정과 목적지(key)만 같을 뿐 별도 선택 상태를
        // 갖지 않는 얼굴 이미지 요약 프레임이다 — 다른 아이콘처럼 유리 하이라이트
        // 배경을 얹으면 내부 32x32 얼굴 Border가 불투명 배경이 없어 스킨 얼굴 뒤로
        // 흰 유리 원이 비쳐 보인다. 설정 아이콘 쪽만 선택 표시를 하고 계정 아바타는
        // 항상 배경 없이 얼굴 이미지 그대로 유지한다.
        var active = button != NavAccountButton && key == _currentPage;
        circle.Background = active ? activeCircle : Brushes.Transparent;
        circle.BorderBrush = active ? activeBorder : Brushes.Transparent;
        circle.BorderThickness = active ? new Thickness(1) : new Thickness(0);
        icon.Stroke = active ? iconActive : iconInactive;
    }
}

    private async void OnLoginClick(object? sender, RoutedEventArgs e)
    {
        LoginButton.IsEnabled = false;
        LoginStatusText.Text = "로그인 중입니다. 브라우저 창에서 Microsoft 계정으로 로그인해주세요...";

        try
        {
            var session = await _launcherService.GetSessionAsync();
            _session = session;

            ShowAccount(session);
            _ = LoadAvatarAsync(session.UUID);

            LoginScreen.IsVisible = false;
            Shell.IsVisible = true;
            NavigateTo("home");
        }
        catch (Exception ex)
        {
            LoginStatusText.Text = "로그인 실패: " + ex.Message;
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void ShowAccount(MSession session)
    {
        SettingsPage.SetProfile(session.Username ?? "", session.UUID ?? "");
    }

    private async Task LoadAvatarAsync(string? uuid)
    {
        if (string.IsNullOrEmpty(uuid)) return;

        try
        {
            var (skinUrl, isSlim) = await _launcherService.GetSkinUrlAsync(uuid);
            if (string.IsNullOrEmpty(skinUrl)) return;

            var bytes = await AvatarHttpClient.GetByteArrayAsync(skinUrl);
            using var stream = new MemoryStream(bytes);
            var skin = new Bitmap(stream);

            SkinsPage.SetCurrentSkin(bytes, skin, isSlim);
            UpdateAvatarDisplay(skin);
        }
        catch (Exception ex)
        {
            // 실패 시 실루엣 플레이스홀더 유지
            Console.WriteLine("[avatar] LoadAvatarAsync EXCEPTION: " + ex);
        }
    }

    // 사이드바 아바타 아이콘 + 계정 페이지 아바타에 스킨 얼굴/모자 레이어를 반영한다.
    // 로그인 직후 스킨 로드와 스킨 적용 성공 시 둘 다에서 재사용.
    private void UpdateAvatarDisplay(Bitmap skin)
    {
        // 베이스 얼굴 레이어: 포맷(64x32/64x64) 관계없이 항상 (8,8)-(16,16)
        var face = new CroppedBitmap(skin, new PixelRect(8, 8, 8, 8));

        // 모자/헬멧 오버레이 레이어: 항상 (40,8)-(48,16), 투명 부분은 그대로 유지됨
        var hat = new CroppedBitmap(skin, new PixelRect(40, 8, 8, 8));

        NavAccountFace.Source = face;
        NavAccountFace.IsVisible = true;

        NavAccountHat.Source = hat;
        NavAccountHat.IsVisible = true;

        NavAccountPlaceholder.IsVisible = false;
        SettingsPage.SetAvatar(face, hat);
    }

    private async void OnSkinApplyRequested(SkinItem item)
    {
        if (item.Bytes is null || _session?.AccessToken is null)
        {
            SkinsPage.SetApplyResult(item.Id, false, "로그인 세션이 없습니다");
            return;
        }

        try
        {
            await _launcherService.ChangeSkinAsync(_session.AccessToken, item.Bytes, item.IsSlim);

            using var stream = new MemoryStream(item.Bytes);
            UpdateAvatarDisplay(new Bitmap(stream));

            SkinsPage.SetApplyResult(item.Id, true);
        }
        catch (Exception ex)
        {
            HomePage.AppendLog("스킨 적용 실패: " + ex.Message);
            SkinsPage.SetApplyResult(item.Id, false, ex.Message);
        }
    }

    private async void OnPlayRequested()
    {
        HomePage.SetPlayEnabled(false);
        HomePage.SetProgressVisible(true);
        try
        {
            var session = _session!;
            var maxRamMb = SettingsPage.MemoryGb * 1024;

            HomePage.SetProgressStatus("Fabric 설치 중...");
            var fabricVersionName = await _launcherService.InstallFabricAsync();

            HomePage.SetProgressStatus("모드 다운로드 중...");
            var manifest = await _launcherService.GetManifestAsync();
            await _launcherService.DownloadModsAsync(manifest);

            HomePage.SetProgressStatus("게임 실행 중...");
            var process = await _launcherService.LaunchGameAsync(fabricVersionName, session, maxRamMb);

            HomePage.SetProgressStatus("실행 완료");
            HomePage.SetProgressVisible(false);

            process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                HomePage.SetPlayEnabled(true);
                HomePage.AppendLog("게임이 종료되었습니다.");
            });
        }
        catch (Exception ex)
        {
            HomePage.SetProgressStatus("오류 발생");
            HomePage.AppendLog("오류: " + ex.Message);
            HomePage.SetPlayEnabled(true);
        }
    }

    private void OnLogoutRequested()
    {
        _session = null;

        Shell.IsVisible = false;
        LoginScreen.IsVisible = true;
        LoginStatusText.Text = "";

        NavAccountFace.IsVisible = false;
        NavAccountHat.IsVisible = false;
        NavAccountPlaceholder.IsVisible = true;
        SettingsPage.ResetAvatar();

        HomePage.SetPlayEnabled(true);
        HomePage.SetProgressVisible(false);
        NavigateTo("home");
    }
}
