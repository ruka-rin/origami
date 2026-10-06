using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 折纸字库：普通文本 -&gt; 折痕母版串。
/// <para>
/// ★ 这组测试里最有力的两条是
/// <see cref="生成的母版与题面手写母版逐字符相同"/>：题面给的两份「手��」母版
/// 本身就是用这套字库编出来的，所以「从文本生成」能<b>逐字符复现</b>它们 ——
/// 这比任何间接指标都强的正确性证据。</para>
/// </summary>
public class FoldFontTests
{
    [Theory]
    [InlineData("KRENIO")]
    [InlineData("PROTEIN")]
    public void 生成的母版与题面手写母版逐字符相同(string word)
    {
        string reference = word == "KRENIO" ? SampleMasters.Krenio : SampleMasters.Protein;
        Assert.Equal(reference, FoldFont.TextToMaster(word));
    }

    /// <summary>生成结果必须能被正常解析，并铺出与手写母版相同的方形。</summary>
    [Theory]
    [InlineData("KRENIO", 100, 9, 11, 80)]
    [InlineData("PROTEIN", 118, 11, 12, 91)]
    public void 生成的母版规模与手写的一致(string word, int ncell, int pipes, int n, int fold)
    {
        var m = Master.ParseMaster(FoldFont.TextToMaster(word));
        Assert.Equal(ncell, m.NCell);
        Assert.Equal(pipes, m.Pipes.Count);

        var plan = SquareOps.PlanFromMaster(m);
        Assert.Equal(n, plan.N);
        Assert.Equal(fold, plan.Fold.Count);
    }

    /// <summary>
    /// 折痕序列 -&gt; 母版 -&gt; 折痕序列 应当回到同一组折痕。
    /// <para>
    /// 这是 <see cref="FoldFont.CreasesToMaster"/> 的往返性质，也是最容易出错的地方：
    /// 竖折痕在母版串里是<b>插入</b>而不是替换格子，若实现成替换就会吃掉一格斜折痕。
    /// </para>
    /// </summary>
    [Fact]
    public void 折痕与母版互为逆运算()
    {
        foreach (var word in new[] { "KRENIO", "PROTEIN", "HELLO", "ABC" })
        {
            string master = FoldFont.TextToMaster(word);

            // 母版 -> 折痕（走真实链路，含首尾哨兵）
            var (creases, width, height) = Layout.MasterToCreases(
                Master.ParseMaster(master).Cells,
                Master.ParseMaster(master).Pipes);

            // 折痕 -> 母版（跳过首尾哨兵后应逐条一致）
            var again = FoldFont.CreasesToMaster(
                creases.Select(c => (c.Xb, c.Xt)).ToList());
            Assert.Equal(master, again);
        }
    }

    /// <summary>母版串里 <c>|</c> 是插入的，不占用格位。</summary>
    [Fact]
    public void 竖折痕不占用格位()
    {
        string master = FoldFont.TextToMaster("KRENIO");
        int pipes = master.Count(c => c == '|');
        int cells = master.Count(c => c is '/' or '\\' or '.');
        var m = Master.ParseMaster(master);

        Assert.Equal(9, pipes);
        Assert.Equal(m.Pipes.Count, pipes);
        Assert.Equal(100, cells);
        Assert.Equal(m.NCell, cells);

        // 第 p 条竖折痕必须出现在第 p 格之前：数到它时已过的格数 == p
        int seen = 0;
        foreach (char c in master)
        {
            if (c == '|') Assert.Contains(seen, m.Pipes);
            else seen++;
        }
    }

    /// <summary>大小写与不支持的字符。</summary>
    [Fact]
    public void 大小写归一且跳过不支持的字符()
    {
        Assert.Equal(FoldFont.TextToMaster("KISS"), FoldFont.TextToMaster("kiss"));

        // 标点与中文不在字库里，应被跳过（不影响其余字符）
        string withPunct = FoldFont.TextToMaster("A-B");
        Assert.Equal(FoldFont.TextToMaster("AB"), withPunct);

        // 完全没有可识别字符 -> 空母版
        Assert.Equal("", FoldFont.TextToMaster("你好"));

        // ★ 空格是<b>字库里的一个字符</b>，作用是「留一段空白纸」，
        //   所以三个空格得到的是 9 个素格而不是空串 —— 这是正确行为。
        string blanks = FoldFont.TextToMaster("   ");
        Assert.NotEqual("", blanks);
        Assert.All(blanks, c => Assert.Equal('.', c));
    }

    /// <summary>多行文本：奇数行按开关镜像或反转，行间补转折折痕。</summary>
    [Fact]
    public void 多行文本两种模式都能生成合法母版()
    {
        foreach (bool mirror in new[] { false, true })
        {
            foreach (bool left in new[] { false, true })
            {
                string master = FoldFont.TextToMaster("AB\nCD", mirror, left);
                Assert.NotEqual("", master);
                // 必须能解析、能铺方 —— 两种模式的差别只该体现在视觉上，不该破坏结构
                var plan = SquareOps.PlanFromMaster(Master.ParseMaster(master));
                Assert.True(plan.N >= 1);
            }
        }

        // 默认（不镜像、不左起）与显式传参应当一致
        Assert.Equal(FoldFont.TextToMaster("AB\nCD"),
                     FoldFont.TextToMaster("AB\nCD", mirror: false, left: false));
    }

    /// <summary>字库收录 A-Z 与 0-9。</summary>
    [Fact]
    public void 字库收录全部字母与数字()
    {
        var chars = FoldFont.Chars;
        foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
            Assert.Contains(c, chars);

        // 「词首」变体是内部实现，不该出现在可输入字符里
        Assert.DoesNotContain('s', FoldFont.Chars);
        Assert.True(FoldFont.Encodings.ContainsKey("Ostart"));
        Assert.True(FoldFont.Encodings.ContainsKey("Tstart"));
    }

    /// <summary>生成的母版应能逆向还原回同一串（闭环）。</summary>
    [Theory]
    [InlineData("KRENIO")]
    [InlineData("PROTEIN")]
    public void 生成的母版可被精确逆向(string word)
    {
        var plan = SquareOps.PlanFromMaster(Master.ParseMaster(FoldFont.TextToMaster(word)));
        var rep = Reverse.VerifyRoundtrip(plan);

        Assert.True(rep.Ok);
        Assert.Equal(FoldFont.TextToMaster(word), rep.Master.Text.Trim());
    }
}