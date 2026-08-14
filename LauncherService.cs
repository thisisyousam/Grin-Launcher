using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using Microsoft.Identity.Client;
using XboxAuthNet.Game.Msal;
using XboxAuthNet.Game.Msal.OAuth;

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

    private ModManifest? _cachedManifest;
    private IPublicClientApplication? _msalApp;

    public LauncherService()
    {
        Launcher = new MinecraftLauncher(Path);
        Launcher.FileProgressChanged += (sender, args) => FileProgressChanged?.Invoke(sender, args);
        Launcher.ByteProgressChanged += (sender, args) => ByteProgressChanged?.Invoke(sender, args);
    }

    private void Log(string message) => LogMessage?.Invoke(message);

    public async Task<MSession> GetSessionAsync()
    {
        _msalApp ??= await MsalClientHelper.BuildApplicationWithCache(AzureClientId);

        var loginHandler = new JELoginHandlerBuilder()
            .WithOAuthProvider(new MsalCodeFlowProvider(_msalApp))
            .Build();

        var session = await loginHandler.Authenticate();
        Log("Microsoft 로그인 성공!");
        return session;
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

    public async Task<ModManifest> GetManifestAsync()
    {
        if (_cachedManifest is not null)
            return _cachedManifest;

        using var httpClient = new HttpClient();
        var manifest = await httpClient.GetFromJsonAsync<ModManifest>(ManifestUrl);
        _cachedManifest = manifest!;
        return _cachedManifest;
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
