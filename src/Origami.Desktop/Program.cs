using Avalonia;

namespace Origami.Desktop;

/// <summary>程序入口。<see cref="App"/> 是 <c>AvaloniaApplication</c> 子类，
/// 因此入口点签名不能用 C# 顶层的 <c>static int Main</c>（框架元数据里没有 modreq）。</summary>
internal static class Program
{
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}