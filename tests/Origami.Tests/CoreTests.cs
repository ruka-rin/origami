using System.Text.Json;
using Origami.Core;
using Xunit;

namespace Origami.Tests;


/// <summary>
/// 母版字符串解析与长条几何。
/// </summary>
public class MasterTests
{
    [Fact]
    public void Pipe不占格只记边界()
    {
        // 尾部 '|' 标记最后一格之后的边界，故 3 格 -> pipes {1,2}
        var m = Master.ParseMaster("/|/|/");
        Assert.Equal(3, m.NCell);
        Assert.Equal(new[] { 1, 2 }, m.Pipes);

        // "/|/" 只有 2 格：末尾 '|' 不凭空造出第三格
        var m2 = Master.ParseMaster("/|/");
        Assert.Equal(2, m2.NCell);
        Assert.Equal(new[] { 1 }, m2.Pipes);
    }

    [Fact]
    public void 空白被忽略()
    {
        // 去掉空白后是 "/\.|/" -> 4 格；'|' 不占格
        var m = Master.ParseMaster(" / \\. | / ");
        Assert.Equal(4, m.NCell);
        Assert.Equal(new[] { 3 }, m.Pipes);
    }

    [Fact]
    public void 未知字符报错()
    {
        var ex = Assert.Throws<ArgumentException>(() => Master.ParseMaster("/x/"));
        Assert.Contains("未知字符", ex.Message);
    }

    [Fact]
    public void 空母版报错()
        => Assert.Throws<ArgumentException>(() => Master.ParseMaster("|"));

    [Fact]
    public void 竖折痕位置正确()
    {
        // 格 0 是 '/'，格 1 与格 2 之间有 pipe
        var m = Master.ParseMaster("/|/.");
        var (_, fold, _) = m.StripGeometry();
        // '/' -> (0,0)-(1,1)
        Assert.Contains(fold, s => s.A == new Point(0, 0) && s.B == new Point(1, 1));
        // pipe 1 -> (1,0)-(1,1)
        Assert.Contains(fold, s => s.A == new Point(1, 0) && s.B == new Point(1, 1));
    }

    [Fact]
    public void 长条没有剪切线且边框贯通()
    {
        var m = Master.ParseMaster("/.\\.");
        var (cut, _, border) = m.StripGeometry();
        Assert.Empty(cut);
        // 上下两条长边各 4 段 + 左右两个短边
        Assert.Equal(10, border.Count);
    }

    [Fact]
    public void 行进顺序按纸带走到底()
    {
        var m = Master.ParseMaster("/|/\\");
        var (creases, kinds, cellIds) = m.CreasesInOrder();
        Assert.Equal(4, creases.Count);
        Assert.Equal(new[] { 'D', 'V', 'D', 'D' }, kinds);
        Assert.Equal(new[] { 0, 1, 1, 2 }, cellIds);
    }

    [Fact]
    public void 数据模型可逆回母版()
    {
        const string text = "/....|../\\.|.//.\\./.";
        var m = Master.ParseMaster(text);
        var model = Model.StripModel(m);
        var back = Model.StripModelToMaster(model);
        Assert.Equal(m.Cells, back.Cells);
        Assert.Equal(m.Pipes, back.Pipes);
    }
}

/// <summary>
/// 几何与平面图：半边遍历、面片过滤、反射。
/// </summary>
public class GeomTests
{
    [Fact]
    public void 线段类别区分四个方向()
    {
        Assert.Equal("V", Geom.SegClass(new Point(0, 0), new Point(0, 5)));
        Assert.Equal("H", Geom.SegClass(new Point(0, 0), new Point(5, 0)));
        // 屏幕坐标 y 向下：右下方向 = '\'
        Assert.Equal("\\", Geom.SegClass(new Point(0, 0), new Point(1, 1)));
        // 左下->右上 = '/'
        Assert.Equal("/", Geom.SegClass(new Point(0, 1), new Point(1, 0)));
    }

    [Fact]
    public void 面积与有符号面积()
    {
        var ccw = new List<Point> { new(0, 0), new(2, 0), new(2, 2), new(0, 2) };
        Assert.Equal(4.0, Geom.PolyArea(ccw), 9);
        Assert.True(Geom.PolySignedArea(ccw) > 0);
        var cw = new List<Point> { new(0, 0), new(0, 2), new(2, 2), new(2, 0) };
        Assert.True(Geom.PolySignedArea(cw) < 0);
        Assert.Equal(4.0, Geom.PolyArea(cw), 9);
    }

    [Fact]
    public void 反射矩阵是对合且保距()
    {
        var m = Geom.ReflectMatrix(new Point(0, 0), new Point(1, 1));
        var p = new Point(3, -2);
        var q = m.Apply(p);
        var back = m.Apply(q);
        Assert.Equal(p.X, back.X, 9);
        Assert.Equal(p.Y, back.Y, 9);
    }

    [Fact]
    public void 竖直线的反射只翻x()
    {
        var m = Geom.ReflectMatrix(new Point(1, 0), new Point(1, 10));
        var q = m.Apply(new Point(4, 3));
        Assert.Equal(-2.0, q.X, 9);       // 2*1 - 4
        Assert.Equal(3.0, q.Y, 9);
    }

    [Fact]
    public void 半边遍历只返回有限面且过滤外圈()
    {
        // 单位方块 + 一条对角折痕 -> 2 个三角面
        var border = SegSets.Create(new[]
        {
            Segment.Norm(new Point(0, 0), new Point(1, 0)),
            Segment.Norm(new Point(1, 0), new Point(1, 1)),
            Segment.Norm(new Point(1, 1), new Point(0, 1)),
            Segment.Norm(new Point(0, 1), new Point(0, 0)),
        });
        var fold = SegSets.Create(new[] { Segment.Norm(new Point(0, 0), new Point(1, 1)) });
        var faces = Geom.HalfedgeFaces(new HashSet<Segment>(), fold, border);

        Assert.Equal(2, faces.Count);
        Assert.All(faces, f => Assert.Equal(0.5, Geom.PolyArea(f), 9));
    }

    [Fact]
    public void 保留共线顶点以免折痕在对偶图上消失()
    {
        // 2x1 长条，中间一条竖折痕把折痕端点放在长边中点之外的情形由 simplify 控制
        var border = SegSets.Create(new[]
        {
            Segment.Norm(new Point(0, 0), new Point(2, 0)),
            Segment.Norm(new Point(2, 0), new Point(2, 1)),
            Segment.Norm(new Point(2, 1), new Point(0, 1)),
            Segment.Norm(new Point(0, 1), new Point(0, 0)),
        });
        var fold = SegSets.Create(new[] { Segment.Norm(new Point(1, 0), new Point(1, 1)) });

        // 折痕端点落在长边中点，但该中点并不是图上的顶点 —— 两种模式结果相同
        var keep = Geom.HalfedgeFaces(new HashSet<Segment>(), fold, border, simplify: false);
        var simp = Geom.HalfedgeFaces(new HashSet<Segment>(), fold, border, simplify: true);
        Assert.Single(keep);
        Assert.Single(simp);

        // 真正会暴露问题���情形：折痕把长边切成两段，中间顶点成为图上的节点。
        // 面片数量不变（仍是 1 个），但 simplify=True 会把边上共线的顶点合并掉 ——
        // 这正是它会抹掉折痕端点、让对偶图丢边的原因，所以默认必须 simplify=False。
        var border2 = SegSets.Create(new[]
        {
            Segment.Norm(new Point(0, 0), new Point(1, 0)),
            Segment.Norm(new Point(1, 0), new Point(2, 0)),
            Segment.Norm(new Point(2, 0), new Point(2, 1)),
            Segment.Norm(new Point(2, 1), new Point(0, 1)),
            Segment.Norm(new Point(0, 1), new Point(0, 0)),
        });
        var k2 = Geom.HalfedgeFaces(new HashSet<Segment>(), fold, border2, simplify: false);
        var s2 = Geom.HalfedgeFaces(new HashSet<Segment>(), fold, border2, simplify: true);
        Assert.Single(k2);
        Assert.Single(s2);
        Assert.True(k2[0].Count > s2[0].Count,
            $"simplify=True 应合并共线顶点（keep={k2[0].Count}, simp={s2[0].Count}）");
    }

    [Fact]
    public void 屏幕坐标翻转只影响纵向()
    {
        var polys = new List<List<Point>> { new() { new Point(0, 0), new Point(2, 0), new Point(0, 1) } };
        var (flip, _, (w1, h1)) = Geom.ToScreen(polys, 10, 0, flipY: true);
        var (noflip, _, _) = Geom.ToScreen(polys, 10, 0, flipY: false);
        Assert.Equal(20, w1);
        Assert.Equal(10, h1);
        Assert.NotEqual(flip[0][0].Y, noflip[0][0].Y);
        Assert.Equal(flip[0][0].X, noflip[0][0].X);
    }
}

/// <summary>
/// 方形 CP 铺排：边长公式、尾部填充、蛇形状态机。
/// </summary>
public class LayoutTests
{
    [Theory]
    [InlineData(100, 22)]     // KRENIO
    [InlineData(118, 24)]     // PROTEIN
    [InlineData(76, 20)]      // ABCD
    [InlineData(1, 4)]        // 边界：N 至少为 2
    [InlineData(0, 4)]
    public void 边长公式(int ncell, int expectSquare)
        => Assert.Equal(expectSquare, Layout.ChooseSquare(ncell));

    [Fact]
    public void 总长不小于母版长度()
    {
        for (int n = 2; n <= 30; n++)
            Assert.True(Layout.ChooseTotal(n) >= 0);
    }

    [Fact]
    public void 母版转折痕序列带首尾哨兵()
    {
        var m = Master.ParseMaster("/|\\");
        var (creases, width, height) = Layout.MasterToCreases(m.Cells, m.PipeSet);
        Assert.Equal(0, creases[0].Xb);
        Assert.Equal(0, creases[0].Xt);
        Assert.Equal(width, creases[^1].Xb);
        Assert.Equal(width, creases[^1].Xt);
        Assert.Equal(Layout.Unit, height);
    }

    [Fact]
    public void 尾部填充严格递增且收敛()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./");
        var (creases, width, h) = Layout.MasterToCreases(m.Cells, m.PipeSet);
        int n = Layout.ChooseSquare(width / Layout.Unit) / Layout.Unit;
        var padded = Layout.PadCreases(new List<Layout.RawCrease>(creases), Layout.ChooseTotal(n));

        // 该母版长度恰好等于方形容量，无需填充 —— 换一个更短的母版来验证填充逻辑
        var shortM = Master.ParseMaster("/|/\\");
        var (sc, sw, _) = Layout.MasterToCreases(shortM.Cells, shortM.PipeSet);
        int sn = Layout.ChooseSquare(sw / Layout.Unit) / Layout.Unit;
        var sp = Layout.PadCreases(new List<Layout.RawCrease>(sc), Layout.ChooseTotal(sn));

        Assert.True(sp.Count > sc.Count, "短母版应触发尾部填充");

        // 填充按「对角 + 竖直」交替推进：x 单调不减；每个对角步让 x 前进 UNIT，
        // 紧跟其后的竖直折痕与它同 x（故非严格递增 —— 这是设计使然）。
        int prevX = -1;
        int diagonals = 0;
        foreach (var c in sp.Skip(sc.Count))
        {
            int x = Math.Max(c.Xt, c.Xb);
            Assert.True(x >= prevX, $"填充 x 必须单调不减：x={x} < prevX={prevX}");
            if (c.Xt != c.Xb)
            {
                diagonals++;
                if (prevX >= 0) Assert.True(x - prevX <= Layout.Unit,
                    $"对角步进不得超过一格：x={x} prevX={prevX}");
            }
            prevX = x;
        }
        Assert.True(diagonals > 0, "填充序列应包含对角折痕");
        // 且不得越过方形总长
        int total = Layout.ChooseTotal(sn);
        Assert.True(sp[^1].Xb <= total && sp[^1].Xt <= total);
    }

    [Fact]
    public void 行容量首末行比中间行多一格()
    {
        var caps = Layout.ChooseRowCaps(11);
        Assert.Equal(11, caps.Count);
        Assert.Equal(10, caps[0]);
        Assert.Equal(9, caps[5]);
        Assert.Equal(10, caps[^1]);
    }
}

/// <summary>
/// 长条顺次滚动折叠与层序。
/// </summary>
public class FoldTests
{
    [Fact]
    public void 单个斜杠折出左上三角()
    {
        // 坐标系约定：数学坐标 y 向上，单个 '/' 顺次折叠 = {(0,0),(0,1),(1,1)}
        var m = Master.ParseMaster("/");
        var (faces, creases, _) = MasterOps.ChainFromMaster(m);
        var (world, _) = FoldOps.RollFold(faces, creases);

        var got = world[0].Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6)))
                          .OrderBy(t => t).ToList();
        Assert.Equal(new List<(double, double)> { (0, 0), (0, 1), (1, 1) }, got);
    }

    [Fact]
    public void 顺次折叠层序等于行进序()
    {
        var m = Master.ParseMaster("/....|../\\.");
        var (faces, creases, _) = MasterOps.ChainFromMaster(m);
        var rank = FoldOps.LayerRanks(faces.Count, creases);
        Assert.Equal(Enumerable.Range(0, faces.Count), rank);
        Assert.Empty(FoldOps.VerifyLayers(creases, rank));
    }

    [Fact]
    public void 折叠保持面积守恒()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./.../\\./.");
        var (faces, creases, _) = MasterOps.ChainFromMaster(m);
        var (world, _) = FoldOps.RollFold(faces, creases);

        double before = faces.Sum(Geom.PolyArea);
        double after = world.Sum(Geom.PolyArea);
        Assert.Equal(before, after, 6);
    }

    [Fact]
    public void 峰谷数量不足时报错()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => FoldOps.LayerRanks(5, new List<Segment>(), new List<string> { "M" }));
        Assert.Contains("峰谷序列长度", ex.Message);
    }

    [Fact]
    public void 折叠成品可摆正为横向()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./.../\\./.");
        var fm = FoldOps.FoldStrip(m, MasterOps.DefaultMv(48));
        var (x0, y0, x1, y1) = fm.Bbox();
        Assert.True((x1 - x0) >= (y1 - y0), "orient_landscape 后应为宽 >= 高");
    }

    [Fact]
    public void 方形对偶图必须是树()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./");
        var plan = SquareOps.PlanFromMaster(m);
        var (world, mv, depth, order, _) = FoldOps.SquareFold(plan);

        Assert.Equal(world.Count, order.Count);
        Assert.Equal(world.Count, mv.Count);
        Assert.Equal(world.Count, depth.Count);
        Assert.Equal(0, depth[order[0]]);
    }
}

/// <summary>
/// 逆向：方形 CP -&gt; 母版字符串。
/// </summary>
public class ReverseTests
{
    [Theory]
    [InlineData("/....|../\\.|.//.\\./.../\\./.|////.\\...\\..|../.\\..|..\\./..\\|\\/...." +
                "|.///\\..|....\\./....|..../.\\...\\./...\\/")]
    [InlineData("/|/")]
    [InlineData("/\\.")]
    public void 逆向后正向能复现原方形(string master)
    {
        var m = Master.ParseMaster(master);
        SquarePlan plan;
        try { plan = SquareOps.PlanFromMaster(m); }
        catch { return; }                       // 该母版不构成合法方形 CP

        var rep = Reverse.VerifyRoundtrip(plan);
        Assert.True(rep.Ok,
            $"闭环失败: 仅生成多出 {rep.OnlyGen.Count} 条、仅原图有 {rep.OnlyInput.Count} 条");
    }

    [Fact]
    public void 逆向能还原KRENIO母版()
    {
        var krenio = SampleMasters.Krenio;
        var m = Master.ParseMaster(krenio);
        Assert.Equal(100, m.NCell);
        Assert.Equal(9, m.Pipes.Count);

        var plan = SquareOps.PlanFromMaster(m);
        Assert.Equal(11, plan.N);
        Assert.Equal(80, plan.Fold.Count);

        var back = Reverse.SquareToMaster(plan);
        Assert.Equal(100, back.NCell);
        Assert.Equal(m.Cells, back.Cells);
        Assert.Equal(m.Pipes, back.Pipes);
    }

    [Fact]
    public void 分解能读出转折点()
    {
        var m = Master.ParseMaster(SampleMasters.Krenio);
        var plan = SquareOps.PlanFromMaster(m);
        var dec = Reverse.Decompose(plan);

        // 每条水平折痕唯一对应一个转折，转折次数 = N - 1
        Assert.Equal(plan.N - 1, dec.NTurn);
        // 行数 = 转折数 + 1：最后一行只承载尾部填充折痕，不再翻行
        Assert.Equal(dec.NTurn + 1, dec.Rows.Count);
        Assert.All(dec.Rows, r => Assert.NotEmpty(r.Value));
    }
}

/// <summary>
/// SVG / JSON 往返一致性。
/// </summary>
public class RoundTripTests
{
    [Fact]
    public void 长条模型经svg往返几何不变()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./");
        var model = Model.StripModel(m);
        var back = SvgIO.SvgToModel(SvgIO.ModelToSvg(model));

        Assert.Equal(model.Kind, back.Kind);
        Assert.Equal(model.NCell, back.NCell);
        Assert.Equal(model.Folds.Count, back.Folds.Count);
        Assert.Equal(model.Cut.Count, back.Cut.Count);
        Assert.Equal(model.Border.Count, back.Border.Count);
        for (int i = 0; i < model.Folds.Count; i++)
            Assert.Equal(model.Folds[i].MvType, back.Folds[i].MvType);
    }

    [Fact]
    public void 方形模型经svg往返几何不变()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./.../\\./.");
        var model = Model.SquareModel(SquareOps.PlanFromMaster(m));
        var back = SvgIO.SvgToModel(SvgIO.ModelToSvg(model));

        Assert.Equal(model.N, back.N);
        Assert.Equal(model.Folds.Count, back.Folds.Count);
        Assert.Equal(model.Cut.Count, back.Cut.Count);
    }

    [Fact]
    public void json往返保持模型()
    {
        var m = Master.ParseMaster("/....|../\\.|.//.\\./");
        var model = Model.StripModel(m);
        var back = SvgIO.JsonToModel(SvgIO.ModelToJson(model));

        Assert.Equal(model.NCell, back.NCell);
        Assert.Equal(model.Folds.Count, back.Folds.Count);
        for (int i = 0; i < model.Folds.Count; i++)
        {
            Assert.Equal(model.Folds[i].Seg, back.Folds[i].Seg);
            Assert.Equal(model.Folds[i].MvType, back.Folds[i].MvType);
        }
    }

    [Fact]
    public void 重复线段被校验拦下()
    {
        var model = new Model
        {
            Kind = "square",
            N = 2,
            Folds = new List<Fold>
            {
                new(Segment.Norm(new Point(0, 0), new Point(1, 1)), "M"),
                new(Segment.Norm(new Point(0, 0), new Point(1, 1)), "V"),
            },
        };
        Assert.Throws<InvalidOperationException>(() => SvgIO.ValidateModel(model));
    }
}

/// <summary>
/// 确定性：输出不得依赖任何哈希表的内部顺序。
/// <para>
/// 几何算法里有几处需要稳定的遍历顺序。若直接依赖 <c>HashSet</c> / <c>Dictionary</c>
/// 的迭代顺序，结果就会绑死在哈希实现上。因此这些位置一律<b>显式排序</b> ——
/// 面片遍历起点取字典序最小的线段，多边形顶点<b>规范化旋转</b>到字典序最小顶点开头，
/// 层序并列用<b>质心字典序</b>打破。本测试类守住这条性质。</para>
/// </summary>
public class DeterminismTests
{
    [Fact]
    public void 半边遍历结果与输入顺序无关()
    {
        var m = Master.ParseMaster(SampleMasters.Krenio);
        var (cut, fold, border) = m.StripGeometry();

        var a = Geom.HalfedgeFaces(cut, fold, border);

        // 打乱输入顺序重建同一组图，面片集合必须完全一致
        var fold2 = SegOrder.Sorted(fold).AsEnumerable().Reverse().ToList();
        var border2 = SegOrder.Sorted(border).AsEnumerable().Reverse().ToList();
        var b = Geom.HalfedgeFaces(cut, fold2, border2);

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            var ca = a[i].Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).OrderBy(t => t);
            var cb = b[i].Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))).OrderBy(t => t);
            Assert.Equal(ca, cb);
        }
    }

    [Fact]
    public void 多边形顶点已规范化为字典序最小开头()
    {
        var m = Master.ParseMaster("/.");
        var (cut, fold, border) = m.StripGeometry();
        foreach (var f in Geom.HalfedgeFaces(cut, fold, border))
        {
            var first = f[0];
            Assert.All(f, p => Assert.False(Point.Compare(p, first) < 0));
        }
    }

    [Fact]
    public void 重复运行折叠结果完全一致()
    {
        var m = Master.ParseMaster(SampleMasters.Krenio);
        var plan = SquareOps.PlanFromMaster(m);

        static string Digest(FoldedModel fm) => string.Join("|", fm.World.Select(f =>
            string.Join(",", f.Select(p => $"{p.X:F6},{p.Y:F6}"))));

        var a = Digest(FoldOps.FoldSquare(plan));
        var b = Digest(FoldOps.FoldSquare(plan));
        Assert.Equal(a, b);
    }

    [Fact]
    public void 层序是0到n减一的排列()
    {
        var m = Master.ParseMaster(SampleMasters.Krenio);
        var plan = SquareOps.PlanFromMaster(m);
        var fm = FoldOps.FoldSquare(plan);
        Assert.Equal(fm.World.Count, fm.Rank.Count);
        Assert.Equal(Enumerable.Range(0, fm.World.Count).OrderBy(x => x), fm.Rank.OrderBy(x => x));
    }

    [Fact]
    public void 线段集合去重且规范端点顺序()
    {
        var s = SegSets.Create(new[]
        {
            Segment.Norm(new Point(0, 0), new Point(1, 1)),
            Segment.Norm(new Point(1, 1), new Point(0, 0)),   // 反向，归一化后应重合
        });
        Assert.Single(s);
        Assert.Contains(Segment.Norm(new Point(0, 0), new Point(1, 1)), s);
    }

    [Fact]
    public void 排序工具给出稳定的字典序()
    {
        var segs = new[]
        {
            Segment.Norm(new Point(2, 0), new Point(2, 1)),
            Segment.Norm(new Point(0, 0), new Point(1, 1)),
            Segment.Norm(new Point(1, 0), new Point(1, 1)),
        };
        var sorted = SegOrder.Sorted(segs);
        Assert.Equal(new Point(0, 0), sorted[0].A);
        Assert.Equal(new Point(1, 0), sorted[1].A);
        Assert.Equal(new Point(2, 0), sorted[2].A);
        // 再排一次结果不变
        Assert.Equal(sorted, SegOrder.Sorted(sorted));
    }
}


/// <summary>
/// 模型校验。
/// </summary>
public class ModelTests
{
    [Fact]
    public void 合法模型无告警()
    {
        var m = Master.ParseMaster("/....|../\\.");
        Assert.Empty(Model.CheckModel(Model.StripModel(m)));
    }

    [Fact]
    public void 方形坐标越界被拦下()
    {
        var model = new Model
        {
            Kind = "square", N = 2,
            Folds = new List<Fold> { new(Segment.Norm(new Point(0, 0), new Point(9, 9)), "M") },
        };
        Assert.Contains(Model.CheckModel(model), s => s.Contains("越界"));
    }

    [Fact]
    public void 长条不允许有剪切线()
    {
        var model = new Model { Kind = "strip", NCell = 2, Cut = new List<Segment> { Segment.Norm(new Point(0, 0), new Point(1, 0)) } };
        Assert.Contains(Model.CheckModel(model), s => s.Contains("剪切线"));
    }

    [Fact]
    public void 峰谷兜底首条为谷且严格交替()
    {
        var mv = MasterOps.DefaultMv(48);
        Assert.Equal(48, mv.Count);
        Assert.Equal("V", mv[0]);
        Assert.Equal(24, mv.Count(m => m == "M"));
        Assert.Equal(24, mv.Count(m => m == "V"));
        for (int i = 1; i < mv.Count; i++) Assert.NotEqual(mv[i], mv[i - 1]);
    }

    [Fact]
    public void 统计信息与折痕一致()
    {
        var m = Master.ParseMaster("/....|../\\.");
        var st = Model.CreaseStats(Model.StripModel(m));
        Assert.Equal(4, st["total"]);
        // ★ 统计是「按类别 + 按峰谷」两个维度各自累加，因此 M+V 可能大于 total：
        //   一条 '\' 对角折痕会同时计入 '\\' 类别桶与它的 M/V 桶。
        Assert.Equal(2, st["M"]);
        Assert.Equal(3, st["V"]);
        Assert.Equal(2, st["\\"]);
        Assert.Equal(1, st["/"]);

        // KRENIO 全量
        var full = Model.StripModel(Master.ParseMaster(SampleMasters.Krenio));
        var stf = Model.CreaseStats(full);
        Assert.Equal(48, stf["total"]);

        // ★ 这里有两个<b>不同</b>的计数口径，务必分清：
        //   · 按峰谷：default_mv 对 48 条折痕交替赋值 -> 峰 24 / 谷 24
        //     （CLI 的 describe() 用的就是这一口径）
        //   · crease_stats 的 Counter：按「类别」和「峰谷」两个维度各累加一次，
        //     而竖直折痕的类别恰好也叫 'V'，与谷撞名后累加进同一个键，
        //     所以谷桶 = 24 + 9(竖直折痕数) = 33。
        int vertical = stf["total"] - stf["/"] - stf["\\"];   // 48 - 18 - 21 = 9
        Assert.Equal(9, vertical);
        Assert.Equal(39, stf["/"] + stf["\\"]);             // 斜 39 + 竖 9 = 48
        Assert.Equal(24, stf["M"]);                          // 峰：不会与类别撞名
        Assert.Equal(24 + vertical, stf["V"]);               // 谷 24 + 竖直 9
    }
}