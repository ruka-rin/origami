using Origami.Core;
using Origami.Desktop;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 画布方向（旋转 / 镜像）。
/// <para>
/// 这组测试针对的是<b>一致性</b>而非单个函数：同一个
/// <see cref="CanvasTransform"/> 必须让<b>预览 SVG、导出的 SVG、导出的 PNG</b>
/// 三者呈现完全相同的方向。方向功能天然容易「三处各写一遍、然后慢慢跑偏」，
/// 而跑偏的表现还特别隐蔽 —— 图还在，只是转错了角度。</para>
/// </summary>
public class CanvasTransformTests
{
    const double W = 100, H = 50;      // 故意用非正方形，旋转后宽高互换才看得出来

    /// <summary>四种旋转 × 两个镜像开关之外，再加几组混合，共 16 种全覆盖。</summary>
    public static TheoryData<int, bool, bool> AllCombos()
    {
        var d = new TheoryData<int, bool, bool>();
        for (int q = 0; q < 4; q++)
            foreach (bool fx in new[] { false, true })
                foreach (bool fy in new[] { false, true })
                    d.Add(q, fx, fy);
        return d;
    }

    // ---------------------------------------------------------------- 矩阵不变式

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void 四角恰好映满新画布四角(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);
        var (rw, rh) = t.SizeOf(W, H);

        var corners = new[] { (0.0, 0.0), (W, 0.0), (W, H), (0.0, H) }
                     .Select(c => t.Apply(new Point(c.Item1, c.Item2), W, H))
                     .ToList();

        var want = new[] { (0.0, 0.0), (rw, 0.0), (rw, rh), (0.0, rh) };
        var got = corners.Select(p => (p.X, p.Y)).ToList();

        Assert.Equal(4, got.Distinct().Count());
        foreach (var p in want)
            Assert.Contains(p, got);
    }

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void 变换是等距的_内容不被缩放(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);
        var (rw, rh) = t.SizeOf(W, H);

        // 正交变换：行列式绝对值恒为 1
        var (a, b, c, d, _, _) = t.Matrix(W, H);
        Assert.Equal(1.0, Math.Abs(a * d - b * c), 9);

        // 面积守恒：|det| × 原面积 = 新画布面积
        Assert.Equal(W * H, rw * rh, 6);
    }

    [Fact]
    public void 顺时针与逆时针互为逆变换()
    {
        var pts = new[] { new Point(0, 0), new Point(W, 0), new Point(W, H), new Point(0, H) };

        for (int q = 1; q <= 3; q++)
        {
            var cw = CanvasTransform.Create(q);
            var ccw = CanvasTransform.Create(-q);

            foreach (var p in pts)
            {
                var mid = cw.Apply(p, W, H);
                // ★ 逆变换必须用<b>变换后</b>的画布尺寸：转 90° 后宽高互换了，
                //   仍按原尺寸求逆会整体偏移（这正是上面那个 bug 的镜像版）
                var (mw, mh) = cw.SizeOf(W, H);
                var back = ccw.Apply(mid, mw, mh);

                Assert.Equal(p.X, back.X, 6);
                Assert.Equal(p.Y, back.Y, 6);
            }
        }
    }

    [Fact]
    public void 转满四次回原样()
    {
        var t = CanvasTransform.Identity;
        for (int i = 0; i < 4; i++) t = t.RotateCw();

        Assert.True(t.IsIdentity);
        var (a, b, c, d, e, f) = t.Matrix(W, H);
        Assert.Equal(1.0, a, 9); Assert.Equal(0.0, b, 9);
        Assert.Equal(0.0, c, 9); Assert.Equal(1.0, d, 9);
        Assert.Equal(0.0, e, 9); Assert.Equal(0.0, f, 9);
    }

    [Fact]
    public void 镜像两次回原样()
    {
        // ★ MirrorX(bool) 是<b>设置</b>而不是「切换」：默认参数为 true，
        //   连写两次 MirrorX() 仍然是开启的，得显式传 false 才对消。
        Assert.True(CanvasTransform.Identity.MirrorX().FlipX);
        Assert.True(CanvasTransform.Identity.MirrorX(false).IsIdentity);
        Assert.True(CanvasTransform.Identity.MirrorY().MirrorY(false).IsIdentity);
    }

    [Fact]
    public void 旋转次数归一化()
    {
        Assert.Equal(0, CanvasTransform.Create(4).QuarterTurns);
        Assert.Equal(3, CanvasTransform.Create(-1).QuarterTurns);
        Assert.Equal(1, CanvasTransform.Create(5).QuarterTurns);
        Assert.Equal(2, CanvasTransform.Create(-2).QuarterTurns);
    }

    // ---------------------------------------------------------------- SVG 侧

    /// <summary>
    /// ★ 包裹内容的 <c>&lt;g&gt;</c> 必须与 <c>&lt;svg&gt;</c> 处于<b>同一个命名空间</b>。
    /// <para>
    /// 用 <c>XElement("g")</c> 建出来的是无命名空间的元素，序列化会写成
    /// <c>&lt;g xmlns=""&gt;</c> —— 语法完全合法，但 SVG 里它<b>不属于 SVG 命名空间，
    /// 浏览器直接不渲染</b>：点一下「旋转」整张图就白屏，且文件看不出任何毛病。
    /// 这条测试守的就是它。
    /// </para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCombos))]
    public void 变换后的分组不带空命名空间(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);
        string outSvg = SvgTransform.Apply(SampleSvg(), t);

        // 恒等变换按约定原样返回，不包分组
        if (t.IsIdentity) { Assert.Equal(SampleSvg(), outSvg); return; }

        Assert.DoesNotContain("xmlns=\"\"", outSvg);

        // 按 SVG 命名空间查那个分组：它必须真的在命名空间里（否则会被浏览器丢掉）
        var xdoc = System.Xml.Linq.XDocument.Parse(outSvg);
        var ns = xdoc.Root!.Name.Namespace;
        Assert.Equal("http://www.w3.org/2000/svg", ns.NamespaceName);
        Assert.NotEmpty(xdoc.Descendants(ns + "g"));
    }

    [Theory]
    [MemberData(nameof(AllCombos))]
    public void 施加变换后画布尺寸正确(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);
        var (rw, rh) = t.SizeOf(W, H);

        var doc = SvgDocument.Parse(SvgTransform.Apply(SampleSvg(), t));

        Assert.Equal(rw, doc.ViewBox.Width, 6);
        Assert.Equal(rh, doc.ViewBox.Height, 6);
    }

    /// <summary>
    /// ★ 预览必须<b>真的</b>把 <c>&lt;g transform&gt;</c> 施加到坐标上。
    /// <para>这是最容易漏的一环：<c>viewBox</c> 已经是变换后的尺寸，若解析端忽略
    /// transform，图形就还是原始朝向 —— 画面既没转又被裁掉一半。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCombos))]
    public void 预览确实施加了变换(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);
        var plain = SvgDocument.Parse(SampleSvg());
        var moved = SvgDocument.Parse(SvgTransform.Apply(SampleSvg(), t));

        // 图元集合不变（只是被搬动了）
        Assert.Equal(plain.Nodes.Count, moved.Nodes.Count);

        // 纸底矩形必须与新画布同尺寸
        var bg = moved.Nodes.OfType<SvgRect>().First();
        Assert.Equal(moved.ViewBox.Width, bg.W, 6);
        Assert.Equal(moved.ViewBox.Height, bg.H, 6);

        // 恒等变换时图元坐标应逐一不变
        if (t.IsIdentity)
        {
            var a = plain.Nodes.OfType<SvgLine>().First();
            var b = moved.Nodes.OfType<SvgLine>().First();
            Assert.Equal(a.A.X, b.A.X, 9); Assert.Equal(a.A.Y, b.A.Y, 9);
        }
    }

    [Fact]
    public void 不识别的变换关键字要报错而不是静默忽略()
    {
        // 静默忽略会画出一张比例全错的图，用户却以为是自己看错了
        const string bad = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">"
                         + "<g transform=\"rotate(90)\"><rect x=\"0\" y=\"0\" width=\"10\" height=\"10\" fill=\"#000\"/></g></svg>";

        var ex = Assert.Throws<FormatException>(() => SvgDocument.Parse(bad));
        Assert.Contains("matrix", ex.Message);
    }

    [Fact]
    public void 恒等变换原样返回()
    {
        var svg = SampleSvg();
        Assert.Same(svg, SvgTransform.Apply(svg, CanvasTransform.Identity));
    }

    [Fact]
    public void 变换后仍能被读回为数据模型()
    {
        // 方向只影响呈现，不影响数据 —— 这是把变换放在显示层的前提
        var model = Views.Build(SampleMasters.Krenio);
        var svg = Views.BuildSvg(model, ViewKind.Strip, ColorMode.Peaks,
                                 CanvasTransform.Create(1));

        var back = Origami.Core.SvgIO.SvgToModel(svg, strict: false);
        Assert.Equal(model.Strip.Folds.Count, back.Folds.Count);
    }

    // ---------------------------------------------------------------- PNG 侧

    /// <summary>
    /// PNG 与 SVG 必须给出<b>同一个方向</b>。
    /// <para>两侧是两条独立实现（位图重排 vs. 仿射矩阵），任何一侧算错都会
    /// 造成「屏幕上是 A、导出是 B」。这里用同一张图分别走两条路，
    /// 比对红线所在的<b>相对位置</b>。</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(AllCombos))]
    public void PNG与SVG方向一致(int q, bool fx, bool fy)
    {
        var t = CanvasTransform.Create(q, fx, fy);

        using var r = Raster.Create((int)W, (int)H, new Rgb(255, 255, 255));
        r.DrawLine(0, 1, W, 1, new Rgb(255, 0, 0), 2);      // 红线在顶部
        r.Transform(t);

        var px = r.ToRgbBytes();
        var red = Centroid(px, r.Width, r.Height, (R, G, B) => R > 200 && G < 90 && B < 90);

        // 尺寸（宽高互换）必须一致
        var (rw, rh) = t.SizeOf(W, H);
        Assert.Equal(rw, r.Width, 3);
        Assert.Equal(rh, r.Height, 3);

        // 红线在 SVG 侧的相对纵向位置
        var doc = SvgDocument.Parse(SvgTransform.Apply(SampleSvg(), t));
        var redLine = doc.Nodes.OfType<SvgLine>().First(l => l.Stroke.R > 200);
        double svgRelY = (redLine.A.Y + redLine.B.Y) / 2 / doc.ViewBox.Height;
        double svgRelX = (redLine.A.X + redLine.B.X) / 2 / doc.ViewBox.Width;

        double pngRelY = red!.Value.Y / (r.Height - 1);
        double pngRelX = red.Value.X / (r.Width - 1);

        Assert.Equal(svgRelX, pngRelX, 1);
        Assert.Equal(svgRelY, pngRelY, 1);
    }

    [Fact]
    public void 旋转会真的改变像素位置()
    {
        // 红线原本横画在<b>顶部</b>；顺时针 90° 后它应变成竖线贴在<b>右边缘</b>。
        // ★ 必须看X 方向的相对位置：转完之后红线是竖直的，
        //   它的 Y 重心必然落在中间（0.5），拿 Y 去断言「贴边」是错的。
        static (double RelX, double RelY) RedCentroid(Raster r)
        {
            var px = r.ToRgbBytes();
            double sx = 0, sy = 0; int n = 0;
            for (int y = 0; y < r.Height; y++)
                for (int x = 0; x < r.Width; x++)
                {
                    int i = (y * r.Width + x) * 3;
                    if (px[i] > 200 && px[i + 1] < 90 && px[i + 2] < 90) { sx += x; sy += y; n++; }
                }
            return n == 0 ? (-1, -1) : (sx / n / (r.Width - 1), sy / n / (r.Height - 1));
        }

        using var before = Raster.Create((int)W, (int)H, new Rgb(255, 255, 255));
        before.DrawLine(0, 1, W, 1, new Rgb(255, 0, 0), 2);
        var b = RedCentroid(before);

        using var after = Raster.Create((int)W, (int)H, new Rgb(255, 255, 255));
        after.DrawLine(0, 1, W, 1, new Rgb(255, 0, 0), 2);
        after.Transform(CanvasTransform.Create(1));
        var a = RedCentroid(after);

        // 转之前：横贯全宽、贴着上边缘
        Assert.True(b.RelY < 0.05, $"旋转前红线应贴近上边缘，实际 {b.RelY:0.###}");
        Assert.True(b.RelX is > 0.45 and < 0.55, $"旋转前红线应横贯中部，实际 {b.RelX:0.###}");

        // 转之后：竖贯全高、贴着右边缘（画布宽高已互换）
        Assert.True(a.RelX > 0.85, $"旋转后红线应贴近右边缘，实际 {a.RelX:0.###}");
        Assert.True(a.RelY is > 0.45 and < 0.55, $"旋转后红线应竖贯中部，实际 {a.RelY:0.###}");
    }

    // ---------------------------------------------------------------- 工具

    static (double X, double Y)? Centroid(byte[] px, int w, int h,
                                          Func<int, int, int, bool> pred)
    {
        double sx = 0, sy = 0; int n = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = (y * w + x) * 3;
                if (pred(px[i], px[i + 1], px[i + 2])) { sx += x; sy += y; n++; }
            }
        return n == 0 ? null : (sx / n, sy / n);
    }

    /// <summary>一张能区分方向的测试图：红线在上边、蓝图在左边、白底铺满。</summary>
    static string SampleSvg() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
      + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 100 50\" width=\"100px\" height=\"50px\">\n"
      + "  <rect x=\"0\" y=\"0\" width=\"100\" height=\"50\" fill=\"#ffffff\"/>\n"
      + "  <line x1=\"0\" y1=\"0\" x2=\"100\" y2=\"0\" stroke=\"#ff0000\" stroke-width=\"2\"/>\n"
      + "  <line x1=\"0\" y1=\"0\" x2=\"0\" y2=\"50\" stroke=\"#0000ff\" stroke-width=\"2\"/>\n"
      + "</svg>\n";
}