namespace Origami.Core;

/// <summary>
/// 长条母版 -&gt; 方形 CP 的<b>真实</b>铺排算法（<c>strip.js</c> 的逐行忠实移植）。
/// <para>
/// 这是唯一被权威题面图验证过的母版→方形规则。KRENIO 100 格 -&gt; N=11 折痕 80；
/// PROTEIN 118 格 -&gt; N=12 折痕 91；ABCD 76 格 -&gt; N=10 折痕 63，均逐条零差异。
/// </para>
/// <para><b>单位制：1 格 = 2 单位</b>（与参考实现一致），方形边长恒为 2 的倍数。</para>
/// <para><b>算法全貌</b>：
/// <list type="number">
/// <item>母版 -&gt; 折痕序列（<c>parseEnc</c>）：<c>/</c>→<c>{xb:x, xt:x+2}</c>，
/// <c>\</c>→<c>{xb:x+2, xt:x}</c>，<c>.</c>→<c>x+=2</c>，
/// <c>|</c>→追加 <c>{xb:x, xt:x}</c>（不前进）。列表首尾各有一个哨兵 <c>{0,0}</c>。</item>
/// <item>边长：<c>square = 2*ceil(1+sqrt(格数-1))</c>，<c>N = square/2</c>。</item>
/// <item>尾部填充：按「对角+竖直」交替补到 <c>total = 2*(N*(N-2)+2)</c>。</item>
/// <item>蛇形铺排 + 转折耳：每次翻行画 <b>4 条</b>结构线（缺一不可）。</item>
/// <item>行容量：<c>limit</c> 首行/末行 N-1 格，中间行 N-2 格。</item>
/// <item>转折后补偿：<c>if (yOffset) { xt+=2; xb+=2; limit+=2; }</c>。</item>
/// <item>左右交替：奇数行把 x 镜像 <c>x -&gt; square-x</c> 并交换 y1/y2。</item>
/// <item>峰谷：按<b>绘制顺序</b>严格交替，首条为蓝（谷）。</item>
/// <item>剪切线：<c>outerStroke</c> 的水平实线，整行贯通。</item>
/// </list>
/// </para>
/// </summary>
public static class Layout
{
    /// <summary>1 格 = 2 单位（与参考实现一致）。</summary>
    public const int Unit = 2;

    public const char Backslash = '\\';

    /// <summary>折痕端点（单位制）。</summary>
    public readonly record struct RawCrease(int Xb, int Xt);

    /// <summary>母版格符号 + pipe 集合 -&gt; 折痕序列 + 总宽 + 行高。</summary>
    public static (List<RawCrease> Creases, int Width, int Height) MasterToCreases(
        IReadOnlyList<char> cells, IEnumerable<int>? pipes)
    {
        var pipeset = pipes is null ? new HashSet<int>() : new HashSet<int>(pipes);
        var creases = new List<RawCrease> { new(0, 0) };
        int x = 0;

        for (int k = 0; k < cells.Count; k++)
        {
            char c = cells[k];
            if (c == '/') creases.Add(new RawCrease(x, x + Unit));
            else if (c == Backslash) creases.Add(new RawCrease(x + Unit, x));
            else if (c != '.') throw new ArgumentException($"未知母版符号 '{c}'");
            x += Unit;
            if (pipeset.Contains(k + 1)) creases.Add(new RawCrease(x, x));
        }
        creases.Add(new RawCrease(x, x));
        return (creases, x, Unit);
    }

    /// <summary>
    /// 母版格数 -&gt; 方形边长（单位制，2 的倍数）。
    /// <c>square = 2*ceil(1+sqrt(格数-1))</c>。
    /// <para>★ <c>N</c> 至少为 2：<c>N=1</c> 时行容量为 0，铺排状态机会死循环。</para>
    /// </summary>
    public static int ChooseSquare(int ncell)
    {
        if (ncell <= 1) return 2 * Unit;
        int n = (int)Math.Ceiling(1 + Math.Sqrt(ncell - 1));
        return Unit * Math.Max(n, 2);
    }

    /// <summary>方形能容纳的总长度（单位制）。</summary>
    public static int ChooseTotal(int n) => 2 * (n * (n - 2) + 2);

    /// <summary>各行容量（格数）：首末行 N-1，中间行 N-2。</summary>
    public static List<int> ChooseRowCaps(int n)
    {
        var caps = new List<int> { n - 1 };
        for (int i = 0; i < n - 2; i++) caps.Add(n - 2);
        caps.Add(n - 1);
        return caps;
    }

    /// <summary>
    /// 按「对角 + 竖直」交替补到 <c>total</c>。
    /// <para>收尾形态由循环退出点决定：
    /// 母版末条折痕是对角 -&gt; 填充以对角起头；是竖直 -&gt; 填充全是竖直（ABCD 型）；
    /// 余量为偶数格 -&gt; 成对填满。</para>
    /// </summary>
    public static List<RawCrease> PadCreases(List<RawCrease> creases, int total)
    {
        if (creases.Count == 0) return creases;

        var last = creases[^1];
        int x = Math.Max(last.Xt, last.Xb);
        var prev = (last.Xt == last.Xb && creases.Count >= 2) ? creases[^2] : last;
        int diag = 0;
        if (prev.Xt == x) diag = 1;
        else if (prev.Xb == x) diag = -1;

        // 死循环保护：x 必须严格递增
        int guard = 0;
        int limitGuard = (total - x) / Unit + 4;
        while (x < total)
        {
            if (guard > limitGuard)
                throw new InvalidOperationException(
                    $"尾部填充未收敛：x={x} total={total}（母版长度超出方形容量）");
            guard++;

            // ★ RawCrease 的构造顺序是 (Xb, Xt)，别写反：
            //   diag==1 表示「xt 在前、xb 在后」，即 xt = x、xb = x + UNIT。
            if (diag == 1) creases.Add(new RawCrease(x + Unit, x));
            else if (diag == -1) creases.Add(new RawCrease(x, x + Unit));
            x += Unit;

            if (x >= total) break;
            creases.Add(new RawCrease(x, x));
            diag = -diag;
        }
        return creases;
    }

    /// <summary>铺排结果（单位制坐标）。</summary>
    public sealed class SquareLayout
    {
        public int Square { get; init; }
        public int N { get; init; }
        public int Width { get; init; }
        public int YOffset { get; init; }
        /// <summary>绘制顺序下的折痕线段列表（峰谷交替依据）。</summary>
        public List<Segment> Order { get; init; } = new();
        /// <summary>线段 -&gt; 'M'(红/峰) | 'V'(蓝/谷)。</summary>
        public Dictionary<Segment, string> FoldMv { get; init; } = new();
        public HashSet<Segment> Cut { get; init; } = new();
        public HashSet<Segment> Border { get; init; } = new();
    }

    /// <summary>折痕序列 -&gt; 方形 CP 线段表（单位制，屏幕坐标 y 向下）。</summary>
    public static SquareLayout CreasesToSquare(List<RawCrease> creases, int width, int height = Unit)
    {
        int square = ChooseSquare(width / Unit);
        int n = square / Unit;
        creases = PadCreases(new List<RawCrease>(creases), ChooseTotal(n));

        // 绘制顺序：原始四元组 (x1,y1,x2,y2)，峰谷交替按此顺序
        var drawn = new List<(double X1, double Y1, double X2, double Y2)>();
        var cutOut = new List<(Point, Point)>();
        int yState = 0, xState = 0;

        // 剪切线：参考实现的 outerStroke 水平实线
        for (int y = 1; y < square; y++)
        {
            if (y % 2 != 0) continue;
            if (y % 4 == 0) cutOut.Add((new Point(Unit, y), new Point(square, y)));
            else cutOut.Add((new Point(0, y), new Point(square - Unit, y)));
        }

        int LimitOf(int yo) =>
            (yo == 0 || yo == square - Unit) ? square - Unit : square - 2 * Unit;

        void DrawCrease(int x1, int y1, int x2, int y2)
        {
            // 参考实现的 drawCrease：奇数行整体镜像 + 交换 y
            if (yState % (2 * Unit) == Unit)
            {
                x1 = square - x1; x2 = square - x2;
                (y1, y2) = (y2, y1);
            }
            drawn.Add((x1, y1, x2, y2));
        }

        foreach (var cr in creases)
        {
            if (cr.Xt == 0 && cr.Xb == 0) continue;           // 首哨兵

            int limit = LimitOf(yState);
            int xt = cr.Xt + xState;
            int xb = cr.Xb + xState;

            // 转折：画 4 条结构线，然后翻到下一行。
            // ★ afterTurn 在 yOffset 自增「之前」被调用，故用转折前的 yOffset 做镜像判断。
            char afterTurn = '\0';
            while (xt > limit || xb > limit)
            {
                if (xt == limit)
                {
                    DrawCrease(square - Unit, yState, square, yState + Unit);
                    afterTurn = 'L';
                }
                else if (xb == limit)
                {
                    DrawCrease(square, yState, square - Unit, yState + Unit);
                    afterTurn = 'R';
                }
                DrawCrease(square - Unit, yState + Unit, square, yState + Unit);   // ② 横档 H
                if (afterTurn == 'L')
                    DrawCrease(square - Unit, yState + 2 * Unit, square, yState + Unit);   // ③ 出耳 C
                else if (afterTurn == 'R')
                    DrawCrease(square, yState + 2 * Unit, square - Unit, yState + Unit);
                afterTurn = '\0';
                DrawCrease(square - Unit, yState + Unit, square - Unit, yState + 2 * Unit); // ④ 竖直 V

                xState -= limit;
                xt -= limit;
                xb -= limit;
                yState += Unit;
                limit = LimitOf(yState);
            }

            if (yState != 0)
            {
                // 折耳结构占掉一格，后续行整体右移一格
                xt += Unit;
                xb += Unit;
                limit += Unit;
            }

            if (xt == xb && xb == square) continue;
            DrawCrease(xt, yState, xb, yState + height);
        }

        // 峰谷：按绘制顺序严格交替，首条为蓝（谷）
        var foldMv = new Dictionary<Segment, string>();
        var order = new List<Segment>(drawn.Count);
        for (int i = 0; i < drawn.Count; i++)
        {
            var (x1, y1, x2, y2) = drawn[i];
            var key = Segment.Norm(new Point(x1, y1), new Point(x2, y2));
            foldMv[key] = i % 2 == 0 ? "V" : "M";
            order.Add(key);
        }

        var border = new HashSet<Segment>();
        for (int x = 0; x < square; x += Unit)
        {
            border.Add(Segment.Norm(new Point(x, 0), new Point(x + Unit, 0)));
            border.Add(Segment.Norm(new Point(x, square), new Point(x + Unit, square)));
        }
        for (int y = 0; y < square; y += Unit)
        {
            border.Add(Segment.Norm(new Point(0, y), new Point(0, y + Unit)));
            border.Add(Segment.Norm(new Point(square, y), new Point(square, y + Unit)));
        }

        var cut = new HashSet<Segment>();
        foreach (var (a, b) in cutOut) cut.Add(Segment.Norm(new Point(a.X, a.Y), new Point(b.X, b.Y)));

        return new SquareLayout
        {
            Square = square,
            N = n,
            Width = width,
            YOffset = yState,
            Order = order,
            FoldMv = foldMv,
            Cut = cut,
            Border = border,
        };
    }

    /// <summary>长条母版 -&gt; 方形 CP 线段表（<b>格坐标</b>，1 格 = 1.0）。</summary>
    public static (int N, int Width, HashSet<Segment> Cut, HashSet<Segment> Fold,
                   Dictionary<Segment, string> FoldMv, List<Segment> Order, HashSet<Segment> Border)
        StripToSquare(IReadOnlyList<char> cells, IEnumerable<int>? pipes)
    {
        var (creases, width, height) = MasterToCreases(cells, pipes);
        var r = CreasesToSquare(creases, width, height);
        double h = 1.0 / Unit;

        Point Cell4(Point s) => new(Geom.Round6(s.X * h), Geom.Round6(s.Y * h));
        Segment Seg2(Segment s) => Segment.Norm(Cell4(s.A), Cell4(s.B));

        HashSet<Segment> Explode(HashSet<Segment> segs)
        {
            var o = new HashSet<Segment>();
            foreach (var (a, b) in segs)
            {
                if (Math.Abs(a.X - b.X) < Geom.Eps)                       // 竖直
                {
                    var (y0, y1) = a.Y <= b.Y ? (a.Y, b.Y) : (b.Y, a.Y);
                    for (int y = (int)Math.Round(y0); y < (int)Math.Round(y1); y += Unit)
                        o.Add(Segment.Norm(new Point(a.X * h, Geom.Round6(y * h)),
                                           new Point(a.X * h, Geom.Round6((y + Unit) * h))));
                }
                else if (Math.Abs(a.Y - b.Y) < Geom.Eps)                  // 水平
                {
                    var (x0, x1) = a.X <= b.X ? (a.X, b.X) : (b.X, a.X);
                    for (int x = (int)Math.Round(x0); x < (int)Math.Round(x1); x += Unit)
                        o.Add(Segment.Norm(new Point(Geom.Round6(x * h), a.Y * h),
                                           new Point(Geom.Round6((x + Unit) * h), a.Y * h)));
                }
                else                                                        // 斜线：原样
                {
                    o.Add(Segment.Norm(new Point(a.X * h, a.Y * h), new Point(b.X * h, b.Y * h)));
                }
            }
            return o;
        }

        var fold = new HashSet<Segment>();
        foreach (var s in r.Order) fold.Add(Seg2(s));

        var foldMv = new Dictionary<Segment, string>();
        foreach (var (k, v) in r.FoldMv) foldMv[Seg2(k)] = v;

        return (r.N, r.Width / Unit, Explode(r.Cut), fold, foldMv,
                r.Order.Select(Seg2).ToList(), Explode(r.Border));
    }
}