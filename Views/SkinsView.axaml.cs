using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using GrinLauncher.Rendering;

namespace GrinLauncher.Views;

// 업로드한 스킨 파일/목록은 이 뷰가 로컬 디스크(SetSkinsDirectory)에 직접 저장해서
// 재실행 후에도 남아있게 한다. 실제 Mojang 계정에 스킨을 반영하는 네트워크 호출은
// 세션(액세스 토큰)이 필요해 이 뷰가 직접 하지 않고, SkinApplyRequested 이벤트로
// MainWindow -> LauncherService.ChangeSkinAsync에 위임한다.
public partial class SkinsView : UserControl
{
    private readonly List<SkinItem> _skins = new()
    {
        new SkinItem("default", "기본 (Steve)", null),
    };

    private bool _has3D = true;
    private string _selectedId = "default";
    private string? _pendingId;
    private SkinItem? _pendingItem;
    private SkinItem? _pendingRevertSnapshot;
    private string? _lastError;
    private SkinItem? _lastFailedItem;
    private SkinItem? _lastFailedRevertSnapshot;
    private string? _skinsDir;
    private Point? _lastPointer;

    // 목록 썸네일용 정면 합성 이미지 캐시. 원본 텍스처(Thumbnail)는 3D 뷰어가 그대로
    // 써야 해서 바꿀 수 없으므로, 목록에서만 쓸 별도 이미지를 스킨 Id 기준으로 캐싱해
    // Refresh() 때마다(팔 두께 토글 등으로 자주 호출됨) 다시 합성하지 않게 한다.
    private readonly Dictionary<string, (Bitmap Source, bool IsSlim, Bitmap? Composite)> _listThumbnailCache = new();

    public event Action<SkinItem>? SkinApplyRequested;

    public SkinsView()
    {
        InitializeComponent();

        SkinViewer3D.SetBackgroundColor(255, 255, 255);
        SkinViewer3D.SetBackgroundImage(SkinViewerControl.LoadBundledBackground("bg_sunset.png"));
        SkinViewer3D.InitFailed += (_, _) =>
        {
            // OpenGL 컨텍스트 생성/셰이더 컴파일 실패(일부 macOS/드라이버 환경) —
            // 3D 뷰를 트리에서 제거하고 기존 2D 폴백으로 전환.
            _has3D = false;
            if (SkinViewer3D.Parent is Panel host) host.Children.Remove(SkinViewer3D);
            SkinViewerInteractionOverlay.IsVisible = false;
            Refresh();
        };

        SkinViewerInteractionOverlay.PointerPressed += OnViewerPointerPressed;
        SkinViewerInteractionOverlay.PointerMoved += OnViewerPointerMoved;
        SkinViewerInteractionOverlay.PointerReleased += OnViewerPointerReleased;
        SkinViewerInteractionOverlay.PointerWheelChanged += OnViewerPointerWheelChanged;

        // 생성자 시점엔 아직 비주얼 트리에 붙기 전이라 테마 리소스(FindResource)를
        // 못 찾으므로, 최초 Refresh()는 Loaded로 미룬다.
        Loaded += (_, _) => Refresh();
    }

    private void OnViewerPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _lastPointer = e.GetPosition(SkinViewerInteractionOverlay);
        e.Pointer.Capture(SkinViewerInteractionOverlay);
    }

    private void OnViewerPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_lastPointer is not { } last || e.Pointer.Captured != SkinViewerInteractionOverlay) return;
        var pos = e.GetPosition(SkinViewerInteractionOverlay);
        SkinViewer3D.ApplyDrag(pos.X - last.X, pos.Y - last.Y);
        _lastPointer = pos;
    }

    private void OnViewerPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _lastPointer = null;
        e.Pointer.Capture(null);
    }

    private void OnViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        SkinViewer3D.ApplyZoom(e.Delta.Y);
    }

    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "스킨 이미지 선택",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("PNG 이미지") { Patterns = new[] { "*.png" } } }
        });

        var file = files.FirstOrDefault();
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);
        var bytes = memoryStream.ToArray();

        memoryStream.Position = 0;
        var bitmap = new Bitmap(memoryStream);

        var id = "up-" + Guid.NewGuid().ToString("N")[..8];
        var name = Path.GetFileNameWithoutExtension(file.Name);
        var item = new SkinItem(id, name, bitmap) { Bytes = bytes };
        _skins.Add(item);
        PersistSkin(item);
        Refresh();
    }

    // 로그인 계정에서 실제로 불러온 스킨을 목록의 기본 항목에 반영한다.
    // MainWindow가 스킨 URL을 성공적으로 로드했을 때 호출. pngBytes를 같이 들고 있어야
    // 나중에 이 항목으로 "되돌리기"를 눌렀을 때도 실제 Mojang 재적용이 가능하다.
    public void SetCurrentSkin(byte[] pngBytes, Bitmap skinBitmap, bool isSlim)
    {
        var updated = new SkinItem("default", "현재 스킨", skinBitmap) { IsSlim = isSlim, Bytes = pngBytes };
        var index = _skins.FindIndex(s => s.Id == "default");
        if (index >= 0) _skins[index] = updated;
        else _skins.Insert(0, updated);
        Refresh();
    }

    // 스킨을 저장해둘 디렉토리(런처의 게임 디렉토리 하위)를 지정하고, 이전에 업로드해
    // 저장해둔 스킨이 있으면 목록에 불러온다. MainWindow 생성자에서 한 번 호출된다.
    public void SetSkinsDirectory(string path)
    {
        _skinsDir = path;
        LoadPersistedSkins();
    }

    private void LoadPersistedSkins()
    {
        if (_skinsDir is null) return;

        var manifestPath = Path.Combine(_skinsDir, "manifest.json");
        if (!File.Exists(manifestPath)) return;

        try
        {
            var entries = JsonSerializer.Deserialize<List<SkinManifestEntry>>(File.ReadAllText(manifestPath)) ?? new();
            foreach (var entry in entries)
            {
                var filePath = Path.Combine(_skinsDir, entry.FileName);
                if (!File.Exists(filePath)) continue;

                var bytes = File.ReadAllBytes(filePath);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                _skins.Add(new SkinItem(entry.Id, entry.Name, bitmap) { IsSlim = entry.IsSlim, Bytes = bytes });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("[skin] LoadPersistedSkins EXCEPTION: " + ex);
        }
    }

    private void PersistSkin(SkinItem item)
    {
        if (_skinsDir is null || item.Bytes is null) return;

        try
        {
            Directory.CreateDirectory(_skinsDir);
            var fileName = item.Id + ".png";
            File.WriteAllBytes(Path.Combine(_skinsDir, fileName), item.Bytes);

            var manifestPath = Path.Combine(_skinsDir, "manifest.json");
            var entries = File.Exists(manifestPath)
                ? JsonSerializer.Deserialize<List<SkinManifestEntry>>(File.ReadAllText(manifestPath)) ?? new()
                : new List<SkinManifestEntry>();

            entries.Add(new SkinManifestEntry(item.Id, item.Name, item.IsSlim, fileName));
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[skin] PersistSkin EXCEPTION: " + ex);
        }
    }

    // 스킨 항목 클릭 = "이 스킨을 실제로 적용해줘" 요청. 로컬에서 바로 선택 상태로
    // 바꾸지 않고, MainWindow가 Mojang 쪽 적용까지 마치고 SetApplyResult를 불러줄 때만
    // 선택 상태로 확정한다 — 그래야 목록 표시와 실제 계정 스킨이 어긋나지 않는다.
    private void OnSkinItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinItem item }) return;
        if (item.Id == _selectedId || item.Id == _pendingId) return;

        AttemptApply(item, revertSnapshot: null);
    }

    // OnSkinItemClick/OnSlimArmToggleClick(최초 시도)과 OnRetryApplyClick(재시도)이
    // 공유하는 실제 적용 요청 로직. revertSnapshot은 팔 두께 토글처럼 낙관적으로 먼저
    // 바꿔둔 상태가 있을 때만 넘기고, 실패 시 SetApplyResult가 그걸로 되돌린다.
    private void AttemptApply(SkinItem item, SkinItem? revertSnapshot)
    {
        _pendingId = item.Id;
        _pendingItem = item;
        _pendingRevertSnapshot = revertSnapshot;
        _lastError = null;
        _lastFailedItem = null;
        _lastFailedRevertSnapshot = null;
        Refresh();
        SkinApplyRequested?.Invoke(item);
    }

    // 적용 실패 후 "재시도" 클릭. 방금 실패한 요청을 그대로 다시 보낸다 — 팔 두께
    // 토글처럼 낙관적으로 바꿔둔 상태가 있었다면(revertSnapshot) 그 낙관적 표시도
    // 다시 적용해서 최초 시도와 동일한 화면을 보여준다.
    private void OnRetryApplyClick(object? sender, RoutedEventArgs e)
    {
        if (_lastFailedItem is not { } item || _pendingId is not null) return;

        var index = _skins.FindIndex(s => s.Id == item.Id);
        if (index >= 0) _skins[index] = item;

        AttemptApply(item, _lastFailedRevertSnapshot);
    }

    // 목록에서 스킨 삭제. 계정 "현재 스킨"(id="default")은 로컬 파일이 아니라
    // CanDelete=false라 버튼 자체가 안 보이지만, 방어적으로 한 번 더 막는다.
    // 적용 중이거나 적용 대기 중인 스킨은 상태가 꼬이지 않도록 삭제를 막는다.
    private void OnDeleteSkinClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SkinItem item } || item.Id == "default") return;
        if (item.Id == _pendingId) return;

        _skins.RemoveAll(s => s.Id == item.Id);
        RemovePersistedSkin(item.Id);

        if (_listThumbnailCache.Remove(item.Id, out var cached)) cached.Composite?.Dispose();
        if (_selectedId == item.Id) _selectedId = "default";

        Refresh();
    }

    private void RemovePersistedSkin(string id)
    {
        if (_skinsDir is null) return;

        try
        {
            var filePath = Path.Combine(_skinsDir, id + ".png");
            if (File.Exists(filePath)) File.Delete(filePath);

            var manifestPath = Path.Combine(_skinsDir, "manifest.json");
            if (!File.Exists(manifestPath)) return;

            var entries = JsonSerializer.Deserialize<List<SkinManifestEntry>>(File.ReadAllText(manifestPath)) ?? new();
            entries.RemoveAll(entry => entry.Id == id);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(entries));
        }
        catch (Exception ex)
        {
            Console.WriteLine("[skin] RemovePersistedSkin EXCEPTION: " + ex);
        }
    }

    // MainWindow가 실제 Mojang 적용 결과를 알려줄 때 호출. 실패 사유(error)의 원문은
    // HomeView 로그에만 남고(MainWindow.OnSkinApplyRequested), 이 화면은 다른 페이지를
    // 보고 있을 때도 실패했다는 사실 자체는 조용히 되돌리지 않고 짧게 보여준 뒤 재시도
    // 버튼을 제공한다 — 예외 메시지 전문은 사용자에게 도움이 안 되고 원인을 숨기는 것도
    // 아니므로 뺐다.
    public void SetApplyResult(string id, bool success, string? error = null)
    {
        if (_pendingId != id) return;

        _pendingId = null;
        if (success)
        {
            _selectedId = id;
            _lastError = null;
            _lastFailedItem = null;
            _lastFailedRevertSnapshot = null;
        }
        else
        {
            if (_pendingRevertSnapshot is not null)
            {
                // 팔 두께 토글처럼 낙관적으로 먼저 바꿔둔 상태를 실패 시 원래대로 되돌린다.
                var index = _skins.FindIndex(s => s.Id == id);
                if (index >= 0) _skins[index] = _pendingRevertSnapshot;
            }
            _lastError = error ?? "알 수 없는 오류";
            _lastFailedItem = _pendingItem;
            _lastFailedRevertSnapshot = _pendingRevertSnapshot;
        }
        _pendingItem = null;
        _pendingRevertSnapshot = null;
        Refresh();
    }

    // 현재 적용중인 스킨의 팔 두께(클래식 4px/슬림 3px)를 바꾸는 것도 실제로는
    // Mojang에 같은 PNG를 variant만 바꿔 재업로드하는 것이므로, 스킨 선택과 동일한
    // SkinApplyRequested 파이프라인을 그대로 태운다. 실패하면 원래 두께로 되돌린다.
    private void OnSlimArmToggleClick(object? sender, RoutedEventArgs e)
    {
        if (_pendingId is not null) return;

        var index = _skins.FindIndex(s => s.Id == _selectedId);
        if (index < 0) return;

        var original = _skins[index];
        if (original.Bytes is null) return;

        var updated = original with { IsSlim = !original.IsSlim };
        _skins[index] = updated;
        AttemptApply(updated, revertSnapshot: original);
    }

    private IBrush GetThemeBrush(string key)
    {
        this.TryFindResource(key, out var value);
        return value as IBrush ?? Brushes.Transparent;
    }

    private void Refresh()
    {
        var accent = GetThemeBrush("AccentBrush");

        // "현재 스킨"(계정에서 불러온 id="default")은 무슨 스킨을 적용/선택하든 항상
        // 목록 1번째 자리에 고정한다 — 선택된 스킨을 앞으로 옮기던 이전 방식은 다른
        // 스킨을 적용하면 "현재 스킨" 카드가 뒤로 밀려버려서 요청에 따라 바꿨다.
        // 업로드 순서(_skins 원본 배열)는 그대로 두고 표시용 목록만 정렬하므로, 원본
        // 순서 자체는 깨지지 않는다. OrderByDescending은 안정 정렬이라 "default"를
        // 제외한 나머지는 기존 순서를 유지한다.
        var displayItems = _skins.OrderByDescending(s => s.Id == "default").Select(s => s with
        {
            IsSelected = s.Id == _selectedId,
            // 모든 카드가 같은 유리 재질을 쓰므로(2026-08-14, "전체 패널 모두 글래스디자인"
            // 요청) 선택 여부는 색이 아니라 테두리 두께로만 구분한다.
            CardBorderThickness = s.Id == _selectedId ? new Thickness(2) : new Thickness(1),
            HasThumbnail = s.Thumbnail is not null,
            ListThumbnail = GetOrBuildListThumbnail(s),
            CanDelete = s.Id != "default"
        }).ToList();

        SkinListItems.ItemsSource = displayItems;
        SkinListItems.IsEnabled = _pendingId is null;
        SlimArmToggleButton.IsEnabled = _pendingId is null;

        var selected = displayItems.First(s => s.Id == _selectedId);
        SkinPreviewName.Text = selected.Name;
        SlimArmToggleThumb.Margin = new Thickness(selected.IsSlim ? 18 : 3, 0, 0, 0);
        SlimArmToggleTrack.Background = selected.IsSlim ? accent : GetThemeBrush("ToggleTrackBrush");
        SlimArmToggleLabel.Text = selected.IsSlim ? "슬림" : "와이드";
        ApplyErrorPanel.IsVisible = _lastError is not null;
        ApplyPreview(selected);
    }

    private void ApplyPreview(SkinItem selected)
    {
        // "기본 (Steve)"처럼 실제 텍스처가 없는 자리표시자는 3D로 보여줄 게 없으니
        // WebView 검토 때와 마찬가지로 2D 폴백을 그대로 쓴다.
        if (_has3D && selected.Thumbnail is not null)
        {
            SkinPreviewFace.IsVisible = false;
            SkinViewer3D.IsVisible = true;
            SkinViewerInteractionOverlay.IsVisible = true;
            SkinViewer3D.SetSkin(selected.Thumbnail, selected.IsSlim);
            return;
        }

        if (_has3D)
        {
            SkinViewer3D.IsVisible = false;
            SkinViewerInteractionOverlay.IsVisible = false;
        }
        SkinPreviewFace.IsVisible = true;

        if (SkinPreviewImage.Source is RenderTargetBitmap previous) previous.Dispose();

        if (selected.Thumbnail is not null)
        {
            SkinPreviewImage.Source = BuildFrontViewBitmap(selected.Thumbnail, selected.IsSlim) ?? selected.Thumbnail;
            SkinPreviewImage.IsVisible = true;
            SkinPreviewPlaceholder.IsVisible = false;
        }
        else
        {
            SkinPreviewImage.IsVisible = false;
            SkinPreviewPlaceholder.IsVisible = true;
        }
    }

    // 목록 썸네일(34x34)도 원본 텍스처 시트를 그대로 UniformToFill하면 UV 언랩 조각들이
    // 뒤섞여 보인다 — 미리보기와 같은 정면 합성 이미지를 스킨 Id 기준으로 캐싱해서 쓴다.
    private Bitmap? GetOrBuildListThumbnail(SkinItem s)
    {
        if (s.Thumbnail is null) return null;

        if (_listThumbnailCache.TryGetValue(s.Id, out var cached)
            && ReferenceEquals(cached.Source, s.Thumbnail) && cached.IsSlim == s.IsSlim)
        {
            return cached.Composite ?? s.Thumbnail;
        }

        cached.Composite?.Dispose();
        var composite = BuildFrontViewBitmap(s.Thumbnail, s.IsSlim);
        _listThumbnailCache[s.Id] = (s.Thumbnail, s.IsSlim, composite);
        return composite ?? s.Thumbnail;
    }

    // 2D 폴백 미리보기(3D 뷰어 초기화 실패 시)용. 원본 스킨 텍스처는 얼굴/몸통/팔다리가
    // UV 언랩으로 조각조각 흩어진 시트라 그대로 보여주면 알아보기 어렵다 — 정면 파츠만
    // 오려내 3D 뷰어와 같은 정면 포즈로 이어붙인 "정면 전신" 이미지를 새로 합성한다.
    // 표준 64폭 스킨(64x32 레거시 / 64x64 신버전)이 아니면 합성 좌표를 신뢰할 수 없어
    // null을 돌려주고 호출부에서 원본 텍스처로 폴백한다.
    private static Bitmap? BuildFrontViewBitmap(Bitmap skin, bool isSlim)
    {
        if (skin.PixelSize.Width != 64) return null;

        var legacyFormat = skin.PixelSize.Height <= 32;
        var armWidth = isSlim ? 3 : 4;
        var canvasSize = new PixelSize(armWidth * 2 + 8, 32);

        var canvas = new Canvas { Width = canvasSize.Width, Height = canvasSize.Height };

        void AddPart(int destX, int destY, int w, int h, int srcX, int srcY, bool flip = false)
        {
            var crop = new CroppedBitmap(skin, new PixelRect(srcX, srcY, w, h));
            var image = new Image { Source = crop, Width = w, Height = h, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.None);
            if (flip) image.RenderTransform = new ScaleTransform(-1, 1);
            Canvas.SetLeft(image, destX);
            Canvas.SetTop(image, destY);
            canvas.Children.Add(image);
        }

        AddPart(armWidth, 0, 8, 8, 8, 8); // 머리 정면
        AddPart(armWidth, 8, 8, 12, 20, 20); // 몸통 정면
        AddPart(0, 8, armWidth, 12, 44, 20); // 오른팔 정면 (화면상 왼쪽)
        AddPart(armWidth, 20, 4, 12, 4, 20); // 오른다리 정면

        if (legacyFormat)
        {
            // 64x32 구버전 포맷은 왼팔/왼다리 전용 UV가 없어 오른쪽을 좌우 반전해 재사용
            // (SkinModelBuilder.BuildParts의 legacyFormat 분기와 동일한 규칙).
            AddPart(armWidth + 8, 8, armWidth, 12, 44, 20, flip: true);
            AddPart(armWidth + 4, 20, 4, 12, 4, 20, flip: true);
        }
        else
        {
            AddPart(armWidth + 8, 8, armWidth, 12, 36, 52); // 왼팔 정면
            AddPart(armWidth + 4, 20, 4, 12, 20, 52); // 왼다리 정면
        }

        // 겉 레이어(모자/재킷/소매/바지) — 각 부위 베이스 위에 같은 자리에 겹쳐 그린다.
        // UV는 SkinModelBuilder.BuildParts의 오버레이 박스와 동일한 좌표. 모자는 구버전
        // (64x32) 텍스처에도 존재하지만, 몸통/팔다리 겉레이어는 64x64 신버전에만 있다.
        AddPart(armWidth, 0, 8, 8, 40, 8); // 머리 모자
        if (!legacyFormat)
        {
            AddPart(armWidth, 8, 8, 12, 20, 36); // 몸통 재킷
            AddPart(0, 8, armWidth, 12, 44, 36); // 오른팔 소매
            AddPart(armWidth + 8, 8, armWidth, 12, 52, 52); // 왼팔 소매
            AddPart(armWidth, 20, 4, 12, 4, 36); // 오른다리 바지
            AddPart(armWidth + 4, 20, 4, 12, 4, 52); // 왼다리 바지
        }

        canvas.Measure(new Size(canvasSize.Width, canvasSize.Height));
        canvas.Arrange(new Rect(0, 0, canvasSize.Width, canvasSize.Height));

        var rtb = new RenderTargetBitmap(canvasSize, new Vector(96, 96));
        rtb.Render(canvas);
        return rtb;
    }
}

public record SkinItem(string Id, string Name, Bitmap? Thumbnail)
{
    public bool IsSelected { get; init; }
    public Thickness CardBorderThickness { get; init; } = new(1);
    public Bitmap? ListThumbnail { get; init; }
    public bool HasThumbnail { get; init; }
    public bool CanDelete { get; init; }
    public bool IsSlim { get; init; } // 업로드한 스킨은 모델 타입을 몰라 기본 false(클래식)
    public byte[]? Bytes { get; init; } // Mojang 재적용 및 디스크 저장용 원본 PNG
}

file record SkinManifestEntry(string Id, string Name, bool IsSlim, string FileName);
