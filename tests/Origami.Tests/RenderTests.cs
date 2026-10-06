using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 渲染回归测试。
/// <para>
/// 这些断言守的都是<b>曾经静默出错</b>的地方：渲��「看起来有图」但背景/配色不对，
/// 肉眼容易放过，断言一加就立刻暴露。</para>
/// </summary>
public class RenderTests
{
    [Fact]
    public void 新建画布的背景色必须是请求的颜色()
    {
        // ★ 这条曾真实出错：ImageSharp 2.x 的 BackgroundColor 是「与现有像素混合」，
        //   而新建图像是透明黑，混白之后仍是黑色 —— 整张图背景变黑且不报错。
        using var r = Raster.Create(20, 10, Rgb.White);
        var px = r.ToRgbBytes();
        Assert.Equal(3, px.Length > 0 ? 3 : 0);           // 缓冲区非空
        for (int i = 0; i < 20 * 10; i++)
        {
            int o = i * 3;
            Assert.True(px[o] == 255 && px[o + 1] == 255 && px[o + 2] == 255,
                $"像素 ({i % 20},{i / 20}) 应为白，实际 ({px[o]},{px[o + 1]},{px[o + 2]})");
        }
    }

    [Fact]
    public void 画布尺寸与请求一致()
    {
        using var r = Raster.Create(37, 19, Rgb.Black);
        Assert.Equal(37, r.Width);
        Assert.Equal(19, r.Height);
    }

    [Fact]
    public void 画线会改变像素()
    {
        using var r = Raster.Create(40, 40, Rgb.White);
        r.DrawLine(2, 20, 37, 20, Rgb.Black, 3);
        var px = r.ToRgbBytes();
        int mid = (20 * 40 + 20) * 3;                       // 线中点
        Assert.True(px[mid] < 250, "画线后中点应为黑色");
    }

    [Fact]
    public void 填充为null时只描边不覆盖背景()
    {
        // ★ 只描边时不能把已画好的背景覆盖掉（否则整片变黑）
        using var r = Raster.Create(40, 40, Rgb.White);
        var tri = new List<Point> { new(5, 5), new(35, 5), new(20, 35) };
        r.FillPolygon(tri, null, Rgb.Black, 2);

        var px = r.ToRgbBytes();
        int inside = (20 * 40 + 20) * 3;                     // 三角形内部
        Assert.True(px[inside] > 200,
            $"fill=null 时三角形内部应保持白底，实际 {px[inside]},{px[inside + 1]},{px[inside + 2]}");
    }

    [Fact]
    public void 三种折痕图都能编码出合法PNG()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var strip = Model.StripModel(master);
        var square = Model.SquareModel(SquareOps.PlanFromMaster(master));

        foreach (var (name, model) in new[] { ("strip", strip), ("square", square) })
        {
            using var black = PngIO.RenderCreaseBlack(model);
            using var color = PngIO.RenderCreaseColor(model);
            foreach (var (suffix, img) in new[] { ("黑", black), ("彩色", color) })
            {
                var bytes = img.ToPngBytes();
                Assert.True(bytes.Length > 100, $"{name}/{suffix} PNG 过小");
                // PNG 签名
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 },
                             bytes.Take(8));
            }
        }
    }

    [Fact]
    public void 彩色折痕图包含四种规定颜色()
    {
        // 红(峰) / 蓝(谷) / 绿(剪切线) / 近黑(边框) 必须都出现
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var square = Model.SquareModel(SquareOps.PlanFromMaster(master));

        using var img = PngIO.RenderCreaseColor(square, unit: 20, pad: 6);
        var px = img.ToRgbBytes();

        bool red = false, blue = false, green = false, dark = false;
        for (int i = 0; i + 2 < px.Length; i += 3)
        {
            int r = px[i], g = px[i + 1], b = px[i + 2];
            if (r > 200 && g < 60 && b < 60) red = true;
            if (b > 200 && r < 60 && g < 60) blue = true;
            if (g > 200 && r < 60 && b < 60) green = true;
            if (r < 70 && g < 70 && b < 70) dark = true;
        }
        Assert.True(red, "缺少红色折痕（峰）");
        Assert.True(blue, "缺少蓝色折痕（谷）");
        Assert.True(green, "缺少绿色剪切线");
        Assert.True(dark, "缺少深色边框");
    }

    [Fact]
    public void 折叠成品保留层序与峰谷配色()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var fm = FoldOps.FoldSquare(SquareOps.PlanFromMaster(master));
        var mvseq = Enumerable.Range(0, fm.World.Count)
                              .Select(i => fm.MvMap.TryGetValue(i, out var v) ? v : Mv.Mountain)
                              .ToList();

        using var img = PngIO.RenderFolded(fm.World, unit: 12, rank: fm.Rank,
                                           mvSeq: mvseq, flipY: false);
        var px = img.ToRgbBytes();

        bool red = false, blue = false, white = false;
        for (int i = 0; i + 2 < px.Length; i += 3)
        {
            int r = px[i], g = px[i + 1], b = px[i + 2];
            if (r > 200 && g < 60 && b < 60) red = true;
            if (b > 200 && r < 60 && g < 60) blue = true;
            if (r > 240 && g > 240 && b > 240) white = true;
        }
        Assert.True(red && blue, "折叠成品应同时含峰(红)与谷(蓝)");
        Assert.True(white, "字母之间应保留白底，不应被填满");
    }

    /// <summary>
    /// 回归：<c>FoldStrip</c> 曾只填 <c>MvSeq</c> 而 <c>MvMap</c> 留空，
    /// 而 <c>RenderFolded</c> 优先读 <c>MvMap</c> —— 结果长条成品「上色」后
    /// <b>一片空白且不报任何错</b>。这里钉死两条折叠路径都能上色。
    /// </summary>
    [Theory]
    [InlineData("长条")]
    [InlineData("方形")]
    public void 两条折叠路径上色后都应有峰谷颜色(string path)
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var strip = Model.StripModel(master);
        var plan = SquareOps.PlanFromMaster(master);

        FoldedModel fm = path == "长条"
            ? FoldOps.FoldStrip(master, strip.Folds.Select(f => f.MvType).ToList(), orient: true)
            : FoldOps.FoldSquare(plan, orient: false);

        Assert.NotEmpty(fm.MvMap);
        Assert.Contains(Mv.Mountain, fm.MvMap.Values);
        Assert.Contains(Mv.Valley, fm.MvMap.Values);

        using var img = PngIO.RenderFolded(fm.World, unit: 12,
                                           rank: fm.Rank, mvMap: fm.MvMap,
                                           supersample: 2,
                                           flipY: path == "长条");
        bool red = false, blue = false;
        foreach (var (r, g, b) in Scan(img))
        {
            if (r > 190 && g < 70 && b < 70) red = true;
            if (b > 190 && r < 70 && g < 70) blue = true;
        }
        Assert.True(red && blue, $"{path}成品上色后应同时含峰(红)与谷(蓝)");
    }

    /// <summary>关闭上色时成品应是纯黑剪影，不能残留彩色。</summary>
    [Theory]
    [InlineData("长条")]
    [InlineData("方形")]
    public void 剪影模式只有黑白(string path)
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var strip = Model.StripModel(master);
        var plan = SquareOps.PlanFromMaster(master);

        FoldedModel fm = path == "长条"
            ? FoldOps.FoldStrip(master, strip.Folds.Select(f => f.MvType).ToList(), orient: true)
            : FoldOps.FoldSquare(plan, orient: false);

        using var img = PngIO.RenderFolded(fm.World, unit: 12, rank: fm.Rank,
                                           mvMap: null, outline: null, lw: 0,
                                           supersample: 2, flipY: path == "长条",
                                           forceFill: Rgb.Black);
        int black = 0, colored = 0;
        foreach (var (r, g, b) in Scan(img))
        {
            if (r < 70 && g < 70 && b < 70) black++;
            if ((r > 190 && g < 70 && b < 70) || (b > 190 && r < 70 && g < 70)) colored++;
        }
        Assert.True(black > 0, "剪影应有黑色填充");
        Assert.Equal(0, colored);
    }

    /// <summary>遍历像素的 (r,g,b) 三元组。</summary>
    static IEnumerable<(int R, int G, int B)> Scan(Raster img)
    {
        var px = img.ToRgbBytes();
        for (int i = 0; i + 2 < px.Length; i += 3)
            yield return (px[i], px[i + 1], px[i + 2]);
    }

    /// <summary>
    /// SVG 构建：三个生成器都必须接受上色开关，且关掉上色后<b>不含任何彩色</b>。
    /// <para>预览走SVG（矢量、无损放大），所以这条路径的正确性和 PNG 渲染同等重要 ——
    /// 而且它曾经真的坏过：图元包在 &lt;g&gt; 里，解析端只遍历 root.Elements()
    /// 会把整个 &lt;g&gt; 漏掉，表现为「纸底画出来了、线条一条都没有」。</para>
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 折痕图SVG的上色开关有效(bool color)
    {
        var m = Model.StripModel(Master.ParseMaster(SampleMasters.Krenio));
        string svg = SvgIO.ModelToSvg(m, 24, 14, 1.2, color: color);
        Assert.Contains("<svg", svg);
        Assert.Contains("<line", svg);

        bool hasRed = svg.Contains("#f00", StringComparison.OrdinalIgnoreCase);
        bool hasBlue = svg.Contains("#00f", StringComparison.OrdinalIgnoreCase);
        Assert.Equal(color, hasRed || hasBlue);
    }

    /// <summary>成品 SVG：上色时含峰谷两种颜色，关闭时全黑（剪影）。</summary>
    [Fact]
    public void 成品SVG的上色开关有效()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var fm = FoldOps.FoldSquare(SquareOps.PlanFromMaster(master), orient: false);

        string on = SvgIO.FoldedToSvg(fm.World, fm.Rank, fm.MvMap, color: true, flipY: false);
        string off = SvgIO.FoldedToSvg(fm.World, fm.Rank, null, color: false, flipY: false);

        Assert.Contains("#f00", on, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("#00f", on, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<polygon", off);
        Assert.DoesNotContain("#f00", off, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#00f", off, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 原子片分解：长条与方形<b>各自独立</b>（折叠路径不同，图也必须不同），
    /// 且颜色确定 —— 同一母版两次生成必须逐字节相同，才能拿它做回归对比。
    /// </summary>
    [Fact]
    public void 两条路径的原子片分解图都存在且可复现()
    {
        var master = Master.ParseMaster(SampleMasters.Krenio);
        var plan = SquareOps.PlanFromMaster(master);
        var stripFm = FoldOps.FoldStrip(master,
            Model.StripModel(master).Folds.Select(f => f.MvType).ToList(), orient: true);
        var squareFm = FoldOps.FoldSquare(plan, orient: false);

        string s1 = SvgIO.PiecesToSvg(stripFm.World, 24, 16, flipY: true);
        string q1 = SvgIO.PiecesToSvg(squareFm.World, 24, 16, flipY: false);

        Assert.Contains("<polygon", s1);
        Assert.Contains("<polygon", q1);
        Assert.Equal(s1, SvgIO.PiecesToSvg(stripFm.World, 24, 16, flipY: true));
        Assert.Equal(q1, SvgIO.PiecesToSvg(squareFm.World, 24, 16, flipY: false));

        // 两条路径的面数不同 -> 图必然不同，否则说明取错了折叠结果
        Assert.NotEqual(CountPolys(s1), CountPolys(q1));
    }

    static int CountPolys(string svg)
        => svg.Split("<polygon").Length - 1;

    /// <summary>逆向歧义检测：方形 CP 能还原出几个等价母版。</summary>
    /// <para>
    /// 母版末尾的「素格 <c>.</c> + 尾部 pipe」在方形 CP 里不可见，所以方形图
    /// 原则上<b>不能唯一确定</b>母版。界面导入时要靠这个计数提醒用户。
    /// </para>
    /// </summary>
    [Fact]
    public void 方形CP逆向会报出候选个数()
    {
        var plan = SquareOps.PlanFromMaster(Master.ParseMaster(SampleMasters.Krenio));
        var r = Reverse.SquareToMasterAmbiguous(plan, strict: true);

        Assert.True(r.Candidates >= 1, "至少应有一个能正向复现的候选");
        Assert.Equal(100, r.Master.NCell);
        Assert.Equal(SampleMasters.Krenio, r.Master.Text.Trim());

        // 再走一遍「方形 SVG -> 模型 -> 逆向」这条界面实际走的路径
        var model = Model.SquareModel(plan);
        string svg = SvgIO.ModelToSvg(model, 48, 48, 1.6, color: true);
        var viaModel = Reverse.SquareToMasterAmbiguous(
            Model.SquarePlanFromModel(SvgIO.SvgToModel(svg, strict: false)), strict: true);
        Assert.Equal(r.Candidates, viaModel.Candidates);
        Assert.Equal(r.Master.Text.Trim(), viaModel.Master.Text.Trim());
    }

    /// <summary>黑白 SVG 上色后仍保留 data-mv —— 它是数据，去了上色也不能丢。</summary>
    /// <para>
    /// 这条断言守着界面「导入黑白图时把上色开关关掉」的判定依据：判据只能是
    /// <b>描边色</b>，不能是 data-mv，否则黑白图会被误判成彩色。
    /// </para>
    /// </summary>
    [Fact]
    public void 黑白SVG保留dataMv但没有彩色描边()
    {
        var m = Model.StripModel(Master.ParseMaster(SampleMasters.Krenio));
        string plain = SvgIO.ModelToSvg(m, 24, 14, 1.2, color: false);

        Assert.Contains("data-mv=", plain);          // 峰谷数据仍在
        Assert.DoesNotContain("#f00", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#00f", plain, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("#0f0", plain, StringComparison.OrdinalIgnoreCase);

        // 但描边一律是纯黑 —— 这才是「判为无色」的依据
        foreach (var line in plain.Split("\n").Where(t => t.Contains("<line")))
            Assert.Contains("#000000", line, StringComparison.OrdinalIgnoreCase);
    }
}
