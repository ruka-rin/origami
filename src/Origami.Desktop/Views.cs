using Origami.Core;

namespace Origami.Desktop;

/// <summary>换图时的默认取景方式。</summary>
public enum FitMode
{
    /// <summary>缩放到整张图都可见。</summary>
    Window,
    /// <summary>横向铺满窗口宽度，纵向自然高度（给宽高比悬殊的图用）。</summary>
    Width,
}

/// <summary>纸面形状维度。</summary>
public enum ShapeKind
{
    /// <summary>长条 —— 母版原样的一长条。</summary>
    Strip,
    /// <summary>方形 —— 铺排成方形后的 CP。</summary>
    Square,
}

/// <summary>图面内容维度。</summary>
public enum ContentKind
{
    /// <summary>折痕图。</summary>
    Crease,
    /// <summary>折叠成品。</summary>
    Folded,
    /// <summary>原子片分解（每面一色，检查层序与漏面）。</summary>
    Pieces,
}

/// <summary>一个可显示的视图种类：形状 × 内容，共 6 种。</summary>
/// <para>
/// 「黑白 vs 彩色」「渲染 vs 剪影」<b>不是独立视图</b>，而是同一张图的
/// 配色属性 —— 由界面上的「上色」开关切换。把它拆成独立维度会让切换项翻倍，
/// 而这两组之间没有任何其它差异。
/// </para>
public enum ViewKind
{
    Strip,        // 长条折痕图
    StripFolded,  // 长条折叠成品（顺次滚动）
    Square,       // 方形折痕图
    SquareFolded, // 方形折叠成品（树反射）
    StripPieces,  // 长条原子片分解（检查顺次折叠的层序与漏面）
    SquarePieces, // 方形原子片分解（检查树反射的层序与漏面）
}

/// <summary>一个视图支持的上色方式。</summary>
public enum ColorMode
{
    /// <summary>无色 —— 折痕图是题面线稿（全黑），成品是剪影（纯黑块）。</summary>
    Plain,
    /// <summary>按峰谷上色 —— 折痕图红=峰/蓝=谷/绿=剪切线，成品按层序着色。</summary>
    Peaks,
}

/// <summary>视图的元信息：形状 × 内容 = 一种视图。</summary>
/// <param name="Fit">
/// 默认取景。长条折痕图是 100 格 × 1 格（宽高比 100:1），用「适应窗口」会把每格
/// 压到几个像素，等于什么都看不清，所以单独用「铺满宽度」。</param>
public sealed record ViewInfo(ViewKind Kind, ShapeKind Shape, ContentKind Content,
                             string Name, string Hint, FitMode Fit = FitMode.Window)
{
    /// <summary>这个视图能不能上色（原子片分解本来就每面一色，不需要开关）。</summary>
    public bool SupportsColor => Content != ContentKind.Pieces;
}

public static class Views
{
    public static readonly ViewInfo[] All =
    {
        new(ViewKind.Strip,        ShapeKind.Strip,  ContentKind.Crease,  "折痕图", "母版原样：一长条折痕", FitMode.Width),
        new(ViewKind.StripFolded,  ShapeKind.Strip,  ContentKind.Folded,  "成品",   "顺次滚动折叠的结果"),
        new(ViewKind.StripPieces,  ShapeKind.Strip,  ContentKind.Pieces,  "原子片", "顺次折叠每面一色，查漏面/重叠"),

        new(ViewKind.Square,       ShapeKind.Square, ContentKind.Crease,  "折痕图", "铺排成方形后的 CP"),
        new(ViewKind.SquareFolded, ShapeKind.Square, ContentKind.Folded,  "成品",   "树反射折叠，拼出字母"),
        new(ViewKind.SquarePieces, ShapeKind.Square, ContentKind.Pieces,  "原子片", "树反射每面一色，查漏面/重叠"),
    };

    /// <summary>由形状与内容两个维度解析出唯一视图。</summary>
    public static ViewInfo Resolve(ShapeKind shape, ContentKind content)
        => All.First(v => v.Shape == shape && v.Content == content);

    /// <summary>按视图种类取它的元信息。</summary>
    public static ViewInfo Info(ViewKind kind) => All.First(v => v.Kind == kind);

    /// <summary>
    /// 由母版产出全流程的中间结果。<b>只算一次</b>，所有视图从这里取 —— 否则每切一次
    /// 标签页就要重跑铺排与折叠，交互会明显发涩。
    /// </summary>
    public sealed class Document
    {
        public required string MasterText { get; init; }
        public required Model Strip { get; init; }
        public required SquarePlan Plan { get; init; }
        public required Model Square { get; init; }

        /// <summary>长条折叠成品 —— <b>顺次滚动</b>：一张连续的纸沿链一路卷起来。</summary>
        public required FoldedModel StripFolded { get; init; }

        /// <summary>方形折叠成品 —— <b>树反射</b>：11×11 方形沿折痕树逐层反射。</summary>
        public required FoldedModel SquareFolded { get; init; }

        /// <summary>
        /// 母版<b>自带的</b>摆正变换（来自开头的 <c>@</c>/<c>#</c> 标记）。
        /// <para>
        /// ★ <b>不烘焙进几何</b>：几何停在原始方向，摆正体现在右下角的翻转按钮上，
        /// 用户才能在它之上继续调整、「恢复原始方向」也才回得去。
        /// 信息只认母版串（可导出、可保存、可粘贴的唯一事实来源），
        /// 不带标记就是 <see cref="CanvasTransform.Identity"/>。
        /// </para>
        /// </summary>
        public CanvasTransform AutoXform { get; init; } = CanvasTransform.Identity;
    }

    /// <summary>
    /// 跑完整条链路：母版解析 -&gt; 长条模型 -&gt; 方形 CP -&gt; 两种折叠。
    /// <para>
    /// 两条折叠路径<b>不是同一件事</b>，成品也不同，所以都保留、都展示：
    /// 顺次滚动（长条母版的意图）与树反射（KRENIO / PROTEIN 拼出字母的那条路）。
    /// </para>
    /// </summary>
    public static Document Build(string masterText)
    {
        var master = Master.ParseMaster(masterText);
        var strip = Model.StripModel(master);
        var plan = SquareOps.PlanFromMaster(master);
        var square = Model.SquareModel(plan);

        // 长条路径需要峰谷序列：折痕图里已按顺序存着，直接取
        var mv = strip.Folds.Select(f => f.MvType).ToList();
        // ★ 两条路径都产出<b>原始方向</b>，不在这里旋转 —— 摆正改由画布变换承担
        // （见 AutoXform 的说明）。长条与方形的坐标系现已统一为数学坐标（y 向上），
        // 所以同一个画布旋转对两者都成立，切换视图时方向不会跳。
        var stripFolded = FoldOps.FoldStrip(master, mv);
        var squareFolded = FoldOps.FoldSquare(plan);

        return new Document
        {
            MasterText = masterText,
            Strip = strip,
            Plan = plan,
            Square = square,
            StripFolded = stripFolded,
            SquareFolded = squareFolded,
            AutoXform = master.Marks.Canvas,
        };
    }

    /// <summary>把某个视图构建成 <b>SVG 文本</b>。</summary>
    /// <param name="color">
    /// 上色开关。<see cref="ColorMode.Plain"/> 对折痕图是「题面线稿、全黑」，
    /// 对成品是「剪影、纯黑块」；<see cref="ColorMode.Peaks"/> 则是峰谷着色。
    /// 同一个开关在两类图上语义一致：<b>关 = 看结构，关掉颜色只看形状</b>。</param>
    /// <param name="xform">
    /// 画布方向。<b>预览与导出传的是同一个值</b>，所以屏幕上是什么方向，
    /// 存到文件里就是什么方向。</param>
    /// <remarks>
    /// <b>预览一律走 SVG，不走位图</b>（位图在 4 倍以上就糊了，而分辨率这个概念
    /// 只在导出 PNG 时才有意义）。这里用<b>固定</b>的 unit 生成，用户坐标系下每格
    /// 多少像素就定了；显示大小由 <see cref="SvgView"/> 的缩放负责，与这里的 unit 无关。
    /// </remarks>
    public static string BuildSvg(Document doc, ViewKind kind, ColorMode color,
                                  CanvasTransform xform = default)
    {
        bool peak = color == ColorMode.Peaks;

        // 长条是 100 格 × 1 格、宽高比 100:1；给足每格像素，
        // 这样「铺满宽度后放大细看」这个动作才真的能看清折痕端点
        double creaseUnit = kind == ViewKind.Strip ? 24.0 : 48.0;
        double creasePad = kind == ViewKind.Strip ? 14 : creaseUnit;
        double creaseLw = kind == ViewKind.Strip ? 1.2 : 1.6;

        // 成品坐标朝向：长条顺次折叠是数学坐标（y 向上）-> 需翻转；
        //             方形树反射已是屏幕坐标（y 向下）-> 直接用
        var stripF = doc.StripFolded;
        var squareF = doc.SquareFolded;

        var svg = kind switch
        {
            ViewKind.Strip =>
                SvgIO.ModelToSvg(doc.Strip, creaseUnit, creasePad, creaseLw, color: peak),

            ViewKind.Square =>
                SvgIO.ModelToSvg(doc.Square, creaseUnit, creasePad, creaseLw, color: peak),

            ViewKind.StripFolded =>
                SvgIO.FoldedToSvg(stripF.World, stripF.Rank, stripF.MvMap,
                                  unit: 28, pad: 18, color: peak, flipY: true),

            ViewKind.SquareFolded =>
                SvgIO.FoldedToSvg(squareF.World, squareF.Rank, squareF.MvMap,
                                  unit: 28, pad: 18, color: peak, flipY: true),

            // 原子片分解本来就每面一色，不受上色开关影响
            ViewKind.StripPieces =>
                SvgIO.PiecesToSvg(stripF.World, unit: 24, pad: 16, flipY: true),

            ViewKind.SquarePieces =>
                SvgIO.PiecesToSvg(squareF.World, unit: 24, pad: 16, flipY: true),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知视图"),
        };

        // ★ 母版摆正只作用于<b>成品与原子片</b>。折痕图（长条/方形）完全不参与方向：
        // 折痕是数据，转过来看就没法与母版串逐格对照，界面也索性不显示方向面板。
        // 母版的标记仍写进 SVG（<c>data-marks</c>），导出后再读回来还是同一份母版。
        if (Info(kind).Content == ContentKind.Crease)
            return SvgTransform.Stamp(svg, SvgMarks(doc));

        // 先母版摆正、再用户调整。<b>分两步套</b>而不是合成一个变换：
        // CanvasTransform 的 90° 是绕「旋转后画布的中心」定义的，
        // 这 16 种组合<b>对复合不封闭</b> —— 「先转再镜像」与「先镜像再转」
        // 落在不同的地方，合成一个就会平移错位。嵌套两层 <g> 则天然正确。
        return SvgTransform.Apply(SvgTransform.Apply(svg, doc.AutoXform), xform);
    }

    /// <summary>母版自带的方向标记原文（折痕图 SVG 里记作 <c>data-marks</c>）。</summary>
    public static string SvgMarks(Document doc)
        => doc.MasterText.Length == 0 ? "" : Master.ParseMaster(doc.MasterText).Marks.Prefix;

    /// <summary>把某个视图渲染成 <b>PNG 字节</b>，用于导出。</summary>
    /// <param name="scale">
    /// 分辨率倍率。1 = 1 像素/用户单位；2 = 双倍分辨率，打印或放大看更清楚。
    /// <b>只在导出 PNG 时才有意义</b> —— 预览走 SVG，不受这个影响。</param>
    /// <param name="xform">
    /// 画布方向，与 <see cref="BuildSvg"/> 收的是<b>同一个</b>值。
    /// PNG 侧改为对已画好的位图做整体变换（与 SVG 侧结构对称）。</param>
    public static byte[] RenderPng(Document doc, ViewKind kind, ColorMode color, double scale = 2.0,
                                  CanvasTransform xform = default)
    {
        bool peak = color == ColorMode.Peaks;
        var stripF = doc.StripFolded;
        var squareF = doc.SquareFolded;

        // 用户单位 -> 导出像素：直接按倍率放大 unit
        double cu = (kind == ViewKind.Strip ? 24.0 : 48.0) * scale;
        double fu = 28.0 * scale;

        using Raster r = kind switch
        {
            ViewKind.Strip => peak
                ? PngIO.RenderCreaseColor(doc.Strip, cu, cu * 0.58, lw: 1.2 * scale)
                : PngIO.RenderCreaseBlack(doc.Strip, cu, cu * 0.58, lw: 1.2 * scale),

            ViewKind.Square => peak
                ? PngIO.RenderCreaseColor(doc.Square, cu, cu, lw: 1.6 * scale)
                : PngIO.RenderCreaseBlack(doc.Square, cu, cu, lw: 1.6 * scale),

            ViewKind.StripFolded => RenderFold(stripF, fu, flipY: true),
            ViewKind.SquareFolded => RenderFold(squareF, fu, flipY: true),

            ViewKind.StripPieces => PngIO.RenderPieces(stripF.World, fu, fu * 0.6, flipY: true),
            ViewKind.SquarePieces => PngIO.RenderPieces(squareF.World, fu, fu * 0.6, flipY: true),

            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知视图"),
        };

        // 方向变换放在最后统一施加，与 SVG 侧完全对称（同样分两步，不合成一个）。
        // 折痕图只认用户调整 —— PNG 也无处记标记，但折痕本来也不需要摆正。
        if (Info(kind).Content != ContentKind.Crease) r.Transform(doc.AutoXform);
        r.Transform(xform);
        return r.ToPngBytes();

        Raster RenderFold(FoldedModel fm, double unit, bool flipY) =>
            PngIO.RenderFolded(fm.World, unit, unit * 0.65, rank: fm.Rank,
                               mvMap: peak ? fm.MvMap : null,
                               outline: peak ? new Rgb(60, 60, 60) : null,
                               lw: peak ? 1.2 * scale : 0,
                               supersample: scale >= 3 ? 4 : 2, flipY: flipY,
                               forceFill: peak ? null : Rgb.Black);
    }
}
