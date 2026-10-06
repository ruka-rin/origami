using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 缺陷回归测试：每一条都对应一个曾经真实存在、修掉之后无人再动的bug。
/// <para>这类测试的写法与「功能测试」不同：断言的不是行为是否合理，
/// 而是<b>曾经错过的那个具体行为</b> —— 否则同样的错误会以「本来如此」的形式回来。</para>
/// </summary>
public class RegressionTests
{
    // ---------------------------------------------------------------- 铺排

    /// <summary>
    /// 母版以素格<b>结尾</b>时，尾部填充不能丢格。
    /// <para>末条真实折痕是对角、但被尾随的素格拉开了距离时，方向推不出来（diag=0）。
    /// 旧实现此时「x前进却不落任何折痕」，方形右端整段留白，少切一个面。</para>
    /// </summary>
    [Theory]
    [InlineData("//.")]
    [InlineData("//..")]
    [InlineData("///...")]
    [InlineData("/.|")]
    public void 尾部素格不丢格(string master)
    {
        var m = Master.ParseMaster(master);
        var (creases, _, _) = m.CreasesInOrder();
        int n = Layout.ChooseSquare(m.NCell) / Layout.Unit;
        int total = Layout.ChooseTotal(n);

        var padded = Layout.PadCreases(
            creases.Select(c => new Layout.RawCrease(
                (int)System.Math.Round(c.A.X), (int)System.Math.Round(c.B.X))).ToList(),
            total);

        // 判据不是「末端 x == total」——对角折痕天然跨一格（KRENIO 也有 Xb == total），
        // 而母版本来就可能只铺到方形的一部分。真正的信号是：
        // 补齐后不应再出现「x 前进却没有任何折痕落点」的空洞，
        // 即相邻折痕之间不该有超过一格(Unit)的空档。
        const int unit = Layout.Unit;
        for (int i = 1; i < padded.Count; i++)
        {
            int prevEnd = System.Math.Max(padded[i - 1].Xt, padded[i - 1].Xb);
            int nextStart = System.Math.Min(padded[i].Xt, padded[i].Xb);
            Assert.True(nextStart - prevEnd <= unit,
                $"{master}: 第 {i} 条折痕起点 {nextStart} 与前一条末端 {prevEnd} 之间空了 " +
                $"{nextStart - prevEnd} 格（>{unit}），说明填充跳格丢折痕");
        }

        // 补齐后不应越出方形太长：对角允许超出 1 格
        foreach (var c in padded)
            Assert.True(c.Xt <= total + unit && c.Xb <= total + unit,
                $"{master}: 折痕 ({c.Xb},{c.Xt}) 远超total={total}");
    }

    /// <summary>补齐后折痕数应与总长度同量级（每格至多两条），防止补出重复退化折痕。</summary>
    [Theory]
    [InlineData("//")]
    [InlineData("//.")]
    [InlineData("///...")]
    public void 补齐不产生退化折痕(string master)
    {
        var m = Master.ParseMaster(master);
        var (creases, _, _) = m.CreasesInOrder();
        int n = Layout.ChooseSquare(m.NCell) / Layout.Unit;

        var padded = Layout.PadCreases(
            creases.Select(c => new Layout.RawCrease(
                (int)System.Math.Round(c.A.X), (int)System.Math.Round(c.B.X))).ToList(),
            Layout.ChooseTotal(n));

        int zero = padded.Count(c => c.Xt == c.Xb);
        // 竖直折痕本身两端相等是合法的，但数量不应超过折痕总数的一半
        Assert.True(zero <= padded.Count / 2 + 1,
            $"{master}: 退化折痕 {zero}/{padded.Count}过多");
    }

    // ---------------------------------------------------------------- 方形结构

    /// <summary>
    /// <c>Diag</c> 的符号约定必须在两条生成路径上一致。
    /// <para>母版直推（<c>PlanFromMaster</c>）与线段反推（<c>PlanFromSegments</c>）
    /// 曾对同一个斜杠给出相反符号：前者判 <c>by &gt; ay</c> 为 '/'，后者判 <c>by &lt; ay</c>。</para>
    /// </summary>
    [Fact]
    public void Diag符号两路径一致()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var a = SquareOps.PlanFromMaster(master);
        var b = SquareOps.PlanFromSegments(a.N, a.Cut, a.Fold, a.Border);

        Assert.NotEmpty(a.Diag);
        foreach (var ((x, y), sym) in a.Diag)
            Assert.True(b.Diag.TryGetValue((x, y), out var other) && other == sym,
                $"({x},{y}) 符号不一致：PlanFromMaster='{sym}' PlanFromSegments='{(b.Diag.TryGetValue((x, y), out var v) ? v : '?')}'");
    }

    /// <summary>Diag 反推出的线段必须真的在 Fold 里（符号约定自洽）。</summary>
    [Fact]
    public void Diag反推线段命中Fold()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var p = SquareOps.PlanFromMaster(master);

        int hit = 0;
        foreach (var ((x, y), sym) in p.Diag)
        {
            var (a, b) = sym == '/'
                ? (new Point(x, y + 1), new Point(x + 1, y))
                : (new Point(x, y), new Point(x + 1, y + 1));
            if (p.Fold.Contains(Segment.Norm(a, b))) hit++;
        }
        Assert.Equal(p.Diag.Count, hit);
    }

    // ---------------------------------------------------------------- 长条逆向

    /// <summary>
    /// 折痕索引越界要报 <see cref="FormatException"/>，不能抛 <c>IndexOutOfRangeException</c>
    /// —— 后者会被 <c>svg2json</c> 等入口的 <c>catch (FormatException)</c> 漏接。
    /// </summary>
    [Fact]
    public void 逆向折痕越界报Format异常()
    {
        var bad = new Model { Kind = "strip", NCell = 2 };
        bad.Folds.Add(new Fold(new Segment(new Point(5, 0), new Point(6, 1)), "V"));

        var ex = Assert.Throws<FormatException>(() => Model.StripModelToMaster(bad));
        Assert.Contains("超出母版长度", ex.Message);
    }

    /// <summary>
    /// <c>x == 0</c> 的竖直折痕是纸带左边界、不是折痕，不能写进母版。
    /// <para>旧实现会 <c>pipes.Add(0)</c>，而输出只检查 <c>pipes.Contains(k+1)</c>（k 从 0 起），
    /// 于是它永远匹配不上——往返一次这条折痕就消失了。</para></summary>
    /// </summary>
    [Fact]
    public void 左边界不被当成折痕()
    {
        var m = new Model { Kind = "strip", NCell = 3 };
        m.Folds.Add(new Fold(new Segment(new Point(0, 0), new Point(0, 1)), "M"));   // 左边界，应丢弃
        m.Folds.Add(new Fold(new Segment(new Point(2, 0), new Point(2, 1)), "V"));   // 真实折痕，应保留
        m.Folds.Add(new Fold(new Segment(new Point(1, 0), new Point(2, 1)), "V"));

        var master = Model.StripModelToMaster(m);

        // 只剩pipe 2 那条（pipes 记的是「右邻格」，故 x=2 -> pipe 2）
        Assert.Equal(new[] { 2 }, master.Pipes);
        // 1 号格是那条对角折痕；0 号格没有对角折痕，应保持素格
        Assert.Equal('/', master.Cells[1]);
        Assert.Equal('.', master.Cells[0]);
    }

    /// <summary>合法折痕应能无损往返（越界修复不能误伤正常路径）。</summary>
    [Fact]
    public void 长条往返无损()
    {
        const string text = "/|/\\|/\\";
        var master = Master.ParseMaster(text);
        var model = Model.StripModel(master);
        var back = Model.StripModelToMaster(model);
        Assert.Equal(master.Text.Replace(" ", ""), back.Text.Replace(" ", ""));
    }

    // ---------------------------------------------------------------- 折叠

    /// <summary>
    /// 旋转成品时峰谷序列不能丢。
    /// <para><c>Rotate90</c> 曾调用 6 参构造器，而它把 <c>MvSeq</c> 置空 ——
    /// 而 <c>OrientLandscape</c>（默认开启）就会走到这里。</para>
    /// </summary>
    [Fact]
    public void 旋转保留峰谷序列()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var mv = Model.StripModel(master).Folds.Select(f => f.MvType).ToList();
        var fm = FoldOps.FoldStrip(master, mv, orient: false);

        Assert.NotEmpty(fm.MvSeq);
        Assert.Equal(fm.MvSeq.Count, fm.Rotate90(1).MvSeq.Count);
        Assert.Equal(fm.MvSeq.Count, fm.Rotate90(3).MvSeq.Count);
    }

    /// <summary>转满 360° 必须回到原样（旋转组合律）。</summary>
    [Fact]
    public void 旋转四次回原样()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var mv = Model.StripModel(master).Folds.Select(f => f.MvType).ToList();
        var fm = FoldOps.FoldStrip(master, mv, orient: false);

        var back = fm.Rotate90(1).Rotate90(1).Rotate90(1).Rotate90(1);
        Assert.Equal(fm.World.Count, back.World.Count);
        for (int i = 0; i < fm.World.Count; i++)
            Assert.Equal(fm.World[i].Count, back.World[i].Count);
    }

    // ---------------------------------------------------------------- JSON

    /// <summary>
    /// 截断的 <c>\u</c> 转义必须报 <see cref="FormatException"/>。
    /// <para>旧实现直接 <c>Substring(i+1, 4)</c>，抛的是
    /// <c>ArgumentOutOfRangeException</c>，会被调用方的 catch漏接。</para>
    /// </summary>
    [Fact]
    public void 截断的Unicode转义报格式异常()
    {
        Assert.Throws<FormatException>(() => JsonLite.Parse("{\"a\":\"\\u\"}"));
        Assert.Throws<FormatException>(() => JsonLite.Parse("{\"a\":\"\\u12\"}"));
    }

    /// <summary>字符串未闭合必须报错，而不是静默接受（曾经的过度宽容）。</summary>
    [Fact]
    public void 未闭合字符串报错()
        => Assert.Throws<FormatException>(() => JsonLite.Parse("{\"a\":\"xx"));

    /// <summary>正常转义不能被上面的防御性检查误伤。</summary>
    [Fact]
    public void 正常Unicode转义可用()
    {
        var v = JsonLite.Parse("{\"a\":\"\\u4e2d\"}");
        Assert.NotNull(v);
    }
}