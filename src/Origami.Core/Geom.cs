namespace Origami.Core;

/// <summary>
/// 几何与平面图基础。
/// <para>
/// 坐标约定：本库统一使用<b>数学坐标系（y 向上）</b>。母版字符串里 <c>/</c> 表示
/// 折痕 <c>(k,0)-&gt;(k+1,1)</c>（左下→右上），<c>\</c> 表示 <c>(k,1)-&gt;(k+1,0)</c>
/// （左上→右下）。因此单个 <c>/</c> 格顺次折叠后得到 <c>{(0,0),(0,1),(1,1)}</c>，
/// 在 y 向上的坐标系里正是左上三角 ◤。
/// </para>
/// <para>渲染到图片/屏幕时必须翻转 y：<c>screen_y = ymax - y</c>。</para>
/// </summary>
public static class Geom
{
    public const double Eps = 1e-9;
    public const double EpsLoose = 1e-6;

    // ---------------------------------------------------------------- 线段

    /// <summary>线段类别：<c>V</c> 竖直 / <c>H</c> 水平 / 对角（<c>/</c> 与 <c>\</c>）。</summary>
    public static string SegClass(Point a, Point b)
    {
        if (Math.Abs(a.X - b.X) < Eps) return "V";
        if (Math.Abs(a.Y - b.Y) < Eps) return "H";
        return (a.X - b.X) * (a.Y - b.Y) < 0 ? "/" : "\\";
    }

    /// <summary>带容差的规范化键，用于浮点坐标比对（对应 <c>seg_key</c>）。</summary>
    public static Segment SegKey(Point a, Point b)
    {
        var qa = new Point(Round6(a.X) + 0.0, Round6(a.Y) + 0.0);
        var qb = new Point(Round6(b.X) + 0.0, Round6(b.Y) + 0.0);
        return qa <= qb ? new Segment(qa, qb) : new Segment(qb, qa);
    }

    /// <summary>保留 6 位小数（四舍六入五成双，<c>ToEven</c> 即默认行为）。</summary>
    public static double Round6(double v) => Math.Round(v, 6, MidpointRounding.ToEven);

    /// <summary>坐标取整（用于 JSON 输出）。</summary>
    public static int RoundInt(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    public static HashSet<Segment> SegSet(IEnumerable<Segment> segs)
    {
        var s = new HashSet<Segment>();
        foreach (var x in segs) s.Add(x);
        return s;
    }

    // ---------------------------------------------------------------- 多边形

    /// <summary>鞋带公式面积（取绝对值）。</summary>
    public static double PolyArea(IReadOnlyList<Point> poly)
    {
        int n = poly.Count;
        if (n < 3) return 0.0;
        double s = 0;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            s += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(s) / 2.0;
    }

    /// <summary>有符号面积：逆时针为正（y 向上坐标）。</summary>
    public static double PolySignedArea(IReadOnlyList<Point> poly)
    {
        int n = poly.Count;
        if (n < 3) return 0.0;
        double s = 0;
        for (int i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            s += a.X * b.Y - b.X * a.Y;
        }
        return s / 2.0;
    }

    /// <summary>顶点平均值（对面片足够且稳定，替代面积重心）。</summary>
    public static Point PolyCentroid(IReadOnlyList<Point> poly)
    {
        double sx = 0, sy = 0;
        foreach (var p in poly) { sx += p.X; sy += p.Y; }
        return new Point(sx / poly.Count, sy / poly.Count);
    }

    public static (double X0, double Y0, double X1, double Y1) PolyBbox(
        IEnumerable<IReadOnlyList<Point>> polys)
    {
        double x0 = double.MaxValue, y0 = double.MaxValue;
        double x1 = double.MinValue, y1 = double.MinValue;
        foreach (var f in polys)
            foreach (var p in f)
            {
                if (p.X < x0) x0 = p.X;
                if (p.Y < y0) y0 = p.Y;
                if (p.X > x1) x1 = p.X;
                if (p.Y > y1) y1 = p.Y;
            }
        return (x0, y0, x1, y1);
    }

    /// <summary>射线法判断点是否在多边形内部。</summary>
    public static bool PointInPoly(Point pt, IReadOnlyList<Point> poly, double eps = Eps)
    {
        double x = pt.X, y = pt.Y;
        bool inside = false;
        int n = poly.Count;
        for (int i = 0; i < n; i++)
        {
            var p1 = poly[i];
            var p2 = poly[(i + 1) % n];
            if ((p1.Y > y + eps) != (p2.Y > y + eps))
            {
                double xin = p1.X + (y - p1.Y) / (p2.Y - p1.Y) * (p2.X - p1.X);
                if (x < xin) inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>去掉相邻重复顶点与共线的冗余顶点。</summary>
    public static List<Point> DedupPoly(IReadOnlyList<Point> poly, double eps = Eps)
    {
        var o = new List<Point>();
        foreach (var p in poly)
        {
            if (o.Count == 0 || Math.Abs(p.X - o[^1].X) > eps || Math.Abs(p.Y - o[^1].Y) > eps)
                o.Add(p);
        }
        while (o.Count > 1 && Math.Abs(o[0].X - o[^1].X) <= eps && Math.Abs(o[0].Y - o[^1].Y) <= eps)
            o.RemoveAt(o.Count - 1);

        if (o.Count < 3) return o;

        bool changed = true;
        while (changed && o.Count > 3)
        {
            changed = false;
            var res = new List<Point>();
            int n = o.Count;
            for (int i = 0; i < n; i++)
            {
                var a = o[(i - 1 + n) % n];
                var b = o[i];
                var c = o[(i + 1) % n];
                double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (Math.Abs(cross) < Eps) { changed = true; continue; }
                res.Add(b);
            }
            o = res.Count > 0 ? res : o;
        }
        return o;
    }

    // ---------------------------------------------------------------- 反射

    /// <summary>沿直线 ab 的 2D 反射，返回 3x3 齐次矩阵。</summary>
    public static Mat3 ReflectMatrix(Point a, Point b)
    {
        double ax = a.X, ay = a.Y;
        double dx = b.X - ax, dy = b.Y - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-12) return Mat3.Identity;

        double ux = dx / len, uy = dy / len;
        double r00 = 2 * ux * ux - 1, r01 = 2 * ux * uy;
        double r10 = 2 * ux * uy, r11 = 2 * uy * uy - 1;
        double tx = ax - (r00 * ax + r01 * ay);
        double ty = ay - (r10 * ax + r11 * ay);
        return new Mat3(r00, r01, tx, r10, r11, ty, 0, 0, 1);
    }

    public static List<Point> ApplyPoly(Mat3 m, IReadOnlyList<Point> poly)
    {
        var o = new List<Point>(poly.Count);
        foreach (var p in poly) o.Add(m.Apply(p));
        return o;
    }

    // ---------------------------------------------------------------- 平面图

    /// <summary>
    /// 平面直线图 -&gt; 所有<b>有限</b>面片（半边遍历）。
    /// <para>过滤外部无限面必须用<b>有符号面积</b>：正 = 有限内部面，负 = 那个绕整张纸
    /// 外圈的巨大面。用绝对值会把它留下来。</para>
    /// <para><paramref name="simplify"/> 默认 <c>false</c>，即<b>保留共线顶点</b>。
    /// 这不是美观问题而是正确性问题：折痕端点若落在某条边中间，删掉共线顶点会让该折痕
    /// 在面片边界上「消失」，对偶图随之丢边甚至断开。</para>
    /// <para>遍历起点按<b>字典序最小</b>的线段确定，使面片顺序只依赖输入几何，
    /// 不依赖任何哈希表的内部顺序 —— 输出因此可复现。</para>
    /// </summary>
    public static List<List<Point>> HalfedgeFaces(
        IEnumerable<Segment> cut, IEnumerable<Segment> fold, IEnumerable<Segment> border,
        double tol = Eps, bool simplify = false)
    {
        // 所有线段去重（值语义），再按字典序排序 —— 排序让后续遍历完全确定
        var all = new HashSet<Segment>();
        foreach (var s in cut) all.Add(s);
        foreach (var s in fold) all.Add(s);
        foreach (var s in border) all.Add(s);
        var edges = SegOrder.Sorted(all);

        var adj = new Dictionary<Point, HashSet<Point>>();
        foreach (var (a, b) in edges)
        {
            if (!adj.TryGetValue(a, out var na)) adj[a] = na = new HashSet<Point>();
            if (!adj.TryGetValue(b, out var nb)) adj[b] = nb = new HashSet<Point>();
            na.Add(b);
            nb.Add(a);
        }

        static double Ang(Point u, Point v) => Math.Atan2(v.Y - u.Y, v.X - u.X);

        // 每个顶点的邻点按极角排序；同角时按字典序，保证环顺序唯一确定
        var ring = new Dictionary<Point, List<Point>>();
        foreach (var (u, vs) in adj)
        {
            var lst = new List<Point>(vs);
            lst.Sort((p, q) =>
            {
                int c = Ang(u, p).CompareTo(Ang(u, q));
                return c != 0 ? c : Point.Compare(p, q);
            });
            ring[u] = lst;
        }

        // next(半边) = 到达 v 后, 在 v 的角度环里取 v->u 的前一条 -> 保持面在左
        var nxt = new Dictionary<(Point, Point), (Point, Point)>();
        var halfEdges = new List<(Point, Point)>();
        foreach (var u in SegOrder.SortedPoints(adj.Keys))
        {
            foreach (var v in SegOrder.SortedPoints(adj[u]))
            {
                var lst = ring[v];
                int i = lst.IndexOf(u);
                int prev = ((i - 1) % lst.Count + lst.Count) % lst.Count;
                var e = (u, v);
                nxt[e] = (v, lst[prev]);
                halfEdges.Add(e);
            }
        }

        var seen = new HashSet<(Point, Point)>();
        var faces = new List<List<Point>>();
        foreach (var e0 in halfEdges)
        {
            if (seen.Contains(e0)) continue;
            var f = new List<Point>();
            var e = e0;
            while (seen.Add(e))
            {
                f.Add(e.Item1);
                e = nxt[e];
            }
            if (PolySignedArea(f) > tol)
                faces.Add(simplify ? DedupPoly(f) : Canonicalize(f));
        }
        return faces;
    }

    /// <summary>
    /// 把多边形顶点旋转到<b>字典序最小的顶点开头</b>。旋转不影响多边形本身，
    /// 但规范化能让输出只依赖几何，不依赖遍历顺序。
    /// </summary>
    public static List<Point> Canonicalize(List<Point> poly)
    {
        int best = 0;
        for (int i = 1; i < poly.Count; i++)
            if (Point.Compare(poly[i], poly[best]) < 0) best = i;
        var o = new List<Point>(poly.Count);
        for (int i = 0; i < poly.Count; i++) o.Add(poly[(best + i) % poly.Count]);
        return o;
    }

    /// <summary>建立 边 -> [面下标] 的索引（边用 <see cref="SegKey"/> 的 6 位容差规范键）。</summary>
    public static Dictionary<Segment, List<int>> EdgeFaceIndex(
        IReadOnlyList<List<Point>> faces)
    {
        var ef = new Dictionary<Segment, List<int>>();
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            for (int k = 0; k < f.Count; k++)
            {
                var key = SegKey(f[k], f[(k + 1) % f.Count]);
                if (!ef.TryGetValue(key, out var l)) ef[key] = l = new List<int>();
                l.Add(i);
            }
        }
        return ef;
    }

    /// <summary>由折痕边建立对偶图，返回 <c>(dual, creaseOf)</c>。</summary>
    public static (Dictionary<int, List<int>>, Dictionary<(int, int), Segment>) BuildDual(
        IReadOnlyList<List<Point>> faces, IEnumerable<Segment> fold)
    {
        var ef = EdgeFaceIndex(faces);
        var fk = new HashSet<Segment>();
        foreach (var s in fold) fk.Add(s);

        var dual = new Dictionary<int, List<int>>();
        var creaseOf = new Dictionary<(int, int), Segment>();
        // 按边键排序遍历，使 dual 的邻接顺序只依赖几何
        foreach (var e in SegOrder.Sorted(ef.Keys))
        {
            var fl = ef[e];
            if (fl.Count == 2 && fk.Contains(e))
            {
                int a = fl[0], b = fl[1];
                if (!dual.TryGetValue(a, out var la)) dual[a] = la = new List<int>();
                if (!dual.TryGetValue(b, out var lb)) dual[b] = lb = new List<int>();
                la.Add(b);
                lb.Add(a);
                creaseOf[(Math.Min(a, b), Math.Max(a, b))] = e;
            }
        }
        return (dual, creaseOf);
    }

    // ---------------------------------------------------------------- 坐标转换

    /// <summary>
    /// 平面坐标 -&gt; 屏幕像素坐标（屏幕 y 向下）。
    /// <para><c>flipY=true</c>：输入是<b>数学坐标（y 向上）</c>，渲染时翻转；
    /// <c>flipY=false</c>：输入已是<b>屏幕坐标（y 向下）</c>，直接映射。</para>
    /// <para>只有一种组合能得到正立的成品：<b>长条</b>用数学坐标 -&gt; <c>true</c>；
    /// <b>方形</b>的 CP 与折叠结果都用屏幕坐标 -&gt; <c>false</c>。</para>
    /// </summary>
    public static (List<List<Point>> Screen, (double X0, double Y0, double X1, double Y1) Bbox,
                   (int W, int H) Size) ToScreen(
        IReadOnlyList<List<Point>> polys, double cell, double pad = 0.0, bool flipY = true)
    {
        var (x0, y0, x1, y1) = PolyBbox(polys);
        int W = (int)((x1 - x0) * cell) + (int)(2 * pad);
        int H = (int)((y1 - y0) * cell) + (int)(2 * pad);

        var screen = new List<List<Point>>(polys.Count);
        foreach (var f in polys)
        {
            var o = new List<Point>(f.Count);
            foreach (var p in f)
            {
                double sx = pad + (p.X - x0) * cell;
                double sy = flipY ? pad + (y1 - p.Y) * cell : pad + (p.Y - y0) * cell;
                o.Add(new Point(sx, sy));
            }
            screen.Add(o);
        }
        return (screen, (x0, y0, x1, y1), (W, H));
    }

    public static (Point, Point) SegToScreen(Segment seg,
        (double X0, double Y0, double X1, double Y1) bbox, double cell, double pad = 0.0)
    {
        var (x0, _, x1, _) = bbox;
        return (new Point(pad + (seg.A.X - x0) * cell, pad + (x1 - seg.A.Y) * cell),
                new Point(pad + (seg.B.X - x0) * cell, pad + (x1 - seg.B.Y) * cell));
    }
}