using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 一个母版 + 一份手绘题面基准。KRENIO 与 PROTEIN 各一套。
/// </summary>
/// <param name="Name">母版名（用于断言消息）。</param>
/// <param name="Master">母版字符串。</param>
/// <param name="TruthFile">基准文件名（相对测试输出目录）。</param>
/// <param name="N">方形边长（格）。</param>
/// <param name="Fold">方形 CP 折痕条数。</param>
/// <param name="Cut">剪切线逐格段数。</param>
/// <param name="Border">外框逐格段数。</param>
/// <param name="Mountain">峰数。</param>
/// <param name="Valley">谷数。</param>
public record Baseline(
    string Name, string Master, string TruthFile,
    int N, int Fold, int Cut, int Border, int Mountain, int Valley);

/// <summary>
/// 方形 CP 的正确性校验 —— <b>与手绘题面基准逐条比对</b>。
/// <para>
/// 基准（<c>square_truth*.svg</c>）不是本工具生成的，而是题面手绘的折痕图。母版推导出的
/// 方形 CP 必须与它<b>逐条零差异</b>：折痕集合、剪切线、外框，以及每条折痕的峰谷。
/// 这是整个工具链的回归闸门 —— 只要铺排算法被改动而破坏了题面，它就会失败。
/// 我特意做了变异测试验证闸门有效：把边长公式改一个字符，本组测试立刻失败。</para>
/// <para>
/// 两套基准覆盖了不同的规模与形态（KRENIO 100 格 / 9 竖折痕 / N=11；
/// PROTEIN 118 格 / 11 竖折痕 / N=12），因此能同时挡住「规模相关的边界错误」
/// 和「只在特定折痕形态下才出现的错误」。</para>
/// </summary>
public class CrossCheckTests
{
    public static TheoryData<string> Baselines => new() { "KRENIO", "PROTEIN" };

    static Baseline Get(string name) => name switch
    {
        "KRENIO" => new Baseline("KRENIO", SampleMasters.Krenio, "square_truth.svg",
                                  N: 11, Fold: 80, Cut: 100, Border: 44,
                                  Mountain: 40, Valley: 40),
        "PROTEIN" => new Baseline("PROTEIN", SampleMasters.Protein, "square_truth_protein.svg",
                                  N: 12, Fold: 91, Cut: 121, Border: 48,
                                  Mountain: 45, Valley: 46),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "未知基准"),
    };

    /// <summary>读基准文件 —— 文件 I/O 归测试自己，库只收文本。</summary>
    static string TruthSvg(Baseline b)
    {
        string p = Path.Combine(AppContext.BaseDirectory, b.TruthFile);
        Assert.True(File.Exists(p), $"缺少权威基准 {p}");
        return File.ReadAllText(p);
    }

    [Theory]
    [MemberData(nameof(Baselines))]
    public void 母版推导的方形CP与手绘基准零差异(string name)
    {
        var b = Get(name);
        var plan = SquareOps.PlanFromMaster(Master.ParseMaster(b.Master));
        var res = Truth.Crosscheck(plan, TruthSvg(b));

        Assert.True(res.Fold.OnlyGen.Count == 0,
            $"[{b.Name}] 多出 {res.Fold.OnlyGen.Count} 条折痕：{Preview(res.Fold.OnlyGen)}");
        Assert.True(res.Fold.OnlyTruth.Count == 0,
            $"[{b.Name}] 缺少 {res.Fold.OnlyTruth.Count} 条折痕：{Preview(res.Fold.OnlyTruth)}");
        Assert.Equal(b.Fold, res.Fold.Shared);

        Assert.True(res.Cut.OnlyGen.Count == 0 && res.Cut.OnlyTruth.Count == 0,
            $"[{b.Name}] 剪切线不一致：多出 {res.Cut.OnlyGen.Count} / 缺少 {res.Cut.OnlyTruth.Count}");
        Assert.Equal(b.Cut, res.Cut.Shared);

        Assert.True(res.Border.OnlyGen.Count == 0 && res.Border.OnlyTruth.Count == 0,
            $"[{b.Name}] 外框不一致：多出 {res.Border.OnlyGen.Count} / 缺少 {res.Border.OnlyTruth.Count}");
        Assert.Equal(b.Border, res.Border.Shared);

        Assert.True(res.MvMismatch.Count == 0,
            $"[{b.Name}] 峰谷不一致 {res.MvMismatch.Count} 处：{Preview(res.MvMismatch)}");
        Assert.True(res.MvOnlyTruth.Count == 0 && res.MvOnlyGen.Count == 0,
            $"[{b.Name}] 峰谷缺失 {res.MvOnlyTruth.Count} / 多出 {res.MvOnlyGen.Count}");

        Assert.True(res.Ok, $"[{b.Name}] " + Truth.FormatReport("方形CP", res));
    }

    [Theory]
    [MemberData(nameof(Baselines))]
    public void 基准文件自身规模符合签名(string name)
    {
        var b = Get(name);
        var t = Truth.ParseCreaseSvg(TruthSvg(b));

        Assert.Equal(b.N, t.N);
        Assert.Equal(b.Fold, t.Fold.Count);
        Assert.Equal(b.Cut, t.Cut.Count);
        Assert.Equal(b.Border, t.Border.Count);
        Assert.Equal(b.Mountain, t.FoldMv.Count(kv => kv.Value == Mv.Mountain));
        Assert.Equal(b.Valley, t.FoldMv.Count(kv => kv.Value == Mv.Valley));
    }

    [Theory]
    [MemberData(nameof(Baselines))]
    public void 母版规模签名(string name)
    {
        var b = Get(name);
        var m = Master.ParseMaster(b.Master);
        var plan = SquareOps.PlanFromMaster(m);

        Assert.Equal(b.N, plan.N);
        Assert.Equal(b.Fold, plan.Fold.Count);
        // 剪切线与外框的逐格段数：N 格的蛇形铺排切出 N*(N-1) 段内部线 + 外框 4*(N-1)
        Assert.Equal(b.Cut, plan.Cut.Count);
        Assert.Equal(b.Border, plan.Border.Count);
    }

    /// <summary>折叠是等距变换，总面积必须守恒。</summary>
    [Theory]
    [MemberData(nameof(Baselines))]
    public void 折叠前后面积守恒(string name)
    {
        var b = Get(name);
        var plan = SquareOps.PlanFromMaster(Master.ParseMaster(b.Master));
        var fm = FoldOps.FoldSquare(plan);

        double flat = Geom.HalfedgeFaces(plan.Cut, plan.Fold, plan.Border)
                            .Sum(Geom.PolyArea);
        Assert.Equal(flat, fm.World.Sum(Geom.PolyArea), 6);
    }

    /// <summary>方形 CP -&gt; 母版 -&gt; 方形 CP 精确还原，且母版字符串逐字符一致。</summary>
    [Theory]
    [MemberData(nameof(Baselines))]
    public void 母版可被精确逆向(string name)
    {
        var b = Get(name);
        var plan = SquareOps.PlanFromMaster(Master.ParseMaster(b.Master));
        var rep = Reverse.VerifyRoundtrip(plan);

        Assert.True(rep.Ok, $"[{b.Name}] 逆向后重新铺排应能复现原方形");
        Assert.Equal(b.Master, rep.Master.Text.Trim());
    }

    static string Preview(List<Segment> segs)
        => $"[{string.Join(", ", segs.Take(5).Select(s => s.ToString()))}]";
}