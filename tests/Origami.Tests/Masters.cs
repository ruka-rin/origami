using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 母版常量与基准的签名自检。
/// <para>
/// 常量本身放在库里（<see cref="SampleMasters"/>），GUI 与测试共用同一份 ——
/// 手抄母版是本项目最容易出错的地方，反斜杠多一个少一个都还是<b>合法</b>母版，
/// 只是格数悄悄变了。集中之后，这里负责把「原始长度 / 格数 / 竖折痕数 / 方形规模」
/// 钉死，任何一处被编辑坏都会立刻暴露在常量本身，而不是在某个下游断言里报「差了几格」。</para>
/// </summary>
public class SampleMasterTests
{
    [Fact]
    public void KRENIO常量正确()
    {
        var m = Master.ParseMaster(SampleMasters.Krenio);
        Assert.Equal(109, SampleMasters.Krenio.Length);   // 含 9 个 '|'
        Assert.Equal(100, m.NCell);
        Assert.Equal(9, m.Pipes.Count);

        var plan = SquareOps.PlanFromMaster(m);
        Assert.Equal(11, plan.N);
        Assert.Equal(80, plan.Fold.Count);
        Assert.Equal(48, Model.StripModel(m).Folds.Count);
    }

    [Fact]
    public void PROTEIN常量正确()
    {
        var m = Master.ParseMaster(SampleMasters.Protein);
        Assert.Equal(129, SampleMasters.Protein.Length);  // 含 11 个 '|'
        Assert.Equal(118, m.NCell);
        Assert.Equal(11, m.Pipes.Count);

        var plan = SquareOps.PlanFromMaster(m);
        Assert.Equal(12, plan.N);
        Assert.Equal(91, plan.Fold.Count);
        Assert.Equal(53, Model.StripModel(m).Folds.Count);
    }

    /// <summary>GUI 用的是同一个常量，这里守住「两边不会各拿一份」这条不变式。</summary>
    [Fact]
    public void 母版常量不含引号因而可用原始字符串字面量表达()
    {
        Assert.DoesNotContain('"', SampleMasters.Krenio);
        Assert.DoesNotContain('"', SampleMasters.Protein);
        Assert.DoesNotContain("\n", SampleMasters.Krenio);
        Assert.DoesNotContain("\n", SampleMasters.Protein);
    }
}
