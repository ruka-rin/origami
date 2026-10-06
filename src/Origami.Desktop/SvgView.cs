using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;

namespace Origami.Desktop;

/// <summary>
/// 矢量 SVG 预览控件：<b>无损缩放</b>，只在鼠标操作下交互。
/// <para>
/// ★ 为什么是矢量而不是位图：折痕图是规整网格、成品是斜边剪影，用 PNG 预览时
/// 放大到 8倍就糊了 —— 而「看清一条折痕的端点」恰恰是这个工具的核心用途。
/// 直接解析 SVG 并用 <c>DrawingContext</c> 画几何，任何倍率都是锐的，
/// 也省掉「分辨率」这个概念（只在导出 PNG 时才需要）。</para>
/// <para>
/// <b>为什么自己写</b>：Avalonia 没有内置 SVG 渲染控件，第三方包多为移动端设计，
/// 在桌面端的滚轮缩放 / 触控板双指缩放这些交互上并不完整。这里只需要
/// 「滚轮缩放 + 按住拖动」，因此写一个专用控件比引入依赖更合适。</para>
/// <para>
/// <b>缩放锚点</b>：滚轮缩放时保持<b>光标下的图像点不动</b>。否则以控件中心缩放，
/// 细节图会「跳」出视野。</para>
/// </summary>
public sealed class SvgView : Control
{
    // ---------------------------------------------------------------- 状态

    SvgDocument? _doc;
    double _zoom = 1.0;      // 相对「适应窗口」的额外倍数
    double _panX, _panY;     // 平移量（控件坐标像素）
    bool _dragging;
    Point _dragOrigin;
    double _dragPanX, _dragPanY;

    /// <summary>缩放上下限。适应窗口时可能远小于 1，放大要能看到像素级细节。</summary>
    const double MinZoom = 0.05, MaxZoom = 400.0;

    /// <summary>倍率变化时通知外部（状态栏显示）。</summary>
    public event EventHandler? ViewChanged;

    // ---------------------------------------------------------------- 依赖属性

    public static readonly StyledProperty<string?> SourceProperty =
        AvaloniaProperty.Register<SvgView, string?>(nameof(Source));

    /// <summary>SVG <b>文本</b>。设置后重新解析并复位视图。</summary>
    public string? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public static readonly StyledProperty<FitMode> DefaultFitProperty =
        AvaloniaProperty.Register<SvgView, FitMode>(nameof(DefaultFit), FitMode.Window);

    /// <summary>
    /// 换图时的默认取景。<b>换图（<see cref="Source"/> 变化）时自动应用。</b>
    /// 长条折痕图是 100:1 的极端长宽 ratio，用「适应窗口」会把每格压到几个像素，
    /// 所以它声明 <see cref="FitMode.Width"/>（铺满宽度）。
    /// </summary>
    public FitMode DefaultFit
    {
        get => GetValue(DefaultFitProperty);
        set => SetValue(DefaultFitProperty, value);
    }

    // ---------------------------------------------------------------- 取景

    /// <summary>当前实际显示倍率（= 适应窗口倍率 × 用户缩放）。</summary>
    public double EffectiveScale => FitScale * _zoom;

    /// <summary>适应窗口时的倍率。</summary>
    double FitScale => _doc is null
            ? 1.0
            : Viewport.FitScale(Bounds.Size, _doc.ViewBox.Size);

    /// <summary>缩放到整张图可见。</summary>
    public void FitToWindow()
    {
        _zoom = 1.0;
        _panX = _panY = 0;
        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>横向铺满窗口宽度，纵向自然高度。</summary>
    public void FitToWidth()
    {
        if (_doc is null) return;
        double s = Bounds.Width / _doc.ViewBox.Width;
        if (s <= 0) return;
        _zoom = s / FitScale;
        _panX = 0;
        _panY = (Bounds.Height - _doc.ViewBox.Height * s) / 2;
        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>以 1:1 用户单位显示并居中。</summary>
    public void ActualSize()
    {
        if (_doc is null) return;
        _zoom = 1.0 / Math.Max(FitScale, 1e-9);
        _panX = _panY = 0;
        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>按倍率缩放（保持视图中心不动）。</summary>
    public void ZoomBy(double factor)
        => ZoomAt(factor, Bounds.Width / 2, Bounds.Height / 2);

    void ZoomAt(double factor, double cx, double cy)
    {
        if (_doc is null) return;
        double before = EffectiveScale;

        double zoom = Math.Clamp(_zoom * factor, MinZoom / FitScale, MaxZoom / FitScale);
        if (Math.Abs(zoom - _zoom) < 1e-12) return;
        _zoom = zoom;

        // ★ after 必须是「换算后」的实际倍率，不能直接拿 _zoom 顶上。
        //   before 是 FitScale × _zoom（含适应窗口的倍率），而 _zoom 只是
        //   相对倍率 —— 两者量纲不同，相除得到的是 factor / FitScale 而不是
        //   factor，缩放就会朝错误的方向跑，而且看起来只是「锚点不准」。
        //   FitScale 在本次调用内不会变（Bounds 没动），所以直接重取一次即可。
        double after = EffectiveScale;

        // 锚点公式在 Viewport.AnchorPan 里（有单元测试钉着）
        _panX = Viewport.AnchorPan(_panX, cx, before, after);
        _panY = Viewport.AnchorPan(_panY, cy, before, after);

        ClampPan();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>限制平移，别让图被拖出视野。</summary>
    void ClampPan()
    {
        if (_doc is null) return;
        double s = EffectiveScale;

        _panX = Viewport.ClampPan(_panX, Bounds.Width, _doc.ViewBox.Width * s);
        _panY = Viewport.ClampPan(_panY, Bounds.Height, _doc.ViewBox.Height * s);
    }

    // ---------------------------------------------------------------- 输入（仅鼠标）

    /// <summary>构造时挂上双击。<see cref="InputElement.DoubleTapped"/> 是事件而非虚方法，
    /// 因此只能订阅，不能 <c>override</c>。</summary>
    public SvgView()
    {
        // 在「1:1」与「适应窗口」之间来回切 —— 放大看清细节后一键回到全局
        DoubleTapped += (_, _) =>
        {
            if (_doc is null) return;
            if (_toggledFit) FitToWindow(); else ActualSize();
            _toggledFit = !_toggledFit;
        };
    }

    bool _toggledFit;

    /// <summary>
    /// 换图时<b>是否保留当前的缩放与平移</b>。
    /// <para>
    /// 判据是<b>viewBox 尺寸是否相同</b>：换视图时 viewBox 必然不同（长条 / 方形 / 成品
    /// 的用户坐标范围都不一样），而切上色时用户坐标一格没动、只有颜色变了。
    /// </para>
    /// <para>
    /// 不保留的代价很实际：放大到一半正在看某条折痕的端点，拨一下开关画面就弹回
    /// 适应窗口，刚找到的位置全丢。
    /// </para>
    /// </summary>
    public static bool ShouldPreserveView(Rect oldBox, Rect newBox)
        => oldBox.Width > 0 && oldBox.Height > 0
           && oldBox.Size == newBox.Size;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs c)
    {
        base.OnPropertyChanged(c);

        // 取景模式变了立刻生效，不必等下一张图
        if (c.Property == DefaultFitProperty && _doc is not null)
        {
            ApplyDefaultFit();
            InvalidateVisual();
            RaiseViewChanged();
            return;
        }

        if (c.Property != SourceProperty) return;

        var text = Source;
        var prevBox = _doc?.ViewBox ?? default;
        bool sameView = false;
        try
        {
            var next = string.IsNullOrWhiteSpace(text) ? null : SvgDocument.Parse(text);
            _parseError = null;
            sameView = _doc is not null && next is not null
                       && ShouldPreserveView(prevBox, next.ViewBox);
            _doc = next;
        }
        catch (Exception e)
        {
            _doc = null;
            _parseError = e.Message;
        }

        if (sameView)          // 同一视图换配色：保住视角
        {
            ClampPan();
            InvalidateVisual();
            RaiseViewChanged();
            return;
        }

        _panX = _panY = 0;
        _zoom = 1.0;
        ApplyDefaultFit();
        InvalidateVisual();
        RaiseViewChanged();
    }

    void ApplyDefaultFit()
    {
        if (DefaultFit == FitMode.Width) FitToWidth();
        else FitToWindow();
    }

    string? _parseError;

    /// <summary>上次解析失败的原因（无则 null）。</summary>
    public string? ParseError => _parseError;

    /// <summary>
    /// 当前文档的用户坐标尺寸，即「1× 导出」时的像素尺寸。
    /// 导出对话框用它算出各倍率下的实际输出像素。
    /// </summary>
    public Size DocumentBox => _doc?.ViewBox.Size ?? new Size(0, 0);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_doc is null) return;
        ClampPan();
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (_doc is null) return;

        // 触控板会发一串细分滚轮事件；不累加，按方向乘固定步长（每格 1.15 倍），
        // 这样触控板与鼠标滚轮手感一致。
        double step = Math.Pow(1.15, Math.Sign(e.Delta.Y));
        var p = e.GetPosition(this);
        ZoomAt(step, p.X, p.Y);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_doc is null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsLeftButtonPressed)
        {
            _dragging = true;
            _dragOrigin = e.GetPosition(this);
            _dragPanX = _panX;
            _dragPanY = _panY;
            Cursor = new Cursor(StandardCursorType.Hand);
        }
        else if (props.IsRightButtonPressed)
        {
            FitToWindow();          // 右键单击 = 回到适应窗口
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        _panX = _dragPanX + (p.X - _dragOrigin.X);
        _panY = _dragPanY + (p.Y - _dragOrigin.Y);
        ClampPan();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        Cursor = Cursor.Default;
        RaiseViewChanged();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
        Cursor = Cursor.Default;
    }

    // ---------------------------------------------------------------- 绘制

    public override void Render(DrawingContext ctx)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.Parse("#1E1E22")), Bounds);

        if (_doc is null)
        {
            if (_parseError is { } err)
                DrawCentered(ctx, "SVG 解析失败：" + err);
            else
                DrawCentered(ctx, "没有可显示的内容");
            return;
        }

        double s = EffectiveScale;
        var vb = _doc.ViewBox;

        // 用户坐标 -> 控件坐标：平移 + 缩放 + 视口原点偏移
        var m = Matrix.CreateScale(s, s)
                * Matrix.CreateTranslation(_panX - vb.X * s, _panY - vb.Y * s);

        using (ctx.PushTransform(m))
            foreach (var n in _doc.Nodes)
                n.Draw(ctx, Matrix.Identity);

        // 勾出图与背景的界
        var dst = new Rect(_panX, _panY, vb.Width * s, vb.Height * s);
        ctx.DrawRectangle(new Pen(new SolidColorBrush(Color.Parse("#3A3A40")), 1), dst);
    }

    void DrawCentered(DrawingContext ctx, string text)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                                   FlowDirection.LeftToRight,
                                   new Typeface(FontFamily.Default), 13,
                                   Brushes.Gray);
        ctx.DrawText(ft, new Point((Bounds.Width - ft.Width) / 2, (Bounds.Height - ft.Height) / 2));
    }

    void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);
}