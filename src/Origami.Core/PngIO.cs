namespace Origami.Core;

/// <summary>
/// 数据模型 -&gt; PNG。三种渲染：
/// <list type="bullet">
/// <item><see cref="RenderCreaseBlack"/> —— <b>纯黑折痕图</b>（题面）：所有线一律黑色。</item>
/// <item><see cref="RenderCreaseColor"/> —— <b>红蓝峰谷 + 绿色剪切线</b>（CP 图）。</item>
/// <item><see cref="RenderFolded"/> —— 折叠成品，按层序上色（红=峰面 蓝=谷面）。</item>
/// </list>
/// <para>★ 线宽层级：红/蓝折痕 <c>lw</c>（最细）、绿剪切线 <c>lw+1</c>、黑边框 <c>lw+2</c>。</para>
/// <para>★ 坐标系与 <see cref="SvgIO"/> 一致：<c>strip</c> 数学坐标 y 向上 -&gt; 渲染翻转；
/// <c>square</c> 屏幕坐标 y 向下 -&gt; 直接用。所有渲染<b>都不画标题文字</b>。</para>
/// <para>底层是 <see cref="Raster"/>（ImageSharp）：抗锯齿、图元绘制与 PNG 编码都交给
/// 成熟库，本类只负责坐标换算与绘制顺序。</para>
/// </summary>
public static class PngIO
{
    /// <summary>折痕模型坐标 -&gt; 画布像素坐标。</summary>
    static (double X, double Y) ToCanvas(Point p, double unit, double pad, bool flipY, int hCells)
        => (pad + p.X * unit, pad + (flipY ? hCells - p.Y : p.Y) * unit);

    static (int W, int H) CanvasSize(int wCells, int hCells, double unit, double pad)
        => ((int)(unit * wCells + 2 * pad), (int)(unit * hCells + 2 * pad));

    /// <summary>纯黑折痕图（题面）。所有线一律黑色，不画任何文字。</summary>
    /// <remarks>返回内存中的位图；编码与写盘由调用方（CLI）负责。</remarks>
    public static Raster RenderCreaseBlack(Model model,
                                           double unit = 48.0, double pad = 18.0, double lw = 2.0)
    {
        bool strip = model.Kind == "strip";
        int n = strip ? model.NCell : model.N;
        int hCells = strip ? 1 : n;
        var (W, H) = CanvasSize(n, hCells, unit, pad);

        var img = Raster.Create(W, H, Rgb.White);
        int w = (int)lw;

        void Draw(IEnumerable<Segment> segs)
        {
            foreach (var s in segs)
            {
                var (x1, y1) = ToCanvas(s.A, unit, pad, strip, hCells);
                var (x2, y2) = ToCanvas(s.B, unit, pad, strip, hCells);
                img.DrawLine(x1, y1, x2, y2, Rgb.Black, w);
            }
        }

        Draw(model.Border);
        Draw(model.Cut);
        Draw(model.Folds.Select(f => f.Seg));

        return img;
    }

    /// <summary>红蓝峰谷 + 绿剪切线的彩色折痕图（CP 图），不画任何文字。</summary>
    /// <remarks>返回内存中的位图；编码与写盘由调用方（CLI）负责。</remarks>
    public static Raster RenderCreaseColor(Model model,
                                           double unit = 48.0, double pad = 18.0, double lw = 2.0)
    {
        bool strip = model.Kind == "strip";
        int n = strip ? model.NCell : model.N;
        int hCells = strip ? 1 : n;
        var (W, H) = CanvasSize(n, hCells, unit, pad);

        var img = Raster.Create(W, H, Rgb.White);

        // ★ 折痕最细，剪切线 +1，边框 +2
        int wFold = Math.Max(1, (int)lw);
        int wCut = wFold + 1;
        int wBorder = wFold + 2;

        void Draw(IEnumerable<Segment> segs, Rgb col, int width)
        {
            foreach (var s in segs)
            {
                var (x1, y1) = ToCanvas(s.A, unit, pad, strip, hCells);
                var (x2, y2) = ToCanvas(s.B, unit, pad, strip, hCells);
                img.DrawLine(x1, y1, x2, y2, col, width);
            }
        }

        // 顺序：先粗后细，避免细线被覆盖
        Draw(model.Border, Rgb.Border, wBorder);
        Draw(model.Cut, Rgb.Green, wCut);
        foreach (var f in model.Folds)
            Draw(new[] { f.Seg }, f.MvType == Mv.Mountain ? Rgb.Red : Rgb.Blue, wFold);

        return img;
    }

    /// <summary>
    /// 折叠成品。
    /// <para>★ <paramref name="flipY"/> 决定输入坐标朝向（唯一能得到正立成品的组合）：
    /// 长条顺次折叠 -&gt; 数学坐标 y 向上 -&gt; <c>true</c>；方形树反射 -&gt; 屏幕坐标 y 向下 -&gt;
    /// <c>false</c>。两者混用都会让成品上下颠倒。</para>
    /// <para><paramref name="supersample"/> 默认 4×：成品边缘是大量斜线，直接画会有明显锯齿。</para>
    /// </summary>
    /// <param name="outline">描边颜色；<c>null</c> 表示不描边。</param>
    /// <param name="lw">描边线宽（单位与折痕格相同，会随超采样放大）。</param>
    /// <param name="supersample">超采样倍数：成品边缘全是斜线，直接画会有明显锯齿。</param>
    /// <param name="flipY">输入坐标朝向。长条顺次折叠是数学坐标（y 向上），需 <c>true</c>；
    /// 方形树反射是屏幕坐标（y 向下），需 <c>false</c>。混用会让成品上下颠倒。</param>
    /// <param name="forceFill">忽略峰谷、一律用此颜色填充。用于<b>剪影</b> ——
    /// 只关心最终形状，不关心层序与峰谷，设 <c>outline:null, lw:0</c> 即可得到纯黑块。</param>
    public static Raster RenderFolded(IReadOnlyList<List<Point>> worldPolys,
                                      double unit = 22.0, double pad = 16.0,
                                      IReadOnlyList<int>? rank = null,
                                      IReadOnlyDictionary<int, string>? mvMap = null,
                                      IReadOnlyList<string>? mvSeq = null,
                                      Rgb? outline = null, double lw = 1.2,
                                      int supersample = 4, bool flipY = true,
                                      Rgb? forceFill = null)
    {
        var polys = worldPolys.Where(p => Geom.PolyArea(p) > 1e-9).ToList();
        if (polys.Count == 0) throw new InvalidOperationException("没有可渲染的面片");

        // lw <= 0 视为「不描边」，因此 outline 的默认值不能直接用 ?? 兜住
        Rgb? oc = lw > 0 ? outline ?? new Rgb(60, 60, 60) : null;

        int ss = Math.Max(1, supersample);
        var (screen, _, (W0, H0)) = Geom.ToScreen(polys, unit * ss, pad * ss, flipY);
        var img = Raster.Create(W0, H0, Rgb.White);

        var order = Enumerable.Range(0, worldPolys.Count).ToList();
        if (rank is not null) order = order.OrderBy(i => rank[i]).ToList();

        // 面 i 用它后面那条折痕的峰谷决定填色
        string? FaceMv(int i)
        {
            if (mvMap is { Count: > 0 }) return mvMap.TryGetValue(i, out var v) ? v : null;
            if (mvSeq is { Count: > 0 }) return i < mvSeq.Count ? mvSeq[i] : mvSeq[^1];
            return null;
        }

        double w = Math.Max(1, lw * ss);
        for (int k = 0; k < order.Count; k++)
        {
            int i = order[k];
            if (Geom.PolyArea(worldPolys[i]) <= 1e-9) continue;

            Rgb? fill = forceFill;
            if (fill is null)
            {
                var m = FaceMv(i);
                fill = m == Mv.Mountain ? Rgb.Red : m == Mv.Valley ? Rgb.Blue : null;
            }
            img.FillPolygon(screen[i], fill, oc, w);
        }

        if (ss > 1) img.Resize(Math.Max(1, W0 / ss), Math.Max(1, H0 / ss));
        return img;
    }

    /// <summary>原子片分解图 —— 每个面片一种颜色，用于检查是否无空洞无重叠。</summary>
    public static Raster RenderPieces(IReadOnlyList<List<Point>> worldPolys,
                                      double unit = 18.0, double pad = 14.0,
                                      int seed = 7, bool flipY = true)
    {
        var polys = worldPolys.Where(p => Geom.PolyArea(p) > 1e-9).ToList();
        if (polys.Count == 0) throw new InvalidOperationException("没有可渲染的面片");
        var (screen, _, (W, H)) = Geom.ToScreen(polys, unit, pad, flipY);
        var img = Raster.Create(W, H, Rgb.White);

        var rnd = new Random(seed);
        var cols = new List<Rgb>();
        for (int i = 0; i < polys.Count + 4; i++)
            cols.Add(new Rgb((byte)rnd.Next(90, 240), (byte)rnd.Next(90, 240), (byte)rnd.Next(90, 240)));

        int k = 0;
        for (int i = 0; i < worldPolys.Count; i++)
        {
            if (Geom.PolyArea(worldPolys[i]) <= 1e-9) continue;
            img.FillPolygon(screen[i], cols[k % cols.Count], new Rgb(50, 50, 50), 1);
            k++;
        }
        return img;
    }
}
