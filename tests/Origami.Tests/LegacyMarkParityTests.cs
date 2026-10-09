using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 逐字模钉住母版开头<b>该写什么标记</b>。
/// <para>
/// ★ 数值是测出来的，不是从编码推的：旧实现的 <c>@</c> 计数只决定字母自身站不站得起来，
/// 「这份母版要转几格、翻不翻」还要叠上 x=0 那条竖折痕的影响。
/// 测定办法是对 4 种旋转 × 2 种翻转逐字模比 IoU 取最高者；
/// 对称字形（X/M/O/8/0/V）有两组并列，取与字模约定一致的那组。
/// </para>
/// </summary>
public class LegacyMarkParityTests
{
    /// <summary>
    /// 每个字符该带的标记。<c>""</c> = 成品本来就是正的。
    /// <para>规律：<c>#</c> ⟺ 编码含 x=0 竖折痕；带 <c>#</c> 时 <c>@</c> 个数 = rotate+2。</para>
    /// </summary>
    [Theory]
    // 无标记：成品本来就是正的
    [InlineData("A", "")] [InlineData("B", "")] [InlineData("D", "")] [InlineData("H", "")]
    [InlineData("I", "")] [InlineData("K", "")] [InlineData("M", "")] [InlineData("N", "")]
    [InlineData("P", "")] [InlineData("R", "")] [InlineData("X", "")] [InlineData("Y", "")]
    [InlineData("Z", "")] [InlineData("3", "")] [InlineData("4", "")] [InlineData("7", "")]
    [InlineData("9", "")]
    [InlineData("KRENIO", "")] [InlineData("PROTEIN", "")] [InlineData("HELLO", "")]
    // 只旋转
    [InlineData("C", "@@@")] [InlineData("J", "@")] [InlineData("O", "@")] [InlineData("Q", "@")]
    [InlineData("S", "@@@")] [InlineData("T", "@@@")] [InlineData("U", "@")] [InlineData("V", "@")]
    [InlineData("W", "@")] [InlineData("0", "@@@")] [InlineData("8", "@")]
    [InlineData("SOS", "@@@")]
    // 翻转 + 补偿旋转
    [InlineData("E", "@@#")] [InlineData("F", "@@#")] [InlineData("G", "@@#")]
    [InlineData("L", "@@@#")] [InlineData("6", "@@@#")]
    [InlineData("1", "#")] [InlineData("2", "#")] [InlineData("5", "#")]
    [InlineData("68S", "@@@#")] [InlineData("123456", "#")]
    public void 每个字模的开头标记符合旧参考实现(string text, string expectPrefix)
    {
        var master = Master.ParseMaster(FoldFont.TextToMaster(text));

        Assert.Equal(expectPrefix, master.Marks.Prefix);

        // 有基准的字模还要再过一道：带上这些标记折出来的成品，
        // 形状与朝向都得与旧实现重合（IoU 会把宽高各自归一化，
        // 看不出差 90° 的旋转，所以长宽比单独再验一次）。
        if (ShapeMatch.Legacy(text) is not { } want) return;

        var fm = FoldOps.FoldStrip(master, MasterOps.DefaultMv(master.Pipes.Count + master.NCell));
        double iou = ShapeMatch.Match(fm.World, master.Marks.Canvas, text);
        Assert.True(iou >= 0.99, $"「{text}」成品与旧实现重合度仅 {iou:0.###}");

        static (double W, double H) Box(List<List<(double X, double Y)>> ps)
        {
            var all = ps.SelectMany(p => p).ToList();
            return (all.Max(p => p.X) - all.Min(p => p.X), all.Max(p => p.Y) - all.Min(p => p.Y));
        }
        var (lw, lh) = Box(want);
        var (ow, oh) = Box(ShapeMatch.View(fm.World, master.Marks.Canvas));
        Assert.True(Math.Abs(lw / lh - ow / oh) < 0.05,
            $"「{text}」朝向不对：旧 {lw:0.#}×{lh:0.#}，新 {ow:0.#}×{oh:0.#}");
    }

    /// <summary>
    /// 带标记的母版串可以重新解析回来：抄一份、存成文件、再粘回来都不该丢。
    /// </summary>
    [Theory]
    [InlineData("G")] [InlineData("E")] [InlineData("6")] [InlineData("S")]
    [InlineData("A")] [InlineData("68S")] [InlineData("KRENIO")]
    public void 带标记的母版串可原样往返(string text)
    {
        string marked = FoldFont.TextToMaster(text);
        var a = Master.ParseMaster(marked);

        // 用「原始正文 + 标记」重新拼一遍，格序列与标记都必须一致
        var b = Master.ParseMaster(a.Marks.Prefix + a.RawBody);
        Assert.Equal(a.Marks, b.Marks);
        Assert.Equal(a.Cells, b.Cells);
        Assert.Equal(a.Pipes, b.Pipes);

        // 已经按标记翻转过的正文（Cells）再套一次 <c>#</c> 会被翻回去 ——
        // 所以「往返」必须始终走原始正文（RawBody），不能走翻转后的 Body
        if (a.Marks.Flip)
            Assert.NotEqual(a.Cells, Master.ParseMaster(a.Marks.Prefix + a.Body).Cells);
    }

    /// <summary><c>@</c> 与 <c>#</c> 只在<b>开头</b>有效，正文里出现即非法。</summary>
    [Fact]
    public void 标记只允许出现在开头()
    {
        Assert.Throws<ArgumentException>(() => Master.ParseMaster("/..@.."));
        Assert.Throws<ArgumentException>(() => Master.ParseMaster("/..#.."));

        // 开头连续出现多次也可以：@ 取总数模 4，# 按奇偶抵消
        Assert.Equal(new MasterMarks(1, false), Master.ParseMaster("@@@@@/....").Marks);
        Assert.Equal(new MasterMarks(0, false), Master.ParseMaster("##/....").Marks);
        Assert.Equal(new MasterMarks(2, true), Master.ParseMaster("@@#/....").Marks);
    }

    /// <summary>
    /// 画布只负责旋转 —— <c>#</c> 已经作用在折痕上，再翻一次会抵消。
    /// </summary>
    [Fact]
    public void 翻转落在折痕上而不是画布上()
    {
        var m = Master.ParseMaster("@@#..\\.../.\\.");
        Assert.True(m.Marks.Flip);
        Assert.Equal(2, m.Marks.QuarterTurns);
        Assert.Equal(CanvasTransform.Create(2), m.Marks.Canvas);

        // 折痕确实被翻了：G 的正文本来是 ..\.../.\.，带 # 后 / 与 \ 互换
        var plain = Master.ParseMaster("..\\.../.\\.");
        Assert.Equal(plain.Cells.Select(c => c == '/' ? '\\' : c == '\\' ? '/' : c),
                     m.Cells);
    }
}
