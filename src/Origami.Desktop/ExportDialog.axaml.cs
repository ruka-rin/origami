using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Origami.Core;

namespace Origami.Desktop;

/// <summary>
/// 导出选项对话框：格式（SVG/PNG）、配色（彩色/黑白）、PNG 分辨率。
/// <para>
/// 分辨率只对 PNG 有意义 —— SVG 是矢量，不存在分辨率概念，
/// 所以选 SVG 时那一整块会被隐藏，而不是灰着放在那里让人以为有影响。</para>
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
        ColorChoice.IsEnabled = canColor;
        PlainChoice.IsEnabled = canColor;
        PlainLabel.Classes.Set("disabled", !canColor);
        if (!canColor)
        {
            ColorRadio.IsChecked = true;          // 只是让取值合法
            PlainRadio.IsChecked = false;
        }

        SvgRadio.IsCheckedChanged += (_, _) => SyncFormat();
        ColorRadio.IsCheckedChanged += (_, _) => SyncFormat();
        PlainRadio.IsCheckedChanged += (_, _) => SyncFormat();

        // 在代码里订阅而不是写在 XAML 上：SelectedIndex="1" 会在
        // InitializeComponent() 期间就触发 SelectionChanged，那时 _baseSize 还没赋值。
        ScaleBox.SelectionChanged += (_, _) => UpdateSizeHint();

        SyncFormat();
        UpdateSizeHint();
    }

    // ---------------------------------------------------------------- 选项卡片

    void OnPickSvg(object? sender, PointerPressedEventArgs e) => SvgRadio.IsChecked = true;

    void OnPickPng(object? sender, PointerPressedEventArgs e) => PngRadio.IsChecked = true;

    void OnPickColor(object? sender, PointerPressedEventArgs e) => ColorRadio.IsChecked = true;

    void OnPickPlain(object? sender, PointerPressedEventArgs e) => PlainRadio.IsChecked = true;

    /// <summary>把 RadioButton 的选中态同步到卡片的视觉态，并按格式切换可见区块。</summary>
    void SyncFormat()
    {
        bool png = PngRadio.IsChecked == true;

        SetActive(SvgChoice, SvgRadio.IsChecked == true);
        SetActive(PngChoice, png);
        SetActive(ColorChoice, ColorRadio.IsChecked == true);
        SetActive(PlainChoice, PlainRadio.IsChecked == true);

        ScalePanel.IsVisible = png;
        VectorNote.IsVisible = !png;
    }

    static void SetActive(Border card, bool on)
        => card.Classes.Set("on", on);

    ColorMode ChosenColor
        => ColorRadio.IsChecked == true ? ColorMode.Peaks : ColorMode.Plain;

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