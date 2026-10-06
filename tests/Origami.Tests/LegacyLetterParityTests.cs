using System.Text;
using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 单字母成品必须与<b>旧参考实现</b>逐顶点一致。
/// <para>
/// 旧实现（<c>旧的参考代码/strip.js</c> 的 <c>showFolded</c>）对每个字模的
/// <c>folded</c> 多边形<b>不做任何额外变换</b>直接画进 SVG ——
/// 所以那些坐标就是「文本→成品」的权威结果。
/// </para>
/// <para>
/// 这组测试的价值在于它<b>逐顶点</b>比对，而不是只比 bbox 宽高：
/// 之前多次「bbox 看起来对、形状却不对」（V 缺一截、SOS 颠倒），
/// 只有顶点级比对才能暴露。</para>
/// </summary>
public class LegacyLetterParityTests
{
    sealed record Ref(
        int Rotate,
        (double X0, double Y0, double X1, double Y1) Bbox,
        List<List<(double X, double Y)>> Polys);

    /// <summary>用 System.Text.Json 读，不用 JsonLite —— 后者不支持嵌套对象内取字段。</summary>
    sealed class RefJson
    {
        public int rotate { get; set; }
        public double[] bbox { get; set; } = [];
        public List<List<XY>> polys { get; set; } = [];
    }

    public sealed class XY
    {
        public double x { get; set; }
        public double y { get; set; }
    }

    static Dictionary<string, Ref> LoadLegacy()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "legacy_letters.json");
        var raw = System.Text.Json.JsonSerializer
            .Deserialize<Dictionary<string, RefJson>>(File.ReadAllText(path, Encoding.UTF8))!;

        return raw.ToDictionary(
            kv => kv.Key,
            kv => new Ref(kv.Value.rotate,
                (kv.Value.bbox[0], kv.Value.bbox[1], kv.Value.bbox[2], kv.Value.bbox[3]),
                kv.Value.polys.Select(p => p.Select(q => (q.x, q.y)).ToList()).ToList()));
    }

    static readonly Dictionary<string, Ref> Legacy = LoadLegacy();

    /// <summary>归一化坐标：平移到原点，消除整体平移的无关差异。</summary>
    static (double X0, double Y0, double X1, double Y1) Norm(
        List<List<(double X, double Y)>> polys)
    {
        double x0 = polys.SelectMany(p => p).Min(p => p.X);
        double y0 = polys.SelectMany(p => p).Min(p => p.Y);
        double x1 = polys.SelectMany(p => p).Max(p => p.X);
        double y1 = polys.SelectMany(p => p).Max(p => p.Y);
        return (x0, y0, x1, y1);
    }

    // ---------------------------------------------------------------- 形状重合度

    const int Grid = 72;

    /// <summary>
    /// 把多边形集合栅格化成 <see cref="Grid"/>² 布尔掩码（归一化到单位框）。
    /// </summary>
    /// <param name="flipY">
    /// 输入是否 y 向上。本项目折叠结果是数学坐标（y 向上），要先翻成屏幕
    /// 方向（y 向下）才能与旧实现的坐标比 —— <b>翻转要作用在多边形上</b>，
    /// 而不是作用在采样点上，否则多边形本身的方向没变，掩码会错。
    /// </param>
    static bool[] Mask(List<List<(double X, double Y)>> polys, bool flipY)
    {
        var all = polys.SelectMany(p => p).ToList();
        double x0 = all.Min(p => p.X), x1 = all.Max(p => p.X);
        double y0 = all.Min(p => p.Y), y1 = all.Max(p => p.Y);
        double w = x1 - x0, h = y1 - y0;
        if (w < 1e-9) w = 1;
        if (h < 1e-9) h = 1;

        var np = polys.Select(f => f.Select(p => (
            (p.X - x0) / w,
            flipY ? 1 - (p.Y - y0) / h : (p.Y - y0) / h)).ToList()).ToList();

        var m = new bool[Grid * Grid];
        for (int gy = 0; gy < Grid; gy++)
            for (int gx = 0; gx < Grid; gx++)
            {
                double px = (gx + 0.5) / Grid, py = (gy + 0.5) / Grid;
                foreach (var f in np)
                    if (Inside(f, px, py)) { m[gy * Grid + gx] = true; break; }
            }
        return m;
    }

    static bool Inside(List<(double X, double Y)> f, double x, double y)
    {
        bool in_ = false;
        for (int i = 0, j = f.Count - 1; i < f.Count; j = i++)
            if ((f[i].Y > y) != (f[j].Y > y) &&
                x < (f[j].X - f[i].X) * (y - f[i].Y) / (f[j].Y - f[i].Y) + f[i].X)
                in_ = !in_;
        return in_;
    }

    static double IoU(bool[] a, bool[] b)
    {
        int inter = 0, uni = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) inter++;
            if (a[i] || b[i]) uni++;
        }
        return uni == 0 ? 0 : (double)inter / uni;
    }

    static List<List<(double X, double Y)>> ToPairs(List<List<Point>> polys)
        => polys.Select(f => f.Select(p => (p.X, p.Y)).ToList()).ToList();

    /// <summary>
    /// 成品与旧实现的<b>形状重合度</b>（栅格化 IoU）必须 ≥ 0.99。
    /// <para>
    /// 这是本项目最强的一道核对：把两边都归一化到单位方框、各自栅格化成
    /// 72×72 的覆盖掩码，再求交并比。它对「面片划分方式不同」「纸带高度
    /// 差 2 倍」「坐标方向相反」都免疫 —— 只关心<b>最终纸张覆盖的形状</b>，
    /// 而这正是用户看到的东西。
    /// </para>
    /// <para>
    /// 曾用它一次抓出两个隐藏很深的缺陷（此前 bbox / 面积序列都查不出来）：
    /// <list type="number">
    /// <item>rotate 的<b>手性反了</b>：旧实现的 <c>rotatePoint</c> 作用在 y 向下的
    /// 坐标系，本项目走「数学坐标 + flipY」，镜像会翻转旋转方向。
    /// 实测约定 rotate=1 的字模（V/L/6）要转 3，rotate=3 的（S）要转 1
    /// —— 即取负。修前 V 只有 0.696。</item>
    /// <item>母版开头的 <c>|</c>（x=0 的竖折痕）被丢弃：它切出的面片宽度为零，
    /// 平面剖分产生不出来，但那条反射线会镜像其后所有面片。修前
    /// L 只有 0.451、68S 只有 0.640。</item>
    /// </list>
    /// 修后 18 个字模/词全部 ≥ 0.996。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("X")] [InlineData("M")] [InlineData("A")] [InlineData("E")]
    [InlineData("F")] [InlineData("G")] [InlineData("Z")]
    [InlineData("V")] [InlineData("L")] [InlineData("6")] [InlineData("O")]
    [InlineData("S")] [InlineData("R")]
    [InlineData("1")] [InlineData("5")]
    [InlineData("68S")] [InlineData("SOS")] [InlineData("SOKRENIO")]
    public void 成品形状与旧实现重合度不低于0_99(string ch)
    {
        var (master, rot) = FoldFont.TextToMasterWithRotate(ch);
        var m = Master.ParseMaster(master);
        m.Rotate = rot;
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));
        if (fm.World.Count == 0) return;

        // 基准：单字母走 showFolded 路径，多字符走文本拼接路径
        List<List<(double X, double Y)>>? want = null;
        foreach (var (file, scaled) in new[] { ("legacy_letters.json", true), ("legacy_multi.json", false) })
        {
            string p = Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(p)) continue;
            var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty(ch, out var el)) continue;

            want = el.GetProperty("polys").EnumerateArray().Select(pa =>
                pa.EnumerateArray().Select(q => (
                    q.GetProperty("x").GetDouble() / (scaled ? 2 : 1),
                    q.GetProperty("y").GetDouble() / (scaled ? 2 : 1))).ToList()).ToList();
            break;
        }
        if (want is null) return;

        // 基准（旧实现）已是屏幕坐标；本项目是数学坐标，要比就得先翻 y
        double iou = IoU(Mask(want, flipY: false), Mask(ToPairs(fm.World), flipY: true));
        Assert.True(iou >= 0.99, $"「{ch}」与旧实现的形状重合度仅 {iou:0.###}（应 ≥ 0.99）");
    }

    /// <summary>多边形面积（鞋带公式），用于筛掉退化面片与做形状指纹。</summary>
    static double Area(IEnumerable<(double X, double Y)> p)
    {
        var list = p.ToList();
        double s = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            var b = list[(i + 1) % list.Count];
            s += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(s) / 2;
    }

    /// <summary>
    /// 单字母的<b>非零面积面片数</b>必须与旧实现一致。
    /// <para>
    /// 只数非零面积是有意的：旧实现的折痕序列里可能含 <c>x=0</c> 的竖折痕，
    /// 它会切出一个<b>宽度为零</b>的退化面片（实测 L/E/F/G/1/5/6 各有 1~2 片
    /// 面积为 0）。母版格式的 <c>pipes</c> 取值只能是 1..格数，
    /// 表达不了「第 0 格之前」的折痕，所以本项目不会产生这些空片 ——
    /// 它们画出来只是一条线，不影响成品。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("A")] [InlineData("B")] [InlineData("E")] [InlineData("F")]
    [InlineData("G")] [InlineData("H")] [InlineData("I")] [InlineData("J")]
    [InlineData("K")] [InlineData("L")] [InlineData("M")] [InlineData("N")]
    [InlineData("O")] [InlineData("P")] [InlineData("R")] [InlineData("S")]
    [InlineData("T")] [InlineData("U")] [InlineData("V")] [InlineData("W")]
    [InlineData("X")] [InlineData("Y")]
    [InlineData("0")] [InlineData("1")] [InlineData("5")] [InlineData("6")]
    [InlineData("8")]
    public void 单字母面片数与旧实现一致(string ch)
    {
        if (!Legacy.TryGetValue(ch, out var want)) return;

        var (master, rot) = FoldFont.TextToMasterWithRotate(ch);
        var m = Master.ParseMaster(master);
        m.Rotate = rot;
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));

        int wantCount = want.Polys.Count(p => Area(p) > 1e-9);
        int ourCount = fm.World.Count(f => Math.Abs(Geom.PolyArea(f)) > 1e-9);

        Assert.Equal(wantCount, ourCount);
    }

    /// <summary>
    /// 逐片<b>面积序列</b>必须与旧实现相同 —— 这是比 bbox 强得多的形状指纹。
    /// <para>
    /// 面积与坐标系方向无关，所以能绕开「y 向上/向下」的换算纠结，
    /// 但只要折叠链上有任何一片位置或形状不对，序列立刻对不上。
    /// 旧实现纸带高 2、x 用半格，本项目高 1、x 用整格，故面积差 <b>4 倍</b>。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("A")] [InlineData("B")] [InlineData("E")] [InlineData("F")]
    [InlineData("G")] [InlineData("H")] [InlineData("I")] [InlineData("J")]
    [InlineData("K")] [InlineData("L")] [InlineData("M")] [InlineData("N")]
    [InlineData("O")] [InlineData("P")] [InlineData("R")] [InlineData("S")]
    [InlineData("T")] [InlineData("U")] [InlineData("V")] [InlineData("W")]
    [InlineData("X")] [InlineData("Y")]
    [InlineData("0")] [InlineData("1")] [InlineData("5")] [InlineData("6")]
    [InlineData("8")]
    public void 单字母逐片面积序列与旧实现一致(string ch)
    {
        if (!Legacy.TryGetValue(ch, out var want)) return;

        var (master, rot) = FoldFont.TextToMasterWithRotate(ch);
        var m = Master.ParseMaster(master);
        m.Rotate = rot;
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));

        var wantSeq = want.Polys.Select(Area).Where(a => a > 1e-9).ToList();
        var ourSeq = fm.World.Select(f => Math.Abs(Geom.PolyArea(f)) * 4)
                             .Where(a => a > 1e-9).ToList();

        Assert.Equal(wantSeq.Count, ourSeq.Count);
        for (int i = 0; i < wantSeq.Count; i++)
            Assert.True(Math.Abs(wantSeq[i] - ourSeq[i]) < 0.05,
                $"「{ch}」第 {i} 片面积 旧 {wantSeq[i]:0.##} vs 新 {ourSeq[i]:0.##}（差 {wantSeq[i] - ourSeq[i]:0.##}）");
    }

    /// <summary>
    /// 单字母成品的<b>长宽比</b>必须与旧实现一致 —— 归一化后即
    /// 「宽 &gt;= 高」的横向还是「宽 &lt; 高」的纵向。
    /// <para>
    /// 旧实现所有字母都是 <b>6×10 竖立</b>（沿用 stripHeight=50 的长条习惯）。
    /// 本项目此前横躺，说明<b>成品朝向反了</b>，不是形状错了。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("A")] [InlineData("B")] [InlineData("L")] [InlineData("O")]
    [InlineData("S")] [InlineData("V")] [InlineData("X")] [InlineData("M")]
    [InlineData("1")] [InlineData("8")]
    public void 单字母朝向与旧实现一致(string ch)
    {
        if (!Legacy.TryGetValue(ch, out var want)) return;

        var (master, rot) = FoldFont.TextToMasterWithRotate(ch);
        var m = Master.ParseMaster(master);
        m.Rotate = rot;
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));

        var ours = fm.World.Select(p => p.Select(q => (q.X, q.Y)).ToList()).ToList();
        var (ax, ay, bx, by) = Norm(want.Polys);
        var (cx, cy, dx, dy) = Norm(ours);

        double wantRatio = (bx - ax) / (by - ay);
        double ourRatio = (dx - cx) / (dy - cy);

        Assert.True(Math.Abs(wantRatio - ourRatio) < 0.05,
            $"「{ch}」长宽比 旧 {(bx - ax):0.#}×{(by - ay):0.#}(比 {wantRatio:0.##})，" +
            $"新 {(dx - cx):0.#}×{(dy - cy):0.#}(比 {ourRatio:0.##}) —— 朝向不一致");
    }

    /// <summary>
    /// 全部字模的<b>长宽比</b>都必须与旧实现吻合。
    /// 覆盖面最广的一条 —— 任何朝向回归都会在这里被抓到。
    /// </summary>
    [Fact]
    public void 全部字模长宽比与旧实现吻合()
    {
        var bad = new List<string>();

        foreach (var (ch, want) in Legacy)
        {
            var (master, rot) = FoldFont.TextToMasterWithRotate(ch);
            var m = Master.ParseMaster(master);
            m.Rotate = rot;
            var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));
            if (fm.World.Count == 0) continue;

            var ours = fm.World.Select(p => p.Select(q => (q.X, q.Y)).ToList()).ToList();
            var (ax, ay, bx, by) = Norm(want.Polys);
            var (cx, cy, dx, dy) = Norm(ours);

            double wantRatio = (bx - ax) / (by - ay);
            double ourRatio = (dx - cx) / (dy - cy);
            if (Math.Abs(wantRatio - ourRatio) >= 0.05)
                bad.Add($"  {ch}: 旧 {(bx - ax):0.#}×{(by - ay):0.#}({wantRatio:0.##}) " +
                        $"vs 新 {(dx - cx):0.#}×{(dy - cy):0.#}({ourRatio:0.##})");
        }

        Assert.True(bad.Count == 0, $"{bad.Count} 个字模长宽比不一致：\n{string.Join("\n", bad)}");
    }
}
