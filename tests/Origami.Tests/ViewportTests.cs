using Avalonia;
using Origami.Desktop;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 预览视口的缩放 / 平移运算。
/// <para>
/// 这组测试的由来是一个真实缺陷：缩放锚点公式里的倍率比写反了
/// （<c>before/after</c>，应为 <c>after/before</c>），肉眼只会觉得
/// 「缩放没以光标为中心」，很容易当成偏好问题放过。核心断言
/// <see cref="缩放后光标下的图像点仍在原处"/> 直接以「屏幕坐标反解用户坐标」
/// 的方式验证，把语义钉死而不是复述公式。</para>
/// </summary>
public class ViewportTests
{
    /// <summary>屏幕坐标 -&gt; 用户坐标，与 <c>Viewport</c> 的约定一致。</summary>
    static double ToUser(double screen, double pan, double scale) => (screen - pan) / scale;

    [Fact]
    public void 缩放后光标下的图像点仍在原处()
    {
        // 图 100×100，视口 500×500，适应窗口倍率 5（初始 pan=0）
        double before = 5.0, pan = 0.0;
        var anchor = new[] { 0.0, 137.0, 250.0, 499.0 };   // 边缘与内部若干光标位置

        foreach (var cx in anchor)
        {
            foreach (double factor in new[] { 0.5, 0.87, 1.15, 4.0 })
            {
                double after = before * factor;
                double pan2 = Viewport.AnchorPan(pan, cx, before, after);

                double u1 = ToUser(cx, pan, before);
                double u2 = ToUser(cx, pan2, after);

                Assert.Equal(u1, u2, 9);
            }
        }
    }

    [Fact]
    public void 锚点缩放在有平移时同样成立()
    {
        // 已经拖到图的一角：pan 必须是负数，否则这个测试没覆盖到平移分支
        double before = 2.0, pan = -137.5;
        double cx = 321.0, after = before * 1.7;

        double pan2 = Viewport.AnchorPan(pan, cx, before, after);

        Assert.Equal(ToUser(cx, pan, before), ToUser(cx, pan2, after), 9);
    }

    [Fact]
    public void 缩小与放大互为逆运算()
    {
        double before = 3.0, pan = -40.0, cx = 260.0;

        double up = Viewport.AnchorPan(pan, cx, before, before * 2);
        double back = Viewport.AnchorPan(up, cx, before * 2, before);

        Assert.Equal(pan, back, 9);
    }

    [Fact]
    public void 缩放围绕光标时图像不会朝反方向跑()
    {
        // 抓住方向这个具体症状：光标在右边，放大后图必须往左让位（pan 变负）
        double pan = Viewport.AnchorPan(0.0, 450.0, 1.0, 2.0);
        Assert.True(pan < 0, $"光标在右侧放大，平移量应变负，实际 {pan}");
    }

    [Fact]
    public void 倍率为零时不动()
    {
        // 视口还没布局完（Bounds=0）时会走到这里，不能产生 NaN / Infinity
        Assert.Equal(12.0, Viewport.AnchorPan(12.0, 100.0, 0.0, 2.0));
        Assert.Equal(12.0, Viewport.AnchorPan(12.0, 100.0, 2.0, 0.0));
    }

    [Fact]
    public void 适应窗口取宽高约束的较小者()
    {
        // 100:1 的长条折痕图放进 800×600 视口：宽度是瓶颈
        Assert.Equal(8.0, Viewport.FitScale(new Size(800, 600), new Size(100, 1)), 9);
        // 方形图放进很扁的视口：高度是瓶颈
        Assert.Equal(6.0, Viewport.FitScale(new Size(800, 600), new Size(100, 100)), 9);
    }

    [Fact]
    public void 适应窗口对退化尺寸返回一()
    {
        // Bounds 尚未布局（0×0）时不能除零
        Assert.Equal(1.0, Viewport.FitScale(new Size(0, 0), new Size(100, 100)));
        Assert.Equal(1.0, Viewport.FitScale(new Size(800, 600), new Size(0, 100)));
    }

    [Fact]
    public void 图比视口小时居中()
    {
        Assert.Equal(50.0, Viewport.ClampPan(pan: 999, viewportSize: 100, contentSize: 0));
        Assert.Equal(45.0, Viewport.ClampPan(pan: 0, viewportSize: 100, contentSize: 10));
        Assert.Equal(45.0, Viewport.ClampPan(pan: -999, viewportSize: 100, contentSize: 10));
    }

    [Fact]
    public void 图比视口大时不允许拖出视野()
    {
        // 内容 300 宽、视口 100 宽：平移量只能落在 [-200, 0]
        Assert.Equal(0.0, Viewport.ClampPan(50, viewportSize: 100, contentSize: 300));
        Assert.Equal(-200.0, Viewport.ClampPan(-999, viewportSize: 100, contentSize: 300));
        Assert.Equal(-120.0, Viewport.ClampPan(-120, viewportSize: 100, contentSize: 300));
    }

    /// <summary>
    /// 缩放前后传入的倍率必须是<b>同一个量纲</b>：都是「换算后实际倍率」
    /// （<c>FitScale × _zoom</c>），而不是「相对倍率」<c>_zoom</c>。
    /// </summary>
    /// <remarks>
    /// ★ 这个坑踩过一次，而且极其隐蔽：<c>AnchorPan</c> 的公式和单元测试都是对的，
    /// 但调用处把 <c>before = EffectiveScale</c> 和 <c>after = _zoom</c> 传了进去 ——
    /// 两者量纲不同（后者少了 FitScale），相除得到 <c>factor / FitScale</c>。
    /// 而长条折痕图的 FitScale 只有 0.45，看起来就只是「缩放幅度略微不对」，
    /// 极难与「锚点公式错了」区分开。所以在这里把量纲关系钉死。
    /// </remarks>
    [Fact]
    public void 相对倍率与实际倍率不可混用()
    {
        const double fitScale = 0.44975;   // 长条折痕图铺满宽度时的实际倍率
        const double zoom = 2.0;           // 相对「适应窗口」放大了 2 倍
        const double anchor = 700.5, pan = -30.0;

        double effective = fitScale * zoom;

        // 正确用法：两端都是实际倍率 -> 缩放后光标点不动
        double okPan = Viewport.AnchorPan(pan, anchor, fitScale * zoom, fitScale * zoom * 1.15);
        double u1 = ToUser(anchor, pan, effective);
        double u2 = ToUser(anchor, okPan, effective * 1.15);
        Assert.Equal(u1, u2, 9);

        // 错误用法：一端传了相对倍率 -> 结果明显不同
        double badPan = Viewport.AnchorPan(pan, anchor, effective, zoom * 1.15);
        Assert.NotEqual(okPan, badPan, 6);
    }
}