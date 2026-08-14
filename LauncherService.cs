using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    private const string ManifestUrl = "https://raw.githubusercontent.com/thisisyousam/Grin-Launcher/main/manifest.json";

    // Azure App Registration의 Application (Client) ID. .env 파일(AZURE_CLIENT_ID)에서 로드.
    private static readonly string AzureClientId = LoadAzureClientId();

    private static string LoadAzureClientId()
    {
        var envPath = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

                var parts = trimmed.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim() == "AZURE_CLIENT_ID")
                    return parts[1].Trim();
            }
        }

        return Environment.GetEnvironmentVariable("AZURE_CLIENT_ID")
            ?? throw new InvalidOperationException(".env 파일에 AZURE_CLIENT_ID가 설정되어 있지 않습니다. .env.example을 참고하세요.");
    }

    public string MinecraftVersion => McVersion;
    public MinecraftPath Path { get; } = new("./test-minecraft");
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

    // 런처가 켜져 있는 동안 manifest.json이 갱신될 수 있으므로(관리자가 새 모드 릴리스 후
    // 재배포) 캐싱하지 않고 호출할 때마다 새로 받아온다.
    public async Task<ModManifest> GetManifestAsync()
    {
        using var httpClient = new HttpClient();
        var manifest = await httpClient.GetFromJsonAsync<ModManifest>(ManifestUrl);
        return manifest!;
    }

    public async Task DownloadModsAsync(ModManifest manifest)
    {
        var modsDir = System.IO.Path.Combine(Path.BasePath, "mods");
        Directory.CreateDirectory(modsDir);

        using var httpClient = new HttpClient();

        foreach (var mod in manifest.mods)
        {
            var destPath = System.IO.Path.Combine(modsDir, mod.name);
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
