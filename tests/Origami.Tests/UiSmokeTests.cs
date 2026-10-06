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

    /// <summary>
    /// 导出对话框与主窗口里的选项都要有<b>可见文字</b>。
    /// <para>
    /// 曾经把 RadioButton 做成卡片时，ControlTemplate 里的 ContentPresenter
    /// 忘了写 <c>Content="{TemplateBinding Content}"</c> —— ContentPresenter
    /// 不会默认继承 Content，于是文字全空、只剩一个彩色方块。
    /// XAML 编译器和「窗口能否加载」的冒烟测试都发现不了这种
    /// 「能加载但没内容」的缺陷，所以这里断言 Content 非空且控件有宽度。</para>
    /// </summary>
    [AvaloniaFact]
    public void 导出选项带有可见文字()
    {
        var vm = new MainViewModel();
        var dlg = new ExportDialog(vm.ViewTabs[0], ColorMode.Peaks, new Size(1200, 1200));
        dlg.Show();
        dlg.Measure(new Size(420, 400));
        dlg.Arrange(new Rect(0, 0, 420, 400));
        dlg.UpdateLayout();

        foreach (var (rb, expected) in new[]
                 {
                     (dlg.SvgRadio, "SVG 矢量"),
                     (dlg.PngRadio, "PNG 位图"),
                     (dlg.ColorRadio, "彩色"),
                     (dlg.PlainRadio, "黑白 / 剪影"),
                 })
        {
            Assert.Equal(expected, rb.Content);
            // 文字为空时控件宽度会塌成 0，有文字则必有宽度
            Assert.True(rb.Bounds.Width > 0,
                $"{expected} 没有可见宽度 —— ContentPresenter 可能没接上 Content");
        }

        dlg.Close();
    }

    /// <summary>主窗口画布上的五个视图选项都要有可见文字。</summary>
    [AvaloniaFact]
    public void 画布视图选项带有可见文字()
    {
        var win = new MainWindow();
        win.Show();
        win.Measure(new Size(1320, 760));
        win.Arrange(new Rect(0, 0, 1320, 760));
        win.UpdateLayout();

        var expected = new[] { "长条", "方形", "折痕图", "成品", "原子片" };
        Assert.Equal(expected.Length, win.ViewOptions.Count);

        for (int i = 0; i < win.ViewOptions.Count; i++)
        {
            var rb = win.ViewOptions[i];
            Assert.Equal(expected[i], rb.Content);
            Assert.True(rb.Bounds.Width > 0,
                $"「{expected[i]}」没有可见宽度 —— ContentPresenter 可能没接上 Content");
        }

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

    [AvaloniaFact]
    public void 帮助对话框能加载()
    {
        var dlg = new HelpDialog();
        dlg.Show();
        Assert.Equal("操作说明", dlg.Title);
        dlg.Close();
    }

    /// <summary>
    /// 导出对话框的格式与配色必须能来回切换。
    /// <para>
    /// 曾经的实现是「Border 画卡片+ 透明 RadioButton 叠放」，透明层
    /// <c>IsHitTestVisible=False</c> 导致<b>点已选中项也切不动</b>、两项不互斥。
    /// 根因是交互被转移到了 Border 上，RadioButton 本身不再是可点击的控件。</para>
    /// <para>
    /// 这里断言的是<b>结构前提</b>：四个选项都必须是启用且可见的 RadioButton，
    /// 由RadioButton 自己承担交互（配GroupName 做互斥）。
    /// <para>
    /// 互斥行为本身<b>无法在无头环境断言</b> —— 实测 Avalonia 无头平台的
    /// GroupName 分组完全不响应指针事件（连首次点击都不生效），
    /// 这是平台限制而非控件问题，需要真机交互验证。
    /// </para>
    /// </summary>
    [AvaloniaFact]
    public void 导出选项由RadioButton承载交互()
    {
        var vm = new MainViewModel();
        var dlg = new ExportDialog(vm.ViewTabs[0], ColorMode.Peaks, new Size(1200, 1200));
        dlg.Show();

        // 四个选项都必须可交互：被禁用/隐藏就退回到「点不动」的旧问题
        foreach (var r in new[] { dlg.SvgRadio, dlg.PngRadio, dlg.ColorRadio, dlg.PlainRadio })
        {
            Assert.True(r.IsEnabled, "选项被禁用了，用户无法点击");
            Assert.Same(r.Classes.Contains("disabled") ? null : r, r);   // 未标记为 disabled
        }

        // GroupName 是互斥的前提，两个组必须各自齐备
        Assert.Equal("fmt", dlg.SvgRadio.GroupName);
        Assert.Equal("fmt", dlg.PngRadio.GroupName);
        Assert.Equal("clr", dlg.ColorRadio.GroupName);
        Assert.Equal("clr", dlg.PlainRadio.GroupName);

        // 默认 PNG，且仅 PNG 时显示分辨率
        Assert.True(dlg.PngRadio.IsChecked);
        Assert.True(dlg.ScalePanel.IsVisible);

        dlg.Close();
    }

    /// <summary>切到 SVG 时分辨率区块应隐藏、说明文字应显示。</summary>
    [AvaloniaFact]
    public void 导出格式切换控制分辨率可见性()
    {
        var vm = new MainViewModel();
        var dlg = new ExportDialog(vm.ViewTabs[0], ColorMode.Peaks, new Size(1200, 1200));
        dlg.Show();

        dlg.SvgRadio.IsChecked = true;
        Assert.False(dlg.ScalePanel.IsVisible);
        Assert.True(dlg.VectorNote.IsVisible);

        dlg.Close();
    }

    /// <summary>配色两项同属一组。</summary>
    [AvaloniaFact]
    public void 导出配色同组互斥()
    {
        var vm = new MainViewModel();
        var dlg = new ExportDialog(vm.ViewTabs[0], ColorMode.Peaks, new Size(1200, 1200));
        dlg.Show();

        Assert.True(dlg.ColorRadio.IsChecked);
        Assert.Equal(dlg.ColorRadio.GroupName, dlg.PlainRadio.GroupName);
        Assert.NotEqual(dlg.SvgRadio.GroupName, dlg.ColorRadio.GroupName);   // 两组互不影响

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

        // 2 形状 × 3 内容 = 6 种组合，逐一切过去都必须能产出图
        foreach (var shape in new[] { ShapeKind.Strip, ShapeKind.Square })
        {
            foreach (var content in new[] { ContentKind.Crease, ContentKind.Folded, ContentKind.Pieces })
            {
                vm.Shape = shape;
                vm.Content = content;
                await Settle(vm);
                Assert.False(string.IsNullOrEmpty(vm.SvgSource), $"{shape}/{content} 没有产出 SVG");
            }
        }
    }

    /// <summary>形状维度只切形状、内容维度只切内容，当前视图应始终与两者一致。</summary>
    [AvaloniaFact]
    public async Task 维度组合解析出正确视图()
    {
        var vm = new MainViewModel();
        await vm.RebuildAsync();

        vm.Shape = ShapeKind.Square;
        vm.Content = ContentKind.Folded;
        await Settle(vm);

        Assert.Equal(ShapeKind.Square, vm.SelectedTab!.Shape);
        Assert.Equal(ContentKind.Folded, vm.SelectedTab.Content);
        Assert.Equal(ViewKind.SquareFolded, vm.SelectedTab.Kind);
    }

    /// <summary>六种组合都能被解析到，且互不重复。</summary>
    [Fact]
    public void 六种组合无重复()
    {
        var all = new List<ViewKind>();
        foreach (var s in new[] { ShapeKind.Strip, ShapeKind.Square })
            foreach (var c in new[] { ContentKind.Crease, ContentKind.Folded, ContentKind.Pieces })
                all.Add(Views.Resolve(s, c).Kind);

        Assert.Equal(6, all.Distinct().Count());
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

        // 原子片不支持上色
        vm.Content = ContentKind.Pieces;
        await Settle(vm);
        Assert.False(vm.CanColorize);

        vm.Content = ContentKind.Crease;
        await Settle(vm);
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
        vm.IsColorOn = true;
        await Settle(vm);

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
        await Settle(vm);

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