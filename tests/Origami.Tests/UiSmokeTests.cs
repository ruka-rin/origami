using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Origami.Desktop;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Origami.Tests.UiSmokeTests))]

namespace Origami.Tests;

/// <summary>
/// XAML 冒烟测试：在<b>无显示环境</b>里真正把窗口实例化并Show 一次。
/// <para>
/// 动机：XAML 里的错误（<c>StaticResource</c> 键不存在、控件名对不上、
/// 样式选择器写错）<b>编译器一律查不出来</b> —— 它们只在运行期解析资源时
/// 才炸。改一次界面如果只能靠「手动启动看看」来验证，回归几乎必然发生。
/// 这里把「窗口能加载、能渲染」变成一条可回归的断言。
/// </para>
/// </summary>
public class UiSmokeTests
{
    /// <summary>
    /// 无头会话用的应用构建器。测试程序集拿不到 Desktop 的 <c>Program</c>（internal），
    /// 因此这里就地搭一个 —— 配置 <see cref="App"/> 即可复用同一套样式与资源。
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = true,
            });

    [AvaloniaFact]
    public void 主窗口能加载并渲染()
    {
        var win = new MainWindow();
        win.Show();
        win.Measure(new Size(1320, 760));
        win.Arrange(new Rect(0, 0, 1320, 760));

        Assert.NotNull(win.DataContext);
        Assert.IsType<MainViewModel>(win.DataContext);
        win.Close();
    }

    [AvaloniaFact]
    public void 导出对话框能加载()
    {
        var vm = new MainViewModel();
        var tab = vm.ViewTabs[0];

        var dlg = new ExportDialog(tab, ColorMode.Peaks, new Size(1200, 1200));
        dlg.Show();

        Assert.Equal("导出", dlg.Title);
        dlg.Close();
    }
}

/// <summary>
/// ViewModel 交互冒烟测试：不碰控件，只验证状态机的正确性。
/// 这些是纯逻辑，跑得快、无需headless，放在这里与UI 测试形成互补。
/// </summary>
public class ViewModelTests
{
    /// <summary>
    /// 等后台渲染跑完。
    /// <para>方向/上色变更都走 <c>_ = RerenderCurrentAsync()</c>（故意不阻塞 UI），
    /// 而 <c>CanResetXform</c> 之类的状态又依赖 <c>Busy</c>，所以断言前必须等它落地，
    /// 否则测到的是「渲染中」的中间态而非最终态。</para>
    /// </summary>
    static async Task Settle(MainViewModel vm)
    {
        for (int i = 0; i < 400 && vm.Busy; i++) await Task.Delay(10);
        Assert.False(vm.Busy, "渲染未在预期时间内结束");
    }

    [AvaloniaFact]
    public async Task 所有视图都能构建()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        foreach (var tab in vm.ViewTabs)
        {
            vm.SelectedTab = tab;
            await vm.SelectViewAsync(tab);
            Assert.False(string.IsNullOrEmpty(vm.SvgSource), $"{tab.Title} 没有产出 SVG");
        }
    }

    /// <summary>旋转后「恢复原始方向」必须可用，复位后必须禁用。</summary>
    [AvaloniaFact]
    public async Task 恢复方向按钮状态随方向变化()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        Assert.True(vm.IsXformIdentity);
        Assert.False(vm.CanResetXform);

        vm.RotateCw();
        await Settle(vm);

        Assert.False(vm.IsXformIdentity);
        Assert.True(vm.CanResetXform);      // 旋转后应可恢复

        vm.ResetXform();
        await Settle(vm);

        Assert.True(vm.IsXformIdentity);
        Assert.False(vm.CanResetXform);
    }

    /// <summary>镜像同样计入「非原始方向」。</summary>
    [AvaloniaFact]
    public async Task 镜像也算非原始方向()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        vm.ToggleMirrorX();
        await Settle(vm);
        Assert.True(vm.CanResetXform);

        vm.ToggleMirrorX();
        await Settle(vm);
        Assert.False(vm.CanResetXform);
    }

    /// <summary>原子片分解不支持上色，开关应禁用。</summary>
    [AvaloniaFact]
    public async Task 原子片视图禁用上色()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        var pieces = vm.ViewTabs.First(t => !t.SupportsColor);
        vm.SelectedTab = pieces;
        await vm.SelectViewAsync(pieces);
        Assert.False(vm.CanColorize);

        var crease = vm.ViewTabs.First(t => t.SupportsColor);
        vm.SelectedTab = crease;
        await vm.SelectViewAsync(crease);
        Assert.True(vm.CanColorize);
    }

    /// <summary>清空母版应回到空状态而不是保留上一次的图。</summary>
    [AvaloniaFact]
    public async Task 清空母版回到空状态()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        vm.MasterText = "";
        await vm.RebuildAsync();

        Assert.False(vm.HasSvg);
        Assert.Null(vm.Document);
        Assert.Equal("", vm.Summary);
    }

    /// <summary>非法母版不应抛到调用方，而应落到状态栏。</summary>
    [AvaloniaFact]
    public async Task 非法母版只报错不崩溃()
    {
        var vm = new MainViewModel();
        vm.MasterText = "/x/";          // 未知字符
        await vm.RebuildAsync();

        Assert.False(vm.HasSvg);
        Assert.Contains("出错", vm.Status);
    }

    /// <summary>导出 SVG 时，对话框里选的配色必须生效（不能被缓存覆盖）。</summary>
    [AvaloniaFact]
    public async Task 导出SVG遵循所选配色()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        var tab = vm.ViewTabs.First(t => t.Kind == ViewKind.Strip);
        vm.SelectedTab = tab;
        vm.IsColorOn = true;
        await vm.SelectViewAsync(tab);

        // 画布是彩色，但导出选黑白 —— 结果必须是黑白
        var (plainBytes, ext) = vm.Export(tab, new ExportOptions
        {
            Format = ExportFormat.Svg,
            Color = ColorMode.Plain,
        });

        Assert.Equal("svg", ext);
        string plainSvg = System.Text.Encoding.UTF8.GetString(plainBytes);

        var (colorBytes, _) = vm.Export(tab, new ExportOptions
        {
            Format = ExportFormat.Svg,
            Color = ColorMode.Peaks,
        });
        string colorSvg = System.Text.Encoding.UTF8.GetString(colorBytes);

        Assert.NotEqual(plainSvg, colorSvg);
        // 黑白线稿不应出现峰谷配色
        Assert.DoesNotContain("#f00", plainSvg);
        Assert.Contains("#f00", colorSvg);
    }

    /// <summary>导出 PNG 必须遵循所选倍率。</summary>
    [AvaloniaFact]
    public async Task 导出PNG遵循所选倍率()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        var tab = vm.ViewTabs.First(t => t.Kind == ViewKind.Strip);
        vm.SelectedTab = tab;
        await vm.SelectViewAsync(tab);

        var (small, _) = vm.Export(tab, new ExportOptions
        {
            Format = ExportFormat.Png, Color = ColorMode.Peaks, PngScale = 1.0,
        });
        var (large, _) = vm.Export(tab, new ExportOptions
        {
            Format = ExportFormat.Png, Color = ColorMode.Peaks, PngScale = 4.0,
        });

        Assert.True(large.Length > small.Length,
            $"4× 应比 1× 大：{large.Length} vs {small.Length}");
    }

    /// <summary>文本生成母版：结果写进母版框并触发渲染。</summary>
    [AvaloniaFact]
    public async Task 文本生成母版写入母版框()
    {
        var vm = new MainViewModel();
        vm.TextInput = "KRENIO";

        await vm.GenerateFromTextAsync();

        Assert.False(string.IsNullOrEmpty(vm.MasterText));
        Assert.True(vm.HasSvg);
    }

    /// <summary>不支持的字符应给出明确提示，而不是静默产出空母版。</summary>
    [AvaloniaFact]
    public async Task 无可支持字符时报错()
    {
        var vm = new MainViewModel();
        string before = vm.MasterText;
        vm.TextInput = "中文";

        await vm.GenerateFromTextAsync();

        // 母版应保持原值（DEBUG 构建下预填了样例，故不能假设为空）
        Assert.Equal(before, vm.MasterText);
        Assert.Contains("字库", vm.Status);
    }
}