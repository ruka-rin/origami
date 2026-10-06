namespace Origami.Core;

/// <summary>
/// 方形 CP 的数据容器与结构标签反推。铺排算法见 <see cref="Layout"/>。
/// <para>坐标系：方形 CP 使用与原题图一致的<b>屏幕坐标（y 向下）</b>，格 <c>(x,y)</c> 占
/// <c>[x,x+1]×[y,y+1]</c>。这与长条的数学坐标不同 —— 转换只发生在渲染层。</para>
/// </summary>
public sealed class SquarePlan
{
    public int N { get; set; }
    public List<char> Cells { get; set; } = new();
    public HashSet<int> Pipes { get; set; } = new();

    /// <summary><c>(x,y)</c> -&gt; 对角符号。</summary>
    public Dictionary<(int, int), char> Diag { get; set; } = new();
    /// <summary><c>(x,y)</c> -&gt; <c>V</c>。</summary>
    public Dictionary<(int, int), char> Vert { get; set; } = new();
    /// <summary><c>(x,y)</c> -&gt; True。</summary>
    public Dictionary<(int, int), bool> Horiz { get; set; } = new();
    /// <summary>水平剪切线段起点。</summary>
    public List<(int X, int Y)> CutH { get; set; } = new();
    public Dictionary<int, (int Start, int End)> PerRow { get; set; } = new();
    public (int X, int Y)? TailFill { get; set; }

    public HashSet<Segment> Cut { get; set; } = new();
    public HashSet<Segment> Fold { get; set; } = new();
    public HashSet<Segment> Border { get; set; } = new();

    /// <summary>峰谷表 <c>{线段: 'M'|'V'}</c> —— 供折痕图红蓝配色。</summary>
    public Dictionary<Segment, string> FoldMv { get; set; } = new();

    /// <summary>由线段表直接构造并推导 cut/fold/border（供测试与手工构造使用）。</summary>
    public static SquarePlan FromRawSets(int n, HashSet<Segment> cut, HashSet<Segment> fold,
                                         HashSet<Segment> border)
    {
        var p = new SquarePlan { N = n, Cut = cut, Fold = fold, Border = border };
        foreach (var ((x, y), sym) in p.Diag)
        {
            var (a, b) = sym == '/'
                ? (new Point(x, y + 1), new Point(x + 1, y))
                : (new Point(x, y), new Point(x + 1, y + 1));
            p.Fold.Add(Segment.Norm(a, b));
        }
        return p;
    }

    public override string ToString() =>
        $"<SquarePlan N={N} 折{Fold.Count} 剪{Cut.Count} 边{Border.Count}>";
}

public static class SquareOps
{
    /// <summary>母版 -&gt; <see cref="SquarePlan"/>（<b>唯一正确的生成路径</b>）。</summary>
    public static SquarePlan PlanFromMaster(Master master, int? n = null)
    {
        var (N, _, cut, fold, foldMv, _, border) = Layout.StripToSquare(master.Cells, master.PipeSet);
        var p = new SquarePlan
        {
            N = N,
            Cells = new List<char>(master.Cells),
            Pipes = new HashSet<int>(master.Pipes),
            Cut = cut,
            Fold = fold,
            Border = border,
            FoldMv = foldMv,
        };

        // 反推结构标签（diag / vert / horiz），供逆向铺排与渲染分类用
        foreach (var (a, b) in p.Fold)
        {
            double ax = a.X, ay = a.Y, bx = b.X, by = b.Y;
            if (Math.Abs(ax - bx) < Geom.Eps)                       // 竖直
                p.Vert[(Geom.RoundInt(Math.Min(ax, bx)), Geom.RoundInt(Math.Min(ay, by)))] = 'V';
            else if (Math.Abs(ay - by) < Geom.Eps)                  // 水平
                p.Horiz[(Geom.RoundInt(Math.Min(ax, bx)), Geom.RoundInt(ay))] = true;
            else                                                   // 对角
            {
                int x = Geom.RoundInt(Math.Min(ax, bx));
                int y = Geom.RoundInt(Math.Min(ay, by));
                // 屏幕坐标 y 向下：'/' 视觉上是 (x,y)->(x+1,y+1)
                p.Diag[(x, y)] = by > ay ? '/' : '\\';
            }
        }

        foreach (var (a, b) in p.Cut)
            p.CutH.Add((Geom.RoundInt(Math.Min(a.X, b.X)), Geom.RoundInt(Math.Min(a.Y, b.Y))));
        p.CutH.Sort();
        p.CutH = p.CutH.Distinct().OrderBy(t => t).ToList();
        return p;
    }

    /// <summary>
    /// 从已有线段表反查结构化信息（用于 SVG &lt;-&gt; JSON 往返与逆向铺排）。
    /// <para>纯线段表没有 <c>diag</c>/<c>vert</c> 的语义标签，但结构可以反推：</para>
    /// <list type="bullet">
    /// <item>竖折痕：记在 <c>(x,y)</c>，按位置区分 <c>B</c>/<c>T</c>/<c>P</c> 三类。</item>
    /// <item>水平折痕：记在 <c>(x,y)</c>。</item>
    /// <item>水平剪切：<c>(x,y)-(x+1,y)</c>，记起点 <c>(x,y)</c>。</item>
    /// </list>
    /// </summary>
    public static SquarePlan PlanFromSegments(int n, HashSet<Segment> cut,
                                              HashSet<Segment> fold, HashSet<Segment> border)
    {
        var p = new SquarePlan { N = n, Cut = cut, Fold = fold, Border = border };

        var turnCol = new HashSet<int>();
        for (int r = 1; r < n; r++) turnCol.Add(r % 2 == 1 ? n - 1 : 0);
        int step = n - 2;
        int JOf(int x, int r) => r % 2 == 0 ? x : n - 1 - x;

        var sortedFold = fold.ToList();
        sortedFold.Sort(static (x, y) =>
        {
            int c = x.A.CompareTo(y.A);
            return c != 0 ? c : x.B.CompareTo(y.B);
        });

        foreach (var seg in sortedFold)
        {
            int ax = Geom.RoundInt(seg.A.X), ay = Geom.RoundInt(seg.A.Y);
            int bx = Geom.RoundInt(seg.B.X), by = Geom.RoundInt(seg.B.Y);
            if (ax == bx)                                  // 竖直
            {
                int x = Math.Min(ax, bx), y = Math.Min(ay, by);
                if (turnCol.Contains(x)) p.Vert[(x, y)] = 'B';
                else if (x == 1 || (x == n - 1 && (y + 1) % 2 == 1)) p.Vert[(x, y)] = 'T';
                else p.Vert[(x, y)] = 'P';
            }
            else if (ay == by) p.Horiz[(Math.Min(ax, bx), ay)] = true;
            else p.Diag[(Math.Min(ax, bx), Math.Min(ay, by))] = by < ay ? '/' : '\\';
        }

        // ★ 转折列上的竖折痕可能同时是母版的 pipe。正向规则是「k 在 pipes 里就画竖折痕」，
        // 而转折列本来就要画竖折痕，两者重合时 P 会被 B/T 覆盖 —— 逆向就会丢掉这个 pipe。
        // 这里补记：只要 k 落在有效范围内就认为它是 pipe。
        foreach (var entry in p.Vert.ToList())
        {
            var tag = entry.Value;
            if (tag != 'B' && tag != 'T') continue;
            var xy = entry.Key;
            int x = xy.Item1, y = xy.Item2;
            int r = y;
            int k = step * r + JOf(x, r) + (r % 2 == 0 ? 0 : 1);
            if (1 <= k && k <= (n - 1) * step + n - 1) p.Pipes.Add(k);
        }

        foreach (var seg in SegOrder.Sorted(cut))
            if (Math.Abs(seg.A.Y - seg.B.Y) < Geom.Eps)
                p.CutH.Add((Geom.RoundInt(seg.A.X), Geom.RoundInt(seg.A.Y)));

        // 末尾回填：末行末格若有一条 '\' 对角且越界
        int rLast = n - 1;
        int xEnd = rLast % 2 == 0 ? n - 1 : 0;
        if (p.Diag.TryGetValue((xEnd, rLast), out var s2) && s2 == '\\')
            p.TailFill = (xEnd, rLast);

        return p;
    }
}