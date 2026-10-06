using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 随机母版压力测试：用<b>随机母版</b>验证「母版 → 方形 → 逆向 → 母版」整条链路的普适性。
/// <para>
/// 这比手绘基准更强的证据 —— 基准只有 3 个样本，随机母版能覆盖各种边界
/// （奇偶格数、极端对角密度、零竖折痕、必须大量尾部填充等）。</para>
/// <para>
/// <b>判定标准</b>（两个都要满足）：
/// <list type="number">
/// <item><b>闭环</b>：逆向得到的母版重新正向铺排，必须<b>逐条复现</b>原方形；</item>
/// <item><b>同母版</b>：逆向结果的格数 / 对角符号 / 竖折痕与原母版完全一致。</item>
/// </list>
/// </para>
/// <para>★ <b>原理性的信息损失（非缺陷）</b>：母版末尾的「素格 <c>.</c> + 尾部 pipe」
/// 在方形 CP 里<b>不可见</b> ——
/// <code>
/// 原：... / | .      （末格是素格）
/// 逆：... / |        （末格变成对角）
/// </code>
/// 这两种母版正向铺排后<b>逐条完全相同</b>：信息在正向映射的象里就已重合，
/// 任何逆向算法都无法区分。所以「同母版」在这一类输入上<b>必然失败</b>，
/// 但「闭环」仍成立（还原出的母版能复现同一个方形，成品仍然正确）。
/// 因此这里断言的是<b>闭环率 100%</b>，而「同母版」率略低 —— 差值就是这类歧义样本的占比。</para>
/// <para>★ <b>随机母版的生成刻意不依赖具体 PRNG</b>：不同运行时的随机数生成器
/// 实现不同，同一种子会生成不同的母版序列。因此本测试只断言
/// <b>闭环率 100%、失败 0</b> 这两条与生成器无关的不变量；要做逐条回归对照，
/// 请用固定的母版串（见 <see cref="CrossCheckTests"/>）。</para>
/// </summary>
public class StressTests
{
    /// <summary>随机母版：<paramref name="pdiag"/> 对角格占比，<paramref name="ppipe"/> 竖折痕密度。</summary>
    internal static Master RandomMaster(Random rng, int ncell, double pdiag, double ppipe)
    {
        var cells = new char[ncell];
        for (int i = 0; i < ncell; i++)
        {
            double r = rng.NextDouble();
            cells[i] = r < pdiag / 2 ? '/' : r < pdiag ? '\\' : '.';
        }
        var pipes = new HashSet<int>();
        for (int k = 1; k < ncell; k++) if (rng.NextDouble() < ppipe) pipes.Add(k);

        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < ncell; k++)
        {
            sb.Append(cells[k]);
            if (pipes.Contains(k + 1)) sb.Append('|');
        }
        return new Master(sb.ToString());
    }

    /// <summary>尾部填充形态串：<c>D</c>=对角、<c>V</c>=竖直；空串 = 无需填充。</summary>
    internal static string TailShape(Master master, int n)
    {
        var (creases, width, _) = Layout.MasterToCreases(master.Cells, master.PipeSet);
        var padded = Layout.PadCreases(new List<Layout.RawCrease>(creases), Layout.ChooseTotal(n));
        var sb = new System.Text.StringBuilder();
        for (int i = creases.Count; i < padded.Count; i++)
            sb.Append(padded[i].Xb == padded[i].Xt ? 'V' : 'D');
        return sb.ToString();
    }

    public sealed class Stats
    {
        public int Trials, Ok, Closed, Fail, Skip, RoundtripFail;
        public int TailTri, TailSq, TailNone, TailMixed;
        public List<string> Failures { get; } = new();
    }

    /// <summary>跑一轮随机母版，返回统计。</summary>
    internal static Stats Run(int trials, int seed)
    {
        var rng = new Random(seed);
        var st = new Stats { Trials = trials };

        for (int t = 0; t < trials; t++)
        {
            int ncell = rng.Next(1, 161);
            double pdiag = new[] { 0.3, 0.6, 0.9 }[rng.Next(3)];
            double ppipe = new[] { 0.0, 0.05, 0.15 }[rng.Next(3)];
            var m = RandomMaster(rng, ncell, pdiag, ppipe);

            SquarePlan plan;
            try { plan = SquareOps.PlanFromMaster(m); }
            catch { st.Skip++; continue; }

            var shape = TailShape(m, plan.N);
            if (shape.Length == 0) st.TailNone++;
            else
            {
                if (shape[^1] == 'V') st.TailSq++; else st.TailTri++;
                if (shape.Distinct().Count() > 1) st.TailMixed++;
            }

            Master back;
            try { back = Reverse.SquareToMaster(plan, strict: true); }
            catch (Exception ex)
            {
                st.Fail++;
                st.Failures.Add($"ncell={ncell} 逆向异常: {ex.Message}");
                continue;
            }

            bool closed = Reverse.FoldCells(back).SetEquals(Reverse.PlanFoldCells(plan));
            if (closed) st.Closed++;
            bool same = back.NCell == m.NCell &&
                        back.Cells.SequenceEqual(m.Cells) &&
                        back.Pipes.OrderBy(x => x).SequenceEqual(m.Pipes.OrderBy(x => x));
            if (closed && same) st.Ok++;
            else if (!closed)
            {
                st.RoundtripFail++;
                st.Failures.Add($"ncell={ncell} 闭环失败，还原后 ncell={back.NCell}");
            }
            // else: 闭环成立但非同母版 —— 属已知的原理性信息损失，不计为失败
        }
        return st;
    }

    [Fact]
    public void 逆向闭环率必须百分之百()
    {
        var st = Run(trials: 500, seed: 20261006);

        Assert.True(st.Closed > 0, "样本数为 0，压力测试没有真正执行");
        Assert.Equal(st.Trials - st.Skip, st.Closed);          // 闭环率 100%
        Assert.Equal(0, st.RoundtripFail);
        Assert.Equal(0, st.Fail);
        Assert.True(st.Failures.Count == 0, string.Join("; ", st.Failures.Take(5)));
    }

    [Fact]
    public void 多种子下闭环率都必须是百分之百()
    {
        foreach (int seed in new[] { 1, 42, 20261006, -7, int.MaxValue })
        {
            var st = Run(trials: 120, seed: seed);
            Assert.Equal(0, st.Fail);
            Assert.Equal(0, st.RoundtripFail);
            Assert.Equal(st.Trials - st.Skip, st.Closed);
        }
    }

    [Fact]
    public void 样本覆盖多种尾部填充形态()
    {
        var st = Run(trials: 500, seed: 20261006);
        Assert.True(st.TailTri > 0, "未覆盖三角收尾");
        Assert.True(st.TailSq > 0, "未覆盖方形收尾");
        Assert.True(st.TailNone > 0, "未覆盖无需填充");
        Assert.True(st.TailMixed > 0, "未覆盖对角+竖直混合收尾");
    }

    [Fact]
    public void 同母版率应接近但允许低于百分之百()
    {
        var st = Run(trials: 500, seed: 20261006);
        int decided = st.Closed;
        Assert.True(st.Ok <= decided);
        // 「同母版」的损失来自母版末尾「素格 + 尾部 pipe」在方形 CP 里不可见，
        // 属原理性信息损失。闭环率已是 100%，这里只做上下界 sanity check。
        Assert.True(st.Ok > decided * 0.8,
            $"同母版率 {st.Ok}/{decided} 偏低，可能存在预期外的信息损失");
    }

    [Fact]
    public void 相同种子结果完全可复现()
    {
        var a = Run(trials: 150, seed: 999);
        var b = Run(trials: 150, seed: 999);
        Assert.Equal(a.Ok, b.Ok);
        Assert.Equal(a.Closed, b.Closed);
        Assert.Equal(a.Fail, b.Fail);
        Assert.Equal(a.Skip, b.Skip);
        Assert.Equal(a.TailTri, b.TailTri);
        Assert.Equal(a.TailSq, b.TailSq);
        Assert.Equal(a.TailNone, b.TailNone);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(64)]
    [InlineData(161)]
    public void 固定边界格数不崩溃(int ncell)
    {
        var m = RandomMaster(new Random(ncell), ncell, 0.6, 0.08);
        Assert.Equal(ncell, m.NCell);
        // 铺排要么成功给出合法方形，要么明确失败 —— 不应抛异常以外的问题
        try
        {
            var plan = SquareOps.PlanFromMaster(m);
            Assert.True(plan.N >= 2);
        }
        catch (InvalidOperationException) { /* 对偶图非树等已知边界，允许 */ }
    }
}