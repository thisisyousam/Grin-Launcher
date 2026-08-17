using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using System.Threading;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;
using XboxAuthNet.Game.XboxAuth;

namespace GrinLauncher;

public class LauncherService
{
    private const string McVersion = "26.2";

    // 모드 jar는 이 태그의 릴리즈 자산으로 올린다. manifest.json을 손으로 맞춰줄
    // 필요 없이, 이 릴리즈에 뭐가 올라와 있는지를 GitHub API로 그대로 조회해서 쓴다.
    private const string ModsReleaseTag = "v1.0.0(beta)";

    // Azure App Registration의 Application (Client) ID. .env/환경 변수(AZURE_CLIENT_ID)로
    // 덮어쓸 수 있지만, 기본값은 배포용 exe/dmg에도 그대로 들어간다. Public client OAuth
    // 앱의 Client ID는 시크릿이 아니라 공개해도 안전하다 (클라이언트 시크릿 없이 동작하는
    // 게 이 방식의 핵심 — 대부분의 오픈소스 마인크래프트 런처도 소스에 그대로 박아둔다).
    private const string DefaultAzureClientId = "b0f0d4ed-d71c-44b0-acf7-d108d7d69df4";

    private static readonly string AzureClientId = LoadAzureClientId();

    private static string LoadAzureClientId()
    {
        // dotnet run은 CWD가 프로젝트 루트라 .env를 바로 찾지만, macOS .app 번들로
        // 실행하면 CWD가 프로젝트 루트가 아니게 된다. 실행 파일 옆(AppContext.BaseDirectory,
        // 번들이면 Contents/MacOS)도 같이 뒤진다.
        var candidatePaths = new[]
        {
            System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".env"),
            System.IO.Path.Combine(AppContext.BaseDirectory, ".env"),
        };

        foreach (var envPath in candidatePaths)
        {
            if (!File.Exists(envPath)) continue;

            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim() == "AZURE_CLIENT_ID")
                    return parts[1].Trim();
            }
        }

        return Environment.GetEnvironmentVariable("AZURE_CLIENT_ID") ?? DefaultAzureClientId;
    }

    public string MinecraftVersion => McVersion;
    public MinecraftPath Path { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GrinLauncher", "minecraft"));
    public MinecraftLauncher Launcher { get; }

    public event Action<string>? LogMessage;
    public event EventHandler<CmlLib.Core.Installers.InstallerProgressChangedEventArgs>? FileProgressChanged;
    public event EventHandler<CmlLib.Core.ByteProgress>? ByteProgressChanged;

    private IPublicClientApplication? _msalApp;

    public LauncherService()
    {
        Launcher = new MinecraftLauncher(Path);
        Launcher.FileProgressChanged += (sender, args) => FileProgressChanged?.Invoke(sender, args);
        Launcher.ByteProgressChanged += (sender, args) => ByteProgressChanged?.Invoke(sender, args);
    }

    private void Log(string message) => LogMessage?.Invoke(message);

    // JEAuthException.Message는 서버 JSON의 "error" 필드만 담고 어느 단계(Xbox Live
    // XASU/XSTS vs Minecraft login_with_xbox vs profile 조회)에서 실패했는지 알려주지
    // 않는다. 모든 요청/실패 응답을 그대로 찍어서 정확한 URL과 응답 본문을 확인한다.
    private sealed class LoggingHttpHandler : DelegatingHandler
    {
        private readonly Action<string> _log;
        public LoggingHttpHandler(Action<string> log) : base(new HttpClientHandler()) => _log = log;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var line = $"[HTTP] {request.Method} {request.RequestUri} -> {(int)response.StatusCode} {response.ReasonPhrase}\n{body}";
                // 로그인 화면은 HomePage(로그 박스)가 아직 안 보이는 상태이므로 콘솔에도 남긴다.
                Console.WriteLine(line);
                _log(line);
            }
            return response;
        }
    }

    public async Task<MSession> GetSessionAsync()
    {
        _msalApp ??= await MsalClientHelper.BuildApplicationWithCache(AzureClientId);

        // 기본 Basic() Xbox 인증은 미성년/가족 세이프티 등으로 나이 확인이 걸린 계정에서
        // "unauthorized" 401만 던지고 실패한다. 문서(auth.microsoft, 나이 관련 섹션)가
        // 권장하는 대로 Full()을 사용해 디바이스/타이틀 토큰까지 포함시킨다.
        var loginHandler = new JELoginHandlerBuilder()
            .WithHttpClient(new HttpClient(new LoggingHttpHandler(Log)))
            .WithOAuthProvider(new MsalCodeFlowProvider(_msalApp))
            .WithXboxAuthProvider(new FullXboxProvider(JELoginHandler.RelyingParty))
            .Build();

        try
        {
            var session = await loginHandler.Authenticate();
            Log("Microsoft 로그인 성공!");
            return session;
        }
        catch (JEAuthException ex)
        {
            Log($"Microsoft 로그인 실패 - StatusCode: {ex.StatusCode}, Error: {ex.Error}, ErrorType: {ex.ErrorType}, ErrorMessage: {ex.ErrorMessage}");
            throw;
        }
    }

    // MojangAPI 1.2.1의 Mojang.GetProfileUsingUUID는 textures.SKIN.metadata를 무조건
    // GetProperty로 읽는다 — 클래식(Steve) 스킨은 metadata 자체가 없어서(슬림일 때만
    // 존재) 거기서 예외가 나고, 그 예외의 catch 블록이 방금 읽어둔 URL까지 통째로
    // null로 덮어써 버린다. 그래서 라이브러리를 거치지 않고 세션서버 응답을 직접
    // 받아서 파싱한다 — metadata는 TryGetProperty로 있으면만 읽는다.
    public async Task<(string? Url, bool IsSlim)> GetSkinUrlAsync(string uuid)
    {
        using var http = new HttpClient();
        var response = await http.GetAsync($"https://sessionserver.mojang.com/session/minecraft/profile/{uuid}");
        if (!response.IsSuccessStatusCode) return (null, false);

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        if (!doc.RootElement.TryGetProperty("properties", out var properties) || properties.GetArrayLength() == 0)
            return (null, false);

        var texturesBase64 = properties[0].GetProperty("value").GetString();
        if (texturesBase64 is null) return (null, false);

        using var texturesDoc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(texturesBase64));
        if (!texturesDoc.RootElement.GetProperty("textures").TryGetProperty("SKIN", out var skin))
            return (null, false);

        var url = skin.GetProperty("url").GetString();
        var isSlim = skin.TryGetProperty("metadata", out var metadata)
            && metadata.TryGetProperty("model", out var model)
            && model.GetString() == "slim";

        return (url, isSlim);
    }

    // Minecraft Services API로 실제 계정 스킨을 교체한다. accessToken은 GetSessionAsync()로
    // 받은 MSession.AccessToken(게임 실행에도 쓰는 그 토큰)을 그대로 쓴다.
    public async Task ChangeSkinAsync(string accessToken, byte[] pngBytes, bool isSlim)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(isSlim ? "slim" : "classic"), "variant");

        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "file", "skin.png");

        var response = await http.PostAsync("https://api.minecraftservices.com/minecraft/profile/skins", content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"스킨 적용 실패 ({(int)response.StatusCode}): {body}");
        }

        // Log("스킨 적용 완료");
    }

    public async Task<string> InstallFabricAsync()
    {
        var fabricInstaller = new FabricInstaller(new HttpClient());
        var fabricVersionName = await fabricInstaller.Install(McVersion, Path);
        Log($"Fabric 설치 완료: {fabricVersionName}");
        return fabricVersionName;
    }

    // 모드 릴리스에 새 자산이 올라와도 manifest.json을 따로 손대지 않아도 되도록,
    // GitHub 릴리즈 자산 목록을 조회해서 그 자체를 모드 목록으로 쓴다.
    //
    // 다만 api.github.com은 미인증 요청을 IP당 시간당 60회로 제한한다. 친구 여럿이
    // 비슷한 시간에 실행/재실행하면(특히 설치 초반 시행착오) 금방 소진되어 429로
    // 막힐 수 있다(실제로 겪은 문제). ETag 조건부 요청(바뀐 게 없으면 304, 할당량
    // 안 깎임)으로 평상시 소모를 줄이고, 그래도 막히면(429/네트워크 오류) 마지막으로
    // 성공했던 목록을 그대로 써서 앱이 완전히 멈추지 않게 한다.
    public async Task<ModManifest> GetManifestAsync()
    {
        var cachePath = System.IO.Path.Combine(Path.BasePath, "mods-manifest-cache.json");
        var cached = await ReadManifestCacheAsync(cachePath);

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GrinLauncher");
        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var releaseUrl = $"https://api.github.com/repos/thisisyousam/Grin-Launcher/releases/tags/{Uri.EscapeDataString(ModsReleaseTag)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, releaseUrl);
        if (!string.IsNullOrEmpty(cached?.ETag))
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cached.ETag));

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(request);
        }
        catch (Exception ex) when (cached is not null)
        {
            Log($"GitHub 릴리즈 조회 실패, 마지막으로 확인된 모드 목록 사용: {ex.Message}");
            return cached.Manifest;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotModified && cached is not null)
            return cached.Manifest;

        if (!response.IsSuccessStatusCode)
        {
            if (cached is not null)
            {
                Log($"GitHub 릴리즈 조회 실패({(int)response.StatusCode}), 마지막으로 확인된 모드 목록 사용");
                return cached.Manifest;
            }

            if ((int)response.StatusCode is 403 or 429 && response.Headers.TryGetValues("X-RateLimit-Reset", out var resetValues)
                && long.TryParse(resetValues.FirstOrDefault(), out var resetEpoch))
            {
                var waitMinutes = Math.Max(1, (int)Math.Ceiling((DateTimeOffset.FromUnixTimeSeconds(resetEpoch) - DateTimeOffset.UtcNow).TotalMinutes));
                throw new HttpRequestException($"GitHub API 요청 제한에 걸렸습니다. 약 {waitMinutes}분 후 다시 시도해주세요.");
            }

            response.EnsureSuccessStatusCode();
        }

        var release = await response.Content.ReadFromJsonAsync<GithubRelease>();
        var mods = (release?.assets ?? [])
            .Where(asset => asset.name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            .Select(asset => new ModEntry
            {
                name = asset.name,
                downloadUrl = asset.browser_download_url,
                version = ExtractVersion(asset.name)
            })
            .ToList();

        var manifest = new ModManifest { mcVersion = McVersion, mods = mods };

        var etag = response.Headers.ETag?.Tag;
        if (!string.IsNullOrEmpty(etag))
            await WriteManifestCacheAsync(cachePath, etag, manifest);

        return manifest;
    }

    private static async Task<ManifestCache?> ReadManifestCacheAsync(string cachePath)
    {
        if (!File.Exists(cachePath)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ManifestCache>(await File.ReadAllTextAsync(cachePath));
        }
        catch
        {
            return null; // 캐시 파일이 깨져 있으면 그냥 새로 받아온다
        }
    }

    private static async Task WriteManifestCacheAsync(string cachePath, string etag, ModManifest manifest)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
        var json = System.Text.Json.JsonSerializer.Serialize(new ManifestCache { ETag = etag, Manifest = manifest });
        await File.WriteAllTextAsync(cachePath, json);
    }

    private sealed class ManifestCache
    {
        public string ETag { get; set; } = "";
        public ModManifest Manifest { get; set; } = new();
    }

    // 파일명 끝의 "-1.2.3.jar" 패턴에서 버전만 뽑아 모드 목록에 표시한다(예:
    // fabric-api-0.155.2+26.2.jar -> "0.155.2+26.2"). 패턴에 안 맞으면 파일명 그대로 보여준다.
    private static string ExtractVersion(string fileName)
    {
        var match = Regex.Match(fileName, @"-(\d[\d.+]*)\.jar$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : fileName;
    }

    private sealed class GithubRelease
    {
        public List<GithubAsset> assets { get; set; } = [];
    }

    private sealed class GithubAsset
    {
        public string name { get; set; } = "";
        public string browser_download_url { get; set; } = "";
    }

    // 모드 파일명에 버전이 박혀 있으므로(예: collection-mod-1.0.2.jar) 매니페스트의
    // 모든 이름이 이미 mods 폴더에 있으면 최신 상태로 본다. 하나라도 없으면(신규 모드
    // 추가 또는 기존 모드 버전업으로 파일명이 바뀐 경우) 업데이트가 필요하다.
    public bool NeedsModUpdate(ModManifest manifest)
    {
        var modsDir = System.IO.Path.Combine(Path.BasePath, "mods");
        return manifest.mods.Any(mod => !File.Exists(System.IO.Path.Combine(modsDir, mod.name)));
    }

    public async Task DownloadModsAsync(ModManifest manifest)
    {
        var modsDir = System.IO.Path.Combine(Path.BasePath, "mods");
        Directory.CreateDirectory(modsDir);

        // 버전업으로 파일명이 바뀐 예전 jar가 남아있으면 Fabric이 신구 버전을 동시에
        // 로드해버리므로, 매니페스트에 없는 기존 jar는 새로 받기 전에 지운다.
        var keepNames = manifest.mods.Select(mod => mod.name).ToHashSet();
        foreach (var existingJar in Directory.EnumerateFiles(modsDir, "*.jar"))
        {
            if (!keepNames.Contains(System.IO.Path.GetFileName(existingJar)))
            {
                Log($"이전 버전 삭제: {System.IO.Path.GetFileName(existingJar)}");
                File.Delete(existingJar);
            }
        }

        using var httpClient = new HttpClient();

        foreach (var mod in manifest.mods)
        {
            var destPath = System.IO.Path.Combine(modsDir, mod.name);
            if (File.Exists(destPath)) continue;

            Log($"다운로드 중: {mod.name}");

            var bytes = await httpClient.GetByteArrayAsync(mod.downloadUrl);
            await File.WriteAllBytesAsync(destPath, bytes);

            Log($"완료: {mod.name}");
        }
    }

    public async Task<Process> LaunchGameAsync(string fabricVersionName, MSession session, int maxRamMb = 4096)
    {
        var launchOption = new MLaunchOption
        {
            Session = session,
            MaximumRamMb = maxRamMb
        };

        await Launcher.InstallAsync(fabricVersionName);
        var process = await Launcher.BuildProcessAsync(fabricVersionName, launchOption);
        process.EnableRaisingEvents = true;
        process.Start();
        Log("게임 실행됨!");
        return process;
    }
}

public class ModManifest
{
    public string mcVersion { get; set; } = "";
    public List<ModEntry> mods { get; set; } = new();
}

public class ModEntry
{
    public string name { get; set; } = "";
    public string downloadUrl { get; set; } = "";
    public string version { get; set; } = "";
}
