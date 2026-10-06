using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Origami.Desktop;

/// <summary>
/// 加载程序图标。
/// <para>
/// 两个来源，各司其职：
/// <list type="bullet">
/// <item><b>Win32 资源</b>（csproj 的 <c>ApplicationIcon</c>）—— 任务栏、Alt-Tab、
/// 资源管理器里的文件图标。走的是操作系统的图标机制，不需要 Avalonia 参与。</item>
/// <item><b>Avalonia 资源</b>（本类）—— 窗口标题栏、对话框的标题图标。
/// 这些要自己给：Avalonia 不读 Win32 资源。</item>
/// </list>
/// </para>
/// </summary>
public static class AppIcon
{
    const string ResourceUri = "avares://origami-ui/Assets/icon.png";

    static WindowIcon? _windowIcon;

    /// <summary>窗口图标。首次访问时从嵌入资源解码。</summary>
    public static WindowIcon Window
        => _windowIcon ??= LoadWindowIcon();

    /// <summary>把图标设到窗口上（<see cref="Window.Icon"/> 只接受 WindowIcon）。</summary>
    public static void Apply(Window window) => window.Icon = Window;

    static WindowIcon LoadWindowIcon()
    {
        // icon.svg 是矢量源文件，Avalonia 无内置 SVG 解码器，
        // 故这里用打包时生成的 256px PNG（已在 csproj 里作为 AvaloniaResource 嵌入）。
        try
        {
            using var s = AssetLoader.Open(new Uri(ResourceUri));
            return new WindowIcon(s);
        }
        catch (Exception)
        {
            // 图标缺失不该让程序起不来：标题栏没图标仍可用
            return new WindowIcon(new MemoryStream());
        }
    }
}