using System.Text;
using Origami.Core;

namespace Origami.Tests;

/// <summary>
/// 与旧参考实现比<b>形状</b>的公用工具。
/// <para>
/// 判据统一是<b>栅格化 IoU</b>：两边都归一化到单位方框、各自栅格成一张掩码再求交并比。
/// 它对「面片划分方式不同」「纸带高度差 2 倍」「坐标方向相反」都免疫 ——
/// 只比<b>最终纸张覆盖的形状</b>，而这正是用户看到的东西。
/// </para>
/// </summary>
public static class ShapeMatch
{
    public const int Grid = 72;

    /// <summary>
    /// 把一批多边形栅格化成 <see cref="Grid"/>² 的覆盖掩码（先归一化到单位框）。
    /// </summary>
    /// <param name="flipY">输入是否 y 向上（本项目折叠结果是数学坐标，要先翻成屏幕方向）。</param>
    public static bool[] Mask(List<List<(double X, double Y)>> polys, bool flipY)
    {
        var all = polys.SelectMany(p => p).ToList();
        double x0 = all.Min(p => p.X), x1 = all.Max(p => p.X);
        double y0 = all.Min(p => p.Y), y1 = all.Max(p => p.Y);
        double w = x1 - x0, h = y1 - y0;
        if (w < 1e-9) w = 1;
        if (h < 1e-9) h = 1;

        var np = polys.Select(f => f.Select(p => (
            (p.X - x0) / w,
            flipY ? 1 - (p.Y - y0) / h : (p.Y - y0) / h)).ToList()).ToList();

        var m = new bool[Grid * Grid];
        for (int gy = 0; gy < Grid; gy++)
            for (int gx = 0; gx < Grid; gx++)
            {
                double px = (gx + 0.5) / Grid, py = (gy + 0.5) / Grid;
                foreach (var f in np)
                    if (Inside(f, px, py)) { m[gy * Grid + gx] = true; break; }
            }
        return m;
    }

    static bool Inside(List<(double X, double Y)> f, double x, double y)
    {
        bool in_ = false;
        for (int i = 0, j = f.Count - 1; i < f.Count; j = i++)
            if ((f[i].Y > y) != (f[j].Y > y) &&
                x < (f[j].X - f[i].X) * (y - f[i].Y) / (f[j].Y - f[i].Y) + f[i].X)
                in_ = !in_;
        return in_;
    }

    public static double IoU(bool[] a, bool[] b)
    {
        int inter = 0, uni = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] && b[i]) inter++;
            if (a[i] || b[i]) uni++;
        }
        return uni == 0 ? 0 : (double)inter / uni;
    }

    public static List<List<(double X, double Y)>> ToPairs(List<List<Point>> polys)
        => polys.Select(f => f.Select(p => (p.X, p.Y)).ToList()).ToList();

    /// <summary>
    /// 把成品多边形按<b>画布方向</b>摆好 —— 与真实渲染管线同构。
    /// <para>次序必须是：数学坐标 -&gt; <c>flipY</c> 成屏幕坐标 -&gt; 施加
    /// <see cref="CanvasTransform"/>。先用数学坐标转，旋转的手性会反。</para>
    /// </summary>
    public static List<List<(double X, double Y)>> View(List<List<Point>> world, CanvasTransform xf)
    {
        var all = world.SelectMany(p => p).ToList();
        double x0 = all.Min(p => p.X), y0 = all.Min(p => p.Y);
        double w = all.Max(p => p.X) - x0, h = all.Max(p => p.Y) - y0;
        if (w < 1e-9) w = 1;
        if (h < 1e-9) h = 1;

        return world.Select(f => f.Select(p =>
        {
            double x = p.X - x0;
            double sy = h - (p.Y - y0);                  // flipY -> 屏幕坐标
            var q = xf.Apply(new Point(x, sy), w, h);
            return (q.X, q.Y);
        }).ToList()).ToList();
    }

    /// <summary>取出旧参考实现给某个字模/词的成品多边形（已是屏幕坐标）。</summary>
    /// <param name="scaled">基准文件是否用半格单位（单字模的 <c>legacy_letters.json</c> 是）。</param>
    public static List<List<(double X, double Y)>>? Legacy(string key)
    {
        foreach (var (file, scaled) in new[] { ("legacy_letters.json", true),
                                               ("legacy_multi.json", false) })
        {
            string p = Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(p)) continue;
            var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty(key, out var el)) continue;

            return el.GetProperty("polys").EnumerateArray().Select(pa =>
                pa.EnumerateArray().Select(q => (
                    q.GetProperty("x").GetDouble() / (scaled ? 2 : 1),
                    q.GetProperty("y").GetDouble() / (scaled ? 2 : 1))).ToList()).ToList();
        }
        return null;
    }

    /// <summary>本项目成品 vs 旧参考实现成品的重合度（都按 <paramref name="xf"/> 摆正后）。</summary>
    public static double Match(List<List<Point>> world, CanvasTransform xf, string key)
    {
        var want = Legacy(key);
        if (want is null) return 1.0;           // 基准里没有这个字模，跳过
        return IoU(Mask(want, flipY: false), Mask(View(world, xf), flipY: false));
    }
}
