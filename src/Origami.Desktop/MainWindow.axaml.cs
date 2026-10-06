using Avalonia;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Origami.Core;

namespace Origami.Desktop;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        // 不要自己写 InitializeComponent() —— XAML 编译器生成的同名方法还负责
        // 把 x:Name 声明的控件字段赋值上，手写一个同名方法会覆盖掉它
        InitializeComponent();
        DataContext = _vm;

        Viewer.ViewChanged += (_, _) => UpdateZoomLabel();

        // 导入完成后要按目标视图的取景方式复位预览
        _vm.ViewerFit = tab => { if (tab is not null) Viewer.DefaultFit = EffectiveFit(tab); };

        // 打开「文本生成母版」时，文本框直接获得焦点，省掉一次点击
        _vm.TextBoxRequested = () => TextBox.Focus();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        // 调试构建预填了样例母版，直接渲染一次
        if (!string.IsNullOrWhiteSpace(_vm.MasterText))
            _ = _vm.RebuildAsync();
    }

    // ------------------------------------------------------------ 自动渲染

    /// <summary>母版框回车即渲染。母版串里没有换行，所以回车不会有别的含义。</summary>
    async void OnMasterKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await RebuildAsync();
    }

    /// <summary>母版框失去焦点即渲染。</summary>
    async void OnMasterLostFocus(object? sender, RoutedEventArgs e) => await RebuildAsync();

    /// <summary>文本框回车即生成母版并渲染。</summary>
    async void OnTextKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await _vm.GenerateFromTextAsync();
    }

    /// <summary>文本框失去焦点即生成母版并渲染。</summary>
    async void OnTextLostFocus(object? sender, RoutedEventArgs e)
        => await _vm.GenerateFromTextAsync();

    async Task RebuildAsync()
    {
        // 先设取景模式再触发：Source 一变，Viewer 会按当前 DefaultFit 复位
        if (_vm.SelectedTab is { } t) Viewer.DefaultFit = EffectiveFit(t);
        await _vm.RebuildAsync();
    }

    /// <summary>
    /// 点击母版输入框<b>以外</b>的任何地方都让它失焦，从而触发自动渲染。
    /// <para>
    /// Avalonia 的 TextBox 不会因为点击别处自动失焦 —— 焦点只在「可聚焦控件之间移动」时转移，
    /// 画布、说明文字这些非可聚焦元素既拿不到焦点也不会让出焦点。
    /// 判据用「按下的位置是否落在输入框内」而不是无条件清除：后者会连按钮、列表的
    /// 键盘焦点一起干掉，影响 Tab 导航。
    /// </para>
    /// </summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // 两个输入框：母版框常驻，文本框只在开关打开时存在，谁在显示就保护谁
        var inputs = _vm.IsTextMode
            ? new[] { MasterBox, TextBox }
            : new[] { MasterBox };

        foreach (var input in inputs)
        {
            // 点在输入框内部（含其内边距）就什么都不做
            if (e.Source is Visual v && IsInside(v, input)) return;
            if (input.IsPointerOver) return;
        }

        // 让输入框交出焦点 -> LostFocus 处理器会去自动渲染
        if (TopLevel.GetTopLevel(this)?.FocusManager is { } fm)
        {
            var focused = fm.GetFocusedElement();
            if (inputs.Any(b => ReferenceEquals(focused, b)))
                fm.ClearFocus();
        }
    }

    /// <summary>判断控件是否位于指定容器内部。</summary>
    static bool IsInside(Visual v, Visual container)
    {
        for (Visual? p = v; p is not null; p = p.Parent as Visual)
            if (ReferenceEquals(p, container)) return true;
        return false;
    }

    // ------------------------------------------------------------ 视图切换

    /// <summary>
    /// 结合<b>当前方向</b>算出实际取景方式。
    /// <para>
    /// 长条折痕图声明的是「铺满宽度」（100 格 × 1 格，横向铺满才看得清折痕端点），
    /// 但转过 90° 之后它就变成 1 格 × 100 格的<b>竖条</b>了 —— 此时再铺满宽度，
    /// 整张图会被压成宽度接近 0 的一条线。所以奇数次旋转时退回「适应窗口」。</para>
    /// </summary>
    FitMode EffectiveFit(ViewTab tab)
    {
        if (tab.Fit == FitMode.Width && _vm.Xform.QuarterTurns % 2 == 1)
            return FitMode.Window;
        return tab.Fit;
    }

    async void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0) return;
        if (e.AddedItems[0] is not ViewTab tab) return;

        // 顺序很重要：先定取景模式，再让 VM 换图（换图会触发复位）
        Viewer.DefaultFit = EffectiveFit(tab);
        await _vm.SelectViewAsync(tab);
    }

    // ------------------------------------------------------------ 导入

    /// <summary>
    /// 导入外部文件。<b>不做二次确认对话框</b> —— 支持哪些格式由内容自动判别，
    /// 多弹一次窗口只会让人多点一下。导入结果直接落到母版框并重渲。
    /// </summary>
    async void OnImport(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入母版或折痕图",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("全部支持的文件")
                {
                    Patterns = new[] { "*.txt", "*.svg" },
                },
                new FilePickerFileType("母版文本") { Patterns = new[] { "*.txt" } },
                new FilePickerFileType("折痕图 SVG") { Patterns = new[] { "*.svg" } },
            },
        });

        if (files.Count == 0) return;
        var file = files[0];

        try
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            string text = await reader.ReadToEndAsync();
            await _vm.ImportAsync(text, file.Name);
        }
        catch (Exception ex) { _vm.Status = "导入失败：" + ex.Message; }
    }

    // ------------------------------------------------------------ 导出

    async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_vm.Document is null || _vm.SelectedTab is not { } tab)
        {
            _vm.Status = "没有可导出的内容";
            return;
        }

        // 先问格式 / 配色 / 分辨率，再问存到哪 —— 参数多，先在一个对话框里定下来
        var box = Viewer.DocumentBox;
        var dialog = new ExportDialog(tab, _vm.Color, box);
        var opt = await dialog.ShowDialog<ExportOptions?>(this);
        if (opt is null) return;      // 用户取消

        _vm.Status = "正在导出…";
        try
        {
            var (data, ext) = _vm.Export(tab, opt);

            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出",
                SuggestedFileName = opt.FileName(tab),
                DefaultExtension = ext,
                FileTypeChoices = ext == "svg"
                    ? new[] { new FilePickerFileType("SVG 矢量图") { Patterns = new[] { "*.svg" } } }
                    : new[] { new FilePickerFileType("PNG 位图") { Patterns = new[] { "*.png" } } },
            });
            if (file is null) { _vm.Status = "已取消导出"; return; }

            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(data);

            _vm.Status = opt.Format == ExportFormat.Svg
                ? $"已导出 SVG（矢量，{data.Length / 1024} KB）"
                : $"已导出 PNG {opt.PngScale:0.#}×（{data.Length / 1024} KB）";
        }
        catch (Exception ex) { _vm.Status = "导出失败：" + ex.Message; }
    }

    // ------------------------------------------------------------ 画布方向

    // 这四个按钮只改视图模型的 Xform，**导出对话框里不再问方向**：
    // 导出直接继承画布当前状态（见 MainViewModel.Export）——
    // 少一个选项就少一种「屏上这样、存出来那样」的可能。

    void OnRotateCw(object? sender, RoutedEventArgs e) => _vm.RotateCw();

    void OnRotateCcw(object? sender, RoutedEventArgs e) => _vm.RotateCcw();

    void OnMirrorX(object? sender, RoutedEventArgs e) => _vm.ToggleMirrorX();

    void OnMirrorY(object? sender, RoutedEventArgs e) => _vm.ToggleMirrorY();

    void OnResetXform(object? sender, RoutedEventArgs e) => _vm.ResetXform();

    void UpdateZoomLabel()
        => _vm.ZoomText = $"{Viewer.EffectiveScale * 100:0.#}%";
}