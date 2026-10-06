using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Threading;
using Origami.Core;

namespace Origami.Desktop;

/// <summary>
/// 轻量 MVVM 支撑：属性变更通知 + 一行赋值的 <c>Set</c>。没引入 CommunityToolkit.Mvvm
/// —— 只有十来个属性，需要的只是 <see cref="INotifyPropertyChanged"/>。
/// </summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(name);
        return true;
    }

    /// <summary>通知一个<b>派生</b>属性变了（<paramref name="name"/> 不指向任何字段）。</summary>
    protected void RaisePropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>导出格式。</summary>
public enum ExportFormat { Svg, Png }

/// <summary>导出选项。分辨率<b>只对 PNG 有意义</b> —— SVG 是矢量，不存在分辨率。</summary>
public sealed class ExportOptions
{
    public required ExportFormat Format { get; init; }
    public required ColorMode Color { get; init; }

    /// <summary>PNG 分辨率倍率。1 ≈ 屏幕；2 适合打印；4 适合放大细看。</summary>
    public double PngScale { get; init; } = 2.0;

    public string Extension => Format == ExportFormat.Svg ? "svg" : "png";

    /// <summary>建议文件名，把视图与配色都编码进去。</summary>
    public string FileName(ViewTab tab)
        => $"{tab.Shape switch { ShapeKind.Strip => "长条", _ => "方形" }}_{tab.Name}_"
         + (tab.SupportsColor ? (Color == ColorMode.Peaks ? "彩色" : "黑白") : "分解")
         + "." + Extension;
}

/// <summary>主窗口的视图模型。</summary>
public sealed class MainViewModel : Observable
{
    /// <summary>
    /// 母版输入框的初值。<b>仅调试构建</b>预填一个样例，省得每次启动都手输。
    /// </summary>
#if DEBUG
    const string InitialMaster = Origami.Core.SampleMasters.Krenio;
#else
    const string InitialMaster = "";
#endif

    public MainViewModel()
    {
        foreach (var v in Views.All)
            ViewTabs.Add(new ViewTab(v.Shape, v.Content));

        MasterText = InitialMaster;
        _color = ColorMode.Peaks;
        SelectedTab = ViewTabs[0];      // 长条·折痕图
        CanColorize = SelectedTab.SupportsColor;
    }

    // ---------------------------------------------------------------- 母版输入

    string _masterText = "";
    /// <summary>母版字符串（输入框直接绑定到这里）。</summary>
    public string MasterText
    {
        get => _masterText;
        set => Set(ref _masterText, value);
    }

    Views.Document? _doc;
    /// <summary>当前母版对应的全流程结果；解析失败时为 null。</summary>
    public Views.Document? Document => _doc;

    // ---------------------------------------------------------------- 文本输入框

    string _textInput = "";
    /// <summary>
    /// 文本输入框的内容（普通文本，如 <code>KRENIO</code>，可分行）。
    /// <para>
    /// 改动后<b>自动生成母版</b>，用防抖合并连打。文本框常驻、无需开关 ——
    /// 母版框就是它的产物，两者并存才说得通，藏起来反而像功能不存在。
    /// </para>
    /// </summary>
    public string TextInput
    {
        get => _textInput;
        set
        {
            if (Set(ref _textInput, value)) ScheduleGenerate();
        }
    }

    /// <summary>文本水印提示。</summary>
    public const string TextWatermark = "普通文本，可分行（字库支持 A-Z 0-9 与空格）";

    /// <summary>
    /// 文本改动后<b>自动生成母版</b>，用防抖合并连打。
    /// <para>
    /// 文本框是多行的（分行折蛇形），回车必须留给换行，失焦也不可靠
    /// —— 用户可能打完就不点别处、一直看着画布。所以改成边打边出：
    /// 设一个短延时，把连打过程合并成一次生成。
    /// </para>
    /// </summary>
    void ScheduleGenerate()
    {
        var timer = _generateTimer;
        if (timer is null)
        {
            var created = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(220),
            };
            created.Tick += async (_, _) =>
            {
                created.Stop();
                await GenerateFromTextAsync();
            };
            _generateTimer = created;
            timer = created;
        }

        timer.Stop();
        timer.Start();
    }

    DispatcherTimer? _generateTimer;

    /// <summary>
    /// 由折纸字库把文本输入框的内容编成母版串，<b>写进母版框</b>并重渲。
    /// <para>
    /// 落到母版框而不是另设一个只读展示框：母版框本来就是渲染的唯一来源，
    /// 落进去之后用户可直接编辑、导出、切视图，不用在两处之间同步。</para>
    /// </summary>
    public async Task GenerateFromTextAsync()
    {
        if (Busy) return;
        Busy = true;
        try
        {
            string raw = TextInput;
            if (string.IsNullOrWhiteSpace(raw))
            {
                Status = "请输入要生成的文本";
                return;
            }

            string master;
            try
            {
                master = await Task.Run(() => Origami.Core.FoldFont.TextToMaster(raw));
            }
            catch (Exception e)
            {
                Status = "生成失败：" + e.Message;
                return;
            }

            if (master.Length == 0)
            {
                Status = "文本里没有字库支持的字符（A-Z 0-9 与空格）";
                return;
            }

            MasterText = master;
            // 必须调 RenderCurrentAsync：Busy 已置位，RebuildAsync 开头的守卫会挡掉它
            await RenderCurrentAsync("正在生成并渲染…");
        }
        finally { Busy = false; }
    }

    // ---------------------------------------------------------------- 状态

    string _status = "就绪";
    /// <summary>状态栏文本。</summary>
    public string Status
    {
        get => _status;
        set => Set(ref _status, value);
    }

    string _summary = "";
    /// <summary>规模摘要（格数 / 方形尺寸 / 折痕数 / 成品尺寸）。</summary>
    public string Summary
    {
        get => _summary;
        set => Set(ref _summary, value);
    }

    string? _svg;
    /// <summary>当前视图的 SVG 文本（由 <see cref="SvgView.Source"/> 消费）。</summary>
    public string? SvgSource
    {
        get => _svg;
        private set => Set(ref _svg, value);
    }

    /// <summary>是否有图可显示（驱动空状态提示）。</summary>
    public bool HasSvg => _svg is not null;

    string _zoomText = "100%";
    /// <summary>当前倍率（视图控件回报）。</summary>
    public string ZoomText
    {
        get => _zoomText;
        set => Set(ref _zoomText, value);
    }

    bool _busy;
    /// <summary>忙碌中：禁用输入控件并显示遮罩。</summary>
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            RaisePropertyChanged(nameof(CanResetXform));   // 组合条件跟着变
        }
    }

    ViewTab? _selectedTab;
    /// <summary>当前视图（形状 × 内容 解析出的唯一组合）。</summary>
    public ViewTab? SelectedTab
    {
        get => _selectedTab;
        private set => Set(ref _selectedTab, value);
    }

    // ---------------------------------------------------------------- 视图维度
    //
    // 6 种视图 = 2 形状 × 3 内容。界面把它们拆成两组单选（画布左上角、右上角），
    // 而不是 6 个标签 —— 后者会让侧栏变长，且两组的正交关系被压平了。

    ShapeKind _shape = ShapeKind.Strip;

    /// <summary>纸面形状：长条 / 方形。切换后重渲当前内容。</summary>
    public ShapeKind Shape
    {
        get => _shape;
        set
        {
            if (!Set(ref _shape, value)) return;
            SelectView(ViewInfo_Of(value, _content));
        }
    }

    ContentKind _content = ContentKind.Crease;

    /// <summary>图面内容：折痕图 / 成品 / 原子片。切换后重渲当前形状。</summary>
    public ContentKind Content
    {
        get => _content;
        set
        {
            if (!Set(ref _content, value)) return;
            SelectView(ViewInfo_Of(_shape, value));
        }
    }

    static ViewInfo ViewInfo_Of(ShapeKind s, ContentKind c) => Views.Resolve(s, c);

    ViewTab SlotOf(ShapeKind s, ContentKind c)
    {
        var t = ViewTabs.FirstOrDefault(v => v.Shape == s && v.Content == c);
        if (t is not null) return t;
        t = new ViewTab(s, c);
        ViewTabs.Add(t);
        return t;
    }

    /// <summary>切到指定视图。渲染是异步的，这里不阻塞调用方（界面不该被卡住）。</summary>
    void SelectView(ViewInfo info)
    {
        var tab = SlotOf(info.Shape, info.Content);
        _ = SelectViewAsync(tab);
    }

    /// <summary>把形状与内容切换到某个既有视图（导入后用）。</summary>
    public void SelectView(ViewKind kind)
    {
        var v = Views.All.First(x => x.Kind == kind);
        _shape = v.Shape;
        _content = v.Content;
        RaisePropertyChanged(nameof(Shape));
        RaisePropertyChanged(nameof(Content));
        SelectView(v);
    }

    // ---------------------------------------------------------------- 上色

    ColorMode _color = ColorMode.Peaks;

    /// <summary>
    /// 上色开关。
    /// <para>
    /// 这是一个<b>属性</b>而不是两个视图：「黑白 vs 彩色」「渲染 vs 剪影」是同一张图的
    /// 两种呈现 —— 关掉时折痕图变题面线稿、成品变剪影，语义一致，
    /// 都是「去掉颜色，只看形状」。</para>
    /// </summary>
    public ColorMode Color
    {
        get => _color;
        set
        {
            if (!Set(ref _color, value)) return;
            RaisePropertyChanged(nameof(IsColorOn));   // 让绑定的开关跟着更新
            InvalidateCaches();
            _ = RerenderCurrentAsync();
        }
    }

    /// <summary>
    /// 开关是否处于「上色」。<b>必须是可写属性</b> —— 开关直接双向绑定到它，
    /// 写成只读计算属性会让点击回写找不到 setter，<b>点了没反应</b>。
    /// 让它成为唯一入口，绑定就不需要任何 code-behind 兜底。
    /// </summary>
    public bool IsColorOn
    {
        get => _color == ColorMode.Peaks;
        set => Color = value ? ColorMode.Peaks : ColorMode.Plain;
    }

    bool _canColorize = true;

    /// <summary>当前视图是否支持上色（原子片分解本来每面一色，开关要禁用）。</summary>
    public bool CanColorize
    {
        get => _canColorize;
        private set => Set(ref _canColorize, value);
    }

    /// <summary>母版变了或上色变了，各视图缓存全部失效。</summary>
    void InvalidateCaches()
    {
        foreach (var t in ViewTabs) t.Cached = null;
    }

    // ---------------------------------------------------------------- 画布方向

    CanvasTransform _xform = CanvasTransform.Identity;

    /// <summary>
    /// 画布方向（整点旋转 + 镜像）。
    /// <para>
    /// 与其去改折叠算法的方向约定，不如在显示层把整张图转一下：同一张母版可以来回对照，
    /// 而且<b>导出直接继承这个设置</b>，导出对话框里不再问一遍。
    /// 四个方向互为闭环，转满 360° 回到原样，不存在「转乱了回不来」。</para>
    /// </summary>
    public CanvasTransform Xform
    {
        get => _xform;
        set
        {
            if (_xform == value) return;
            _xform = value;
            IsXformIdentity = value.IsIdentity;

            RaisePropertyChanged(nameof(XformDescription));
            RaisePropertyChanged(nameof(CanResetXform));

            InvalidateCaches();       // 方向变了，缓存的 SVG 一律作废
            ViewerFit?.Invoke(SelectedTab);   // 宽高可能互换了，重新取景
            _ = RerenderCurrentAsync();
        }
    }

    /// <summary>顺时针转 90°。</summary>
    public void RotateCw() => Xform = Xform.RotateCw();

    /// <summary>逆时针转 90°。</summary>
    public void RotateCcw() => Xform = Xform.RotateCcw();

    /// <summary>切换左右镜像。</summary>
    public void ToggleMirrorX() => Xform = Xform.MirrorX(!Xform.FlipX);

    /// <summary>切换上下镜像。</summary>
    public void ToggleMirrorY() => Xform = Xform.MirrorY(!Xform.FlipY);

    /// <summary>恢复原始方向。</summary>
    public void ResetXform() => Xform = CanvasTransform.Identity;

    /// <summary>当前方向的文字描述（底栏显示，便于确认导出的是什么方向）。</summary>
    public string XformDescription => _xform.Describe();

    bool _isXformIdentity = true;

    /// <summary>是否处于原始方向。</summary>
    public bool IsXformIdentity
    {
        get => _isXformIdentity;
        private set => Set(ref _isXformIdentity, value);
    }

    /// <summary>
    /// 「恢复原始方向」按钮是否可用 = 不忙 + 当前确实不是原始方向。
    /// <para>Avalonia 绑定表达式<b>没有</b> <c>&amp;&amp;</c> 运算符（写了会在运行时报
    /// ExpressionParseException，编译期却看不出来），所以组合条件一律在这里算好再暴露出去。</para>
    /// </summary>
    public bool CanResetXform => !Busy && !IsXformIdentity;

    /// <summary>导入后需要按目标视图的取景方式复位预览 —— 由窗口注入。</summary>
    public Action<ViewTab?>? ViewerFit { get; set; }

    /// <summary>视图标签页（按 长条 / 长条成品 / 方形 / 方形成品 / 检查 分组）。</summary>
    public ObservableCollection<ViewTab> ViewTabs { get; } = new();

    // ---------------------------------------------------------------- 动作

    /// <summary>
    /// 导入外部文件（母版文本 / 长条折痕图 SVG / 方形折痕图 SVG，彩色与黑白都支持）。
    /// <para>导入后：还原出的母版填进输入框、「上色」开关同步成文件里的状态、
    /// 切到<b>刚导入的那一类视图</b>。</para>
    /// </summary>
    public async Task ImportAsync(string text, string fileName)
    {
        if (Busy) return;
        Busy = true;
        Status = "正在导入…";
        try
        {
            var r = Importer.Load(text, fileName);

            MasterText = r.MasterText;
            Color = r.Color;                 // 文件是黑白就自动把开关关掉

            // 导入了什么就先看什么（切形状 + 内容两个维度）
            SelectView(r.PreferredView);
            ViewerFit?.Invoke(SelectedTab);

            // 必须调 RenderCurrentAsync：RebuildAsync 的 `if (Busy) return` 会挡掉它
            await RenderCurrentAsync("正在导入并渲染…");

            string where = SelectedTab is null ? "" : $"（{SelectedTab.Title}）";
            Status = r.Warning is { } w ? $"已导入 {fileName}{where}。注意：{w}"
                                       : $"已导入 {fileName}{where}";
        }
        catch (Exception e)
        {
            Status = "导入失败：" + e.Message;
        }
        finally { Busy = false; }
    }

    /// <summary>解析 + 折叠 + 构建当前视图。耗时操作放后台线程。</summary>
    public async Task RebuildAsync()
    {
        if (Busy) return;
        await RenderCurrentAsync("正在计算…");
    }

    /// <summary>
    /// 真正干活的渲染。<b>不做 Busy 守卫</b> —— 导入流程已经把自己置为 Busy，
    /// 再调 <see cref="RebuildAsync"/> 会被它的守卫直接挡掉，导入后就永远不渲染了。
    /// </summary>
    async Task RenderCurrentAsync(string busyStatus)
    {
        var tab = SelectedTab ?? ViewTabs[0];
        string raw = MasterText;

        if (string.IsNullOrWhiteSpace(raw))
        {
            ClearAll();
            Status = "请输入母版";
            return;
        }

        Busy = true;
        Status = busyStatus;
        try
        {
            // 铺排 + 折叠是 CPU 密集的，跑在后台以免冻结界面
            var work = await Task.Run(() =>
            {
                var doc = Views.Build(raw);
                return (Doc: doc, Svg: Views.BuildSvg(doc, tab.Kind, Color, Xform));
            });

            _doc = work.Doc;
            InvalidateCaches();
            SetSvg(work.Svg, tab);

            var m = Master.ParseMaster(raw);
            Summary = $"{m.NCell} 格 · 竖折痕 {m.Pipes.Count} · "
                    + $"方形 {work.Doc.Square.N}×{work.Doc.Square.N} · "
                    + $"折痕 {work.Doc.Square.Folds.Count} · "
                    + $"长条成品 {BoxOf(work.Doc.StripFolded.World)} 格 · "
                    + $"方形成品 {BoxOf(work.Doc.SquareFolded.World)} 格";
            Status = Describe(tab);
        }
        catch (Exception e)
        {
            ClearAll();
            Status = "出错：" + e.Message;
        }
        finally { Busy = false; }
    }

    /// <summary>切换视图（命中缓存则直接显示）。</summary>
    public async Task SelectViewAsync(ViewTab tab)
    {
        if (tab is null) return;
        if (_doc is null) { Status = "请先解析母版"; return; }

        SelectedTab = tab;
        // 原子片分解本来每面一色，上色开关对它无意义
        CanColorize = tab.SupportsColor;

        if (tab.Cached is { } cached)      // 缓存命中
        {
            SvgSource = cached;
            RaisePropertyChanged(nameof(HasSvg));
            Status = Describe(tab);
            return;
        }

        // 视图切换在 Busy 期间仍可点，连点两个未缓存视图会并发构建，
        // 用递增序号保证「只有最后一次点击的结果能落地」。
        int seq = ++_viewSeq;
        Busy = true;
        Status = $"正在构建 {tab.Title}…";
        try
        {
            var svg = await Task.Run(() => Views.BuildSvg(_doc, tab.Kind, Color, Xform));
            if (seq != _viewSeq) return;         // 已被更新的请求取代，丢弃
            SetSvg(svg, tab);
            Status = Describe(tab);
        }
        catch (Exception e)
        {
            if (seq == _viewSeq) Status = "出错：" + e.Message;
        }
        finally
        {
            if (seq == _viewSeq) Busy = false;
        }
    }

    int _viewSeq;

    /// <summary>
    /// 按当前设置重渲选中视图（上色开关 / 画布方向变更后调它）。
    /// <para>
    /// 忙时只置一个待重绘标记、本次结束后补跑一次：连点 10 次方向按钮也只渲染最后那一次，
    /// 中间态直接跳过 —— 否则画面会停在中间某个方向，和状态栏描述不符。</para>
    /// </summary>
    async Task RerenderCurrentAsync()
    {
        if (_doc is null) return;
        var tab = SelectedTab;
        if (tab is null) return;

        if (Busy) { _rerenderPending = true; return; }

        Busy = true;
        string? err = null;
        try
        {
            do
            {
                _rerenderPending = false;
                Status = "正在更新画布…";
                // 每次循环都重新读取 Xform / Color：连点时要把中间态跳掉
                var svg = await Task.Run(() => Views.BuildSvg(_doc!, tab.Kind, Color, Xform));
                SetSvg(svg, tab);
            }
            while (_rerenderPending);
        }
        catch (Exception e) { err = e.Message; }
        finally
        {
            Busy = false;
            // 出错时保留错误信息，不要被正常描述覆盖掉（否则界面完全无感知）
            Status = err is { } m ? "出错：" + m : Describe(tab);
        }
    }

    bool _rerenderPending;

    /// <summary>状态栏描述：视图 + 上色状态（当前开关处于哪一边）。</summary>
    string Describe(ViewTab tab)
        => tab.SupportsColor
            ? $"{tab.Hint}（上色：{(IsColorOn ? "彩色" : "黑白/剪影")}）"
            : tab.Hint;

    void SetSvg(string svg, ViewTab tab)
    {
        tab.Cached = svg;
        SvgSource = svg;
        RaisePropertyChanged(nameof(HasSvg));
    }

    void ClearAll()
    {
        _doc = null;
        Summary = "";
        InvalidateCaches();
        SvgSource = null;
        RaisePropertyChanged(nameof(HasSvg));
    }

    /// <summary>导出当前视图。格式 / 配色 / 分辨率由对话框给出。</summary>
    public (byte[] Data, string ExtName) Export(ViewTab tab, ExportOptions opt)
    {
        if (_doc is null) throw new InvalidOperationException("没有可导出的内容");

        if (opt.Format == ExportFormat.Svg)
        {
            // SVG 是矢量，「分辨率」对它没有意义，因此复用预览那一份文本 ——
            // 但缓存是用画布当前配色建的，用户在对话框里改了配色就必须重建。
            var svg = opt.Color == Color && tab.Cached is { } cached
                ? cached
                : Views.BuildSvg(_doc, tab.Kind, opt.Color, Xform);
            return (System.Text.Encoding.UTF8.GetBytes(svg), "svg");
        }

        // 方向不在导出对话框里问：直接继承画布当前设置
        return (Views.RenderPng(_doc, tab.Kind, opt.Color, opt.PngScale, Xform), "png");
    }

    /// <summary>成品外包尺寸（格），用于状态栏。</summary>
    static string BoxOf(IReadOnlyList<List<Point>> world)
    {
        if (world.Count == 0) return "-";
        double x0 = world.SelectMany(f => f).Min(p => p.X);
        double x1 = world.SelectMany(f => f).Max(p => p.X);
        double y0 = world.SelectMany(f => f).Min(p => p.Y);
        double y1 = world.SelectMany(f => f).Max(p => p.Y);
        return $"{x1 - x0:0.##}×{y1 - y0:0.##}";
    }
}

/// <summary>
/// 一个视图缓存槽。形状与内容两个维度解析出唯一的 <see cref="ViewInfo"/>，
/// 槽本身负责记住该视图已构建的 SVG，避免切回来时重跑折叠。
/// </summary>
public sealed class ViewTab(ShapeKind shape, ContentKind content) : Observable
{
    public ShapeKind Shape { get; } = shape;
    public ContentKind Content { get; } = content;

    public ViewInfo Info => Views.Resolve(Shape, Content);
    public ViewKind Kind => Info.Kind;
    public string Name => Info.Name;
    public string Hint => Info.Hint;
    public FitMode Fit => Info.Fit;

    /// <summary>这个视图能不能上色（原子片分解本来就每面一色，不受开关影响）。</summary>
    public bool SupportsColor => Info.SupportsColor;

    string? _cached;
    /// <summary>该视图已构建的 SVG 文本（缓存，避免重复构建）。</summary>
    public string? Cached
    {
        get => _cached;
        set => Set(ref _cached, value);
    }

    /// <summary>标签文字：「长条 · 折痕图」。</summary>
    public string Title => $"{Shape switch { ShapeKind.Strip => "长条", _ => "方形" }} · {Name}";
}