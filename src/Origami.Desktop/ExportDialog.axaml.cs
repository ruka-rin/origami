using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Origami.Core;

namespace Origami.Desktop;

/// <summary>
/// 导出选项对话框：格式（SVG/PNG）、配色（彩色/黑白）、PNG 分辨率。
/// <para>
/// ★ <b>分辨率只对 PNG 有意义</b> —— SVG 是矢量，不存在分辨率这个概念，
/// 所以选 SVG 时那一整块会被隐藏，而不是灰着放在那里让人以为有影响。</para>
/// </summary>
public partial class ExportDialog : Window
{
    readonly ViewTab? _tab;
    readonly ColorMode _currentColor;

    /// <summary>1× 时的像素尺寸，用来给「实际会导出多大」提供直观预期。</summary>
    readonly Size _baseSize;

    /// <summary>
    /// 无参构造。<b>只为设计器预览而存在</b> —— Avalonia 的 XAML 设计器只会实例化
    /// 无参构造的对象，没有它设计器里就是一个空白窗格。
    /// 运行时永远走 <see cref="ExportDialog(ViewTab?, ColorMode, Size)"/>。
    /// </summary>
    public ExportDialog() : this(null, ColorMode.Peaks, new Size(1000, 1000))
    {
    }

    public ExportDialog(ViewTab? tab, ColorMode currentColor, Size baseSize)
    {
        // ★ 不要自己写 InitializeComponent() —— XAML 编译器生成的同名方法还负责
        //   把 x:Name 声明的控件字段赋值上，手写一个同名方法会覆盖掉它。
        InitializeComponent();

        _tab = tab;
        _currentColor = currentColor;
        _baseSize = baseSize;

        TitleText.Text = tab is null ? "导出" : $"导出「{tab.Title}」";
        PngRadio.IsChecked = true;                 // 默认 PNG（多数人要的是图）
        ColorRadio.IsChecked = currentColor == ColorMode.Peaks;
        PlainRadio.IsChecked = currentColor == ColorMode.Plain;

        // 原子片分解本来每面一色，配色选项对它无意义
        bool canColor = tab?.SupportsColor ?? true;
        ColorRadio.IsEnabled = canColor;
        PlainRadio.IsEnabled = canColor;
        if (!canColor)
        {
            ColorRadio.IsChecked = true;          // 只是让取值合法
            PlainRadio.IsChecked = false;
        }

        SvgRadio.IsCheckedChanged += (_, _) => SyncEnabled();
        ScaleBox.SelectionChanged += (_, _) => UpdateSizeHint();

        SyncEnabled();
        UpdateSizeHint();
    }

    void SyncEnabled()
    {
        bool png = PngRadio.IsChecked == true;
        ScalePanel.IsVisible = png;
        VectorNote.IsVisible = !png;
    }

    ColorMode ChosenColor
        => ColorRadio.IsChecked == true ? ColorMode.Peaks : ColorMode.Plain;

    /// <summary>
    /// 直接给出<b>导出后的实际像素尺寸</b>，而不是含糊的倍数描述 ——
    /// 「4× 是多大」用户心里没数，「3840 × 3840 像素」才有。
    /// </summary>
    void UpdateSizeHint()
    {
        if (ScaleBox.SelectedItem is not ComboBoxItem { Tag: var tag }) return;
        if (!double.TryParse(tag?.ToString(), out var scale)) return;

        int w = (int)Math.Round(_baseSize.Width * scale);
        int h = (int)Math.Round(_baseSize.Height * scale);

        SizeHint.Text = scale >= 4
            ? $"输出 {w} × {h} 像素，文件会比较大，适合放大细看或印刷。"
            : $"输出 {w} × {h} 像素。";
    }

    /// <summary>用户确认后得到的选项；取消则为 null。</summary>
    public ExportOptions? Result { get; private set; }

    void OnExport(object? sender, RoutedEventArgs e)
    {
        double scale = 2.0;
        if (ScaleBox.SelectedItem is ComboBoxItem { Tag: var tag })
            double.TryParse(tag?.ToString(), out scale);

        Result = new ExportOptions
        {
            Format = PngRadio.IsChecked == true ? ExportFormat.Png : ExportFormat.Svg,
            // 原子片分解不支持配色，用当前值占位即可（渲染时不会读它）
            Color = _tab?.SupportsColor != false ? ChosenColor : _currentColor,
            PngScale = scale,
        };
        Close(Result);
    }

    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}