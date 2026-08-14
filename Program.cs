using Avalonia;

namespace GrinLauncher;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 콘솔에서 실행할 때 원인 불명 크래시/무한대기를 진단하기 쉽게 남겨둔다.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.WriteLine("[diag] UnhandledException: " + e.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.WriteLine("[diag] UnobservedTaskException: " + e.Exception);
            e.SetObserved();
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // RenderingMode에 OpenGl을 명시하지 않으면 macOS(Avalonia.Native)는 기본값인
    // Metal로만 붙어서 OpenGlControlBase가 컨텍스트를 아예 못 받는다(조용히 실패,
    // 예외도 안 던짐) — 스킨 3D 뷰어가 여기 의존하므로 필수. Software는 최후 폴백.
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .With(new AvaloniaNativePlatformOptions
        {
            RenderingMode = new[] { AvaloniaNativeRenderingMode.OpenGl, AvaloniaNativeRenderingMode.Software }
        })
        .LogToTrace();
}
