using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Origami.Core;

namespace Origami.Desktop;

/// <summary>
/// 导出选项对话框：格式（SVG/PNG）、配色（彩色/黑白）、PNG 分辨率。
/// <para>
/// 窗口高度跟随内容（<c>SizeToContent="Height"</c>）：配色与分辨率两块会按所选
/// 视图/格式整块隐藏，固定高度会留出一块空白，而按隐藏项手改高度又要靠魔法数字
/// 和每个分支对齐。</para>
/// </summary>
public partial class ExportDialog : Window
{
    readonly ViewTab? _tab;
    readonly ColorMode _currentColor;

    /// <summary>1× 时的像素尺寸，用来给「实际会导出多大」提供直观预期。</summary>
    readonly Size _baseSize;

    /// <summary>
    /// 无参构造，仅为 XAML 设计器预览存在 —— 设计器只实例化无参构造的对象。
    /// 运行时永远走 <see cref="ExportDialog(ViewTab?, ColorMode, Size)"/>。
    /// </summary>
    public ExportDialog() : this(null, ColorMode.Peaks, new Size(1000, 1000))
    {
    }

    public ExportDialog(ViewTab? tab, ColorMode currentColor, Size baseSize)
    {
        // 不要自己写 InitializeComponent() —— XAML 编译器生成的同名方法还负责
        // 把 x:Name 声明的控件字段赋值上，手写会覆盖掉它
        InitializeComponent();
        AppIcon.Apply(this);

        _tab = tab;
        _currentColor = currentColor;
        _baseSize = baseSize;

        TitleText.Text = tab is null ? "导出" : $"导出「{tab.Title}」";

        PngRadio.IsChecked = true;                 // 默认 PNG（多数人要的是图）
        ColorRadio.IsChecked = currentColor == ColorMode.Peaks;
        PlainRadio.IsChecked = currentColor == ColorMode.Plain;

        // 原子片分解本来每面一色，配色选项对它无意义 —— 整块隐藏，高度由窗口自己收
        if (tab?.SupportsColor == false)
        {
            ColorPanel.IsVisible = false;
            ColorRadio.IsChecked = true;          // 只是让取值合法
            PlainRadio.IsChecked = false;
        }

        SvgRadio.IsCheckedChanged += (_, _) => SyncFormat();
        ColorRadio.IsCheckedChanged += (_, _) => SyncFormat();
        PlainRadio.IsCheckedChanged += (_, _) => SyncFormat();

        // 在代码里订阅而不是写在 XAML 上：SelectedIndex="1" 会在
        // InitializeComponent() 期间就触发 SelectionChanged，那时 _baseSize 还没赋值
        ScaleBox.SelectionChanged += (_, _) => UpdateSizeHint();

        SyncFormat();
        UpdateSizeHint();
    }

    /// <summary>
    /// 按所选格式切换「分辨率」与「矢量无分辨率」两块，二者必居其一。
    /// <para>
    /// 判据用「SVG 被选中则无分辨率」而非「PNG 是否选中」——
    /// 两项同时为 true 时（初始化阶段、或分组未生效的环境）后者会误判成 PNG。
    /// SVG 是明确的一端，用它更稳。
    /// </para>
    /// </summary>
    void SyncFormat()
    {
        bool svg = SvgRadio.IsChecked == true;

        // 整块隐藏：留着标题或灰着的控件都会让人以为分辨率对 SVG 有影响
        ScalePanel.IsVisible = !svg;
        VectorNote.IsVisible = svg;
    }

    ColorMode ChosenColor
    {
        get
        {
            // 两项都未选中时退回当前画布的配色，而不是默认彩色：
            // 原子片分解不支持配色，此项由 CurrentColor 占位。
            if (ColorRadio.IsChecked == true) return ColorMode.Peaks;
            if (PlainRadio.IsChecked == true) return ColorMode.Plain;
            return _currentColor;
        }
    }

    /// <summary>
    /// 直接给出<b>导出后的实际像素尺寸</b>，而不是含糊的倍数描述 ——
    /// 「4× 是多大」用户心里没数，「3840 × 3840 像素」才有。
    /// </summary>
    void UpdateSizeHint()
    {
        // IsCheckedChanged 可能在 InitializeComponent() 期间就触发，
        // 那时控件与字段都还没就绪
        if (SizeHint is null || ScaleBox is null) return;
        if (ScaleBox.SelectedItem is not ComboBoxItem { Tag: var tag }) return;
        if (!double.TryParse(tag?.ToString(), out var scale)) return;

        int w = (int)Math.Round(_baseSize.Width * scale);
        int h = (int)Math.Round(_baseSize.Height * scale);

        SizeHint.Text = scale >= 4
            ? $"输出 {w} × {h} 像素，文件会比较大，适合放大细看或印刷。"
            : $"输出 {w} × {h} 像素。";
    }

    void OnExport(object? sender, RoutedEventArgs e)
    {
        double scale = 2.0;
        if (ScaleBox.SelectedItem is ComboBoxItem { Tag: var tag })
            double.TryParse(tag?.ToString(), out scale);

        var result = new ExportOptions
        {
            // 与 SyncFormat 保持同一判据：SVG 选中就是 SVG，否则 PNG
            Format = SvgRadio.IsChecked == true ? ExportFormat.Svg : ExportFormat.Png,
            // 原子片分解不支持配色，用当前值占位即可（渲染时不会读它）
            Color = _tab?.SupportsColor != false ? ChosenColor : _currentColor,
            PngScale = scale,
        };
        Close(result);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}