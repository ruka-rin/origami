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

    /// <summary>
    /// 成品（按母版开头 <c>@</c>/<c>#</c> 标记摆正后）与旧实现的<b>形状重合度</b>必须 ≥ 0.99。
    /// <para>
    /// IoU 对「面片划分方式不同」「纸带高度差 2 倍」「坐标方向相反」都免疫 ——
    /// 只关心最终纸张覆盖的形状。标记少标、多标或算错，这里立刻掉到 0.6 以下。
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
        var master = Master.ParseMaster(FoldFont.TextToMaster(ch));
        var fm = FoldOps.FoldStrip(master, MasterOps.DefaultMv(master.Pipes.Count + master.NCell));
        if (fm.World.Count == 0) return;

        double iou = ShapeMatch.Match(fm.World, master.Marks.Canvas, ch);
        Assert.True(iou >= 0.99,
            $"「{ch}」与旧实现的形状重合度仅 {iou:0.###}（应 ≥ 0.99）；标记 {master.Marks.Prefix}");
    }

    /// <summary>
    /// <b>方形成品</b>必须与长条成品是同一个形状：两条路径（树反射 / 顺次滚动）
    /// 折的是<b>同一串折痕</b>，形状对不上就说明方形那条路有问题。
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
    [InlineData("KRENIO")] [InlineData("PROTEIN")] [InlineData("SOS")]
    [InlineData("68S")] [InlineData("123456")] [InlineData("HELLO")]
    public void 方形成品与长条成品形状一致(string ch)
    {
        var master = Master.ParseMaster(FoldFont.TextToMaster(ch));
        var mv = Model.StripModel(master).Folds.Select(f => f.MvType).ToList();

        var strip = FoldOps.FoldStrip(master, mv);
        var square = FoldOps.FoldSquare(SquareOps.PlanFromMaster(master));

        if (strip.World.Count == 0 || square.World.Count == 0) return;

        // 两条路径共用同一个画布变换，比的时候也就一起摆正
        double iou = ShapeMatch.IoU(ShapeMatch.Mask(ShapeMatch.View(strip.World, master.Marks.Canvas), false),
                                    ShapeMatch.Mask(ShapeMatch.View(square.World, master.Marks.Canvas), false));

        Assert.True(iou >= 0.99,
            $"「{ch}」方形成品与长条成品形状不一致（IoU {iou:0.###}）—— 方形那条折叠路径有问题");
    }

    /// <summary>
    /// 带 <c>#</c> 的字模（编码含 x=0 竖折痕）：去掉标记就应当对不上。
    /// <para>★ 断言的是<b>去掉标记会错</b>，而不是「代码调了某个镜像函数」——
    /// 换一种摆正实现只要结果仍对，这条就不用改。</para>
    /// </summary>
    [Theory]
    [InlineData("E")] [InlineData("F")] [InlineData("G")] [InlineData("L")]
    [InlineData("6")] [InlineData("1")] [InlineData("5")] [InlineData("68S")]
    [InlineData("123456")]
    public void 去掉翻转标记就与旧实现不符(string ch)
    {
        if (ShapeMatch.Legacy(ch) is null) return;          // 基准未覆盖，无从比较

        var master = Master.ParseMaster(FoldFont.TextToMaster(ch));
        Assert.True(master.Marks.Flip, $"「{ch}」的母版应当带 # 标记，实得 {master.Marks.Prefix}");

        var fm = FoldOps.FoldStrip(master, MasterOps.DefaultMv(master.Pipes.Count + master.NCell));
        double ok = ShapeMatch.Match(fm.World, master.Marks.Canvas, ch);

        // ★ <c>#</c> 是折痕层面的翻转，去掉它要<b>重新解析母版</b>，
        // 而不是改画布 —— 画布上根本没有翻转这一步（翻转已经落在折痕里了）。
        var noFlip = Master.ParseMaster(
            new string(Master.RotateMark, master.Marks.QuarterTurns) + master.RawBody);
        double bad = ShapeMatch.Match(
            FoldOps.FoldStrip(noFlip, MasterOps.DefaultMv(noFlip.Pipes.Count + noFlip.NCell)).World,
            noFlip.Marks.Canvas, ch);

        var bare = Master.ParseMaster(master.RawBody);
        double bad2 = ShapeMatch.Match(
            FoldOps.FoldStrip(bare, MasterOps.DefaultMv(bare.Pipes.Count + bare.NCell)).World,
            bare.Marks.Canvas, ch);

        Assert.True(ok >= 0.99, $"「{ch}」带标记后仍对不上（IoU {ok:0.###}）");
        Assert.True(bad < 0.95 || bad2 < 0.95,
            $"「{ch}」去掉标记居然也对得上（去# {bad:0.###} / 全去 {bad2:0.###}）—— 这两条断言失去意义");
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

        var m = Master.ParseMaster(FoldFont.TextToMaster(ch));
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

        var m = Master.ParseMaster(FoldFont.TextToMaster(ch));
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
    /// 旧实现所有字母都是 <b>6×10 竖立</b>。本项目靠母版开头的 <c>@</c> 转到位 ——
    /// 标记少转一格这里立刻露馅：IoU 会把宽高各自归一化，看不出相差 90° 的旋转。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("A")] [InlineData("B")] [InlineData("L")] [InlineData("O")]
    [InlineData("S")] [InlineData("V")] [InlineData("X")] [InlineData("M")]
    [InlineData("1")] [InlineData("8")]
    public void 单字母朝向与旧实现一致(string ch)
    {
        if (!Legacy.TryGetValue(ch, out var want)) return;

        var m = Master.ParseMaster(FoldFont.TextToMaster(ch));
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));

        var ours = ShapeMatch.View(fm.World, m.Marks.Canvas);
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
            var m = Master.ParseMaster(FoldFont.TextToMaster(ch));
            var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(m.Pipes.Count + m.NCell));
            if (fm.World.Count == 0) continue;

            var ours = ShapeMatch.View(fm.World, m.Marks.Canvas);
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
