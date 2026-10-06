namespace Origami.Core;

/// <summary>
/// 方形 CP 反推长条母版字符串 —— <b>逆向精确可逆</b>（不需要母版做参数）。
/// <para>既然正向铺排是完全已知的<b>确定性状态机</b>，逆向就能<b>逐步反演</b>它而不必猜。</para>
/// <para><b>逆向三步</b>：
/// <list type="number">
/// <item><b>读出转折点</b>：转折耳 4 条线里有一条是<b>水平横档</b>，而母版折痕永远是对角或
/// 竖直 —— 所以每条水平折痕唯一对应一个转折，其 <c>y-2</c> 就是转折时的 <c>yOffset</c>。</item>
/// <item><b>剔除耳线</b>：转折点确定后，耳线坐标完全确定，取并集与实际折痕求交即得耳线。</item>
/// <item><b>反算 raw 坐标与端点定向</b>：<c>xOffset(Y) = -Σlimit_of(t) (t&lt;Y)</c>，
/// <c>shift(Y) = xOffset(Y) + (2 if Y else 0)</c>；再<b>靠 y 坐标定向</b>
/// —— 不能按 x 大小定向，那样会把 '\' 误判成 '/'。</item>
/// </list> </para>
/// </summary>
public static class Reverse
{
    public const char Backslash = '\\';

    /// <summary>行容量（单位制）—— 与 <see cref="Layout"/> 同一公式。</summary>
    static int LimitOf(int square, int yoff)
        => (yoff == 0 || yoff == square - Layout.Unit)
            ? square - Layout.Unit
            : square - 2 * Layout.Unit;

    /// <summary>复现 <c>drawCrease</c> 的奇数行镜像：<c>x -&gt; square-x</c> 且交换 y。</summary>
    static (Point, Point) Mirror(int square, int yoff, Point p, Point q)
        => yoff % (2 * Layout.Unit) == Layout.Unit
            ? (new Point(square - p.X, q.Y), new Point(square - q.X, p.Y))
            : (p, q);

    static Segment Key(Point p, Point q)
        => Segment.Norm(new Point(p.X, p.Y), new Point(q.X, q.Y));

    /// <summary>转折点处转折耳的全部候选线（未镜像，单位制）。含入耳/出耳的两个分支。</summary>
    static List<(Point, Point)> EarCandidates(int square, int yoff)
    {
        int s2 = square - 2, s = square;
        int y0 = yoff, y1 = yoff + Layout.Unit, y2 = yoff + 2 * Layout.Unit;
        return new List<(Point, Point)>
        {
            (new Point(s2, y0), new Point(s, y1)),   // ① 入耳 D，分支 L（xt == limit）
            (new Point(s, y0), new Point(s2, y1)),   // ① 入耳 D，分支 R（xb == limit）
            (new Point(s2, y1), new Point(s, y1)),   // ② 横档 H
            (new Point(s2, y2), new Point(s, y1)),   // ③ 出耳 C，分支 L
            (new Point(s, y2), new Point(s2, y1)),   // ③ 出耳 C，分支 R
            (new Point(s2, y1), new Point(s2, y2)),  // ④ 竖直 V
        };
    }

    /// <summary>行内单条主折痕（已反算 raw 并定向）。</summary>
    public sealed class RowItem
    {
        public double Xb { get; init; }
        public double Xt { get; init; }
        /// <summary>raw 中点 —— 几何上唯一无歧义的行内排序键。</summary>
        public double Mid => Xb + Xt;
        public Segment Seg { get; init; }
    }

    /// <summary>方形 CP 的分解结果。</summary>
    public sealed class Decomposition
    {
        public int Square { get; init; }
        public int N { get; init; }
        /// <summary>转折点列表（<c>yOffset</c>，升序）。</summary>
        public List<int> Turns { get; init; } = new();
        public Dictionary<int, List<RowItem>> Rows { get; init; } = new();
        public HashSet<Segment> Ear { get; init; } = new();
        public HashSet<Segment> Main { get; init; } = new();
        public int NTurn => Turns.Count;
        public int NMain => Main.Count;
    }

    /// <summary>方形 CP -&gt; 转折点 + 主折痕（单位制，按行分组、已反算 raw）。</summary>
    public static Decomposition Decompose(SquarePlan plan)
    {
        int square = plan.N * Layout.Unit;

        // 格坐标 -> 单位制（格坐标 × UNIT）
        var fold = new HashSet<Segment>();
        foreach (var (a, b) in plan.Fold)
            fold.Add(Segment.Norm(new Point(a.X * Layout.Unit, a.Y * Layout.Unit),
                                  new Point(b.X * Layout.Unit, b.Y * Layout.Unit)));

        // 第 1 步：水平折痕 -> 转折点
        var horiz = fold.Where(k => Math.Abs(k.A.Y - k.B.Y) < Geom.Eps).ToList();
        var turns = horiz.Select(k => (int)Math.Round(k.A.Y) - Layout.Unit)
                         .Distinct().OrderBy(x => x).ToList();

        // 第 2 步：剔除耳线
        var ear = new HashSet<Segment>();
        foreach (int y in turns)
            foreach (var (p, q) in EarCandidates(square, y))
            {
                var (mp, mq) = Mirror(square, y, p, q);
                var k = Key(mp, mq);
                if (fold.Contains(k)) ear.Add(k);
            }
        var main = new HashSet<Segment>(fold);
        main.ExceptWith(ear);

        // 按行分组
        var rowsRaw = new Dictionary<int, List<Segment>>();
        foreach (var k in main)
        {
            int y = (int)Math.Round(Math.Min(k.A.Y, k.B.Y));
            if (!rowsRaw.TryGetValue(y, out var l)) rowsRaw[y] = l = new List<Segment>();
            l.Add(k);
        }

        // 第 3 步：反算 raw + 端点定向
        int ShiftOf(int y)
        {
            int xoff = 0;
            foreach (int t in turns) if (t < y) xoff -= LimitOf(square, t);
            return xoff + (y != 0 ? Layout.Unit : 0);
        }

        var outRows = new Dictionary<int, List<RowItem>>();
        foreach (int y in rowsRaw.Keys.OrderBy(x => x))
        {
            int sh = ShiftOf(y);
            bool mirrored = y % (2 * Layout.Unit) == Layout.Unit;
            var items = new List<RowItem>();
            foreach (var k in rowsRaw[y])
            {
                var (lo, hi) = k.A.Y < k.B.Y ? (k.A, k.B) : (k.B, k.A);
                double xt, xb;
                if (mirrored)
                {
                    // 镜像后：大 y 的是 sq-xt，小 y 的是 sq-xb
                    xt = square - hi.X - sh;
                    xb = square - lo.X - sh;
                }
                else
                {
                    xt = lo.X - sh;
                    xb = hi.X - sh;
                }
                items.Add(new RowItem { Xb = xb, Xt = xt, Seg = k });
            }
            // 行内排序键 = raw 中点 xb+xt 升序（折痕中点沿母版行进序严格递增）
            items.Sort((a, b) => a.Mid.CompareTo(b.Mid));
            outRows[y] = items;
        }

        return new Decomposition
        {
            Square = square,
            N = plan.N,
            Turns = turns,
            Rows = outRows,
            Ear = ear,
            Main = main,
        };
    }

    /// <summary>分解结果 -&gt; raw 折痕序列（行进序，不含哨兵，含尾部填充折痕）。</summary>
    public static List<(double Xb, double Xt)> RecoveredCreases(Decomposition dec)
    {
        var o = new List<(double, double)>();
        foreach (int y in dec.Rows.Keys.OrderBy(x => x))
            foreach (var it in dec.Rows[y]) o.Add((it.Xb, it.Xt));
        return o;
    }

    /// <summary>raw 折痕序列 + 格数 -&gt; 母版字符串。</summary>
    public static Master RawToMaster(List<(double Xb, double Xt)> raws, int ncell)
    {
        var cells = new char[ncell];
        Array.Fill(cells, '.');
        var pipes = new HashSet<int>();

        foreach (var c in raws)
        {
            int x1 = (int)Math.Round(c.Xb), x2 = (int)Math.Round(c.Xt);
            if (Math.Min(x1, x2) >= ncell * Layout.Unit) continue;      // 尾部填充折痕
            if (x1 == x2)
            {
                int k = x1 / Layout.Unit;
                if (1 <= k && k <= ncell - 1) pipes.Add(k);
            }
            else
            {
                int k = Math.Min(x1, x2) / Layout.Unit;
                if (0 <= k && k < ncell) cells[k] = x1 < x2 ? '/' : Backslash;
            }
        }

        var sb = new System.Text.StringBuilder();
        for (int k = 0; k < ncell; k++)
        {
            sb.Append(cells[k]);
            if (pipes.Contains(k + 1)) sb.Append('|');
        }
        return Master.ParseMaster(sb.ToString());
    }

    /// <summary>母版 -&gt; 方形折痕集合（<b>格坐标</b>，与 <c>plan.Fold</c> 同单位）。</summary>
    public static HashSet<Segment> FoldCells(Master master)
    {
        var (creases, width, h) = Layout.MasterToCreases(master.Cells, master.PipeSet);
        var r = Layout.CreasesToSquare(creases, width, h);
        var o = new HashSet<Segment>();
        foreach (var k in r.FoldMv.Keys)
            o.Add(Segment.Norm(new Point(k.A.X / Layout.Unit, k.A.Y / Layout.Unit),
                               new Point(k.B.X / Layout.Unit, k.B.Y / Layout.Unit)));
        return o;
    }

    public static HashSet<Segment> PlanFoldCells(SquarePlan plan)
    {
        var o = new HashSet<Segment>();
        foreach (var (a, b) in plan.Fold) o.Add(Segment.Norm(a, b));
        return o;
    }

    /// <summary>
    /// 方形 CP -&gt; 长条母版字符串（<b>精确逆向</b>）：逆向结果 + <b>是否有多个等价候选</b>。
    /// <para>按边长公式 <c>N = ceil(1+sqrt(M-1))</c> 反解得区间
    /// <c>M ∈ [(N-2)²+1, (N-1)²+1]</c>（<b>不是</b> <c>[(N-1)², N²]</c> —— 后者会漏掉
    /// PROTEIN 的 118），每个候选<b>正向重放</b>验证，取能逐条复现输入方形的那个。</para>
    /// </summary>
    /// <param name="Master">选出的母版（取第一个能正向复现的候选）。</param>
    /// <param name="Candidates">
    /// 能正向复现原方形的候选个数。<b>大于 1 就是歧义</b>：母版末尾的「素格 <c>.</c>
    /// + 尾部 pipe」在方形 CP 里不可见（<c>/ | .</c> 与 <c>/ |</c> 铺排逐条相同），
    /// 所以方形 CP 无法唯一确定母版串。
    /// </param>
    public sealed record SquareToMasterResult(Master Master, int Candidates, int NCellLo, int NCellHi);

    /// <summary>
    /// 方形 CP -&gt; 母版，并报告歧义。
    /// <para>与 <see cref="SquareToMaster"/> 同算法，区别是会数一遍到底有多少候选能正向复现。
    /// 界面导入方形 SVG 时需要据此提示「这个图对应多个母版」，否则用户会以为
    /// 拿到的串是唯一解。</para>
    /// </summary>
    public static SquareToMasterResult SquareToMasterAmbiguous(SquarePlan plan, bool strict = true)
    {
        var dec = Decompose(plan);
        int n = dec.N;
        var raws = RecoveredCreases(dec);
        var target = PlanFoldCells(plan);

        int lo = (n - 2) * (n - 2) + 1, hi = (n - 1) * (n - 1) + 1;
        Master? first = null;
        int valid = 0;

        for (int ncell = lo; ncell <= hi; ncell++)
        {
            var cand = RawToMaster(raws, ncell);
            if (!SetEq(FoldCells(cand), target)) continue;
            first ??= cand;                       // 取第一个真正能正向复现的
            valid++;
        }

        // 一个都没匹配上：退回第一个候选，让调用方看到「无法复现」而不是崩溃
        if (first is null) first = RawToMaster(raws, lo);

        if (strict && valid == 0)
            throw new InvalidOperationException(
                $"N={n} 的方形 CP 无法反推母版：格数区间 [{lo}, {hi}] 内没有候选能正向重放复现。" +
                $"转折 {dec.NTurn} 次、主折痕 {dec.NMain} 条。");

        return new SquareToMasterResult(
            first ?? throw new InvalidOperationException("无法构造候选母版"),
            valid, lo, hi);
    }

    public static Master SquareToMaster(SquarePlan plan, bool strict = true)
    {
        var dec = Decompose(plan);
        int n = dec.N;
        var raws = RecoveredCreases(dec);
        var target = PlanFoldCells(plan);

        int lo = (n - 2) * (n - 2) + 1, hi = (n - 1) * (n - 1) + 1;
        Master? fallback = null;
        for (int ncell = lo; ncell <= hi; ncell++)
        {
            var cand = RawToMaster(raws, ncell);
            fallback ??= cand;
            if (SetEq(FoldCells(cand), target)) return cand;
        }
        if (strict)
            throw new InvalidOperationException(
                $"N={n} 的方形 CP 无法反推母版：格数区间 [{lo}, {hi}] 内没有候选能正向重放复现。" +
                $"转折 {dec.NTurn} 次、主折痕 {dec.NMain} 条。");
        return fallback ?? throw new InvalidOperationException("无法构造候选母版");
    }

    static bool SetEq(HashSet<Segment> a, HashSet<Segment> b)
        => a.Count == b.Count && a.SetEquals(b);

    /// <summary>逆向 -&gt; 正向的闭环自检报告（不需要权威母版）。</summary>
    public sealed class RoundtripReport
    {
        public bool Ok { get; init; }
        public Master Master { get; init; } = null!;
        public int NCell { get; init; }
        public int NTurn { get; init; }
        public int NMain { get; init; }
        public int NEar { get; init; }
        public List<Segment> OnlyGen { get; init; } = new();
        public List<Segment> OnlyInput { get; init; } = new();
    }

    public static RoundtripReport VerifyRoundtrip(SquarePlan plan)
    {
        var dec = Decompose(plan);
        var back = SquareToMaster(plan, strict: false);
        var got = FoldCells(back);
        var target = PlanFoldCells(plan);
        var onlyGen = got.Except(target).Take(6).ToList();
        var onlyInput = target.Except(got).Take(6).ToList();
        return new RoundtripReport
        {
            Ok = SetEq(got, target),
            Master = back,
            NCell = back.NCell,
            NTurn = dec.NTurn,
            NMain = dec.NMain,
            NEar = dec.Ear.Count,
            OnlyGen = onlyGen,
            OnlyInput = onlyInput,
        };
    }

    /// <summary>逆向结果与权威母版的逐项差异（仅测试/诊断用）。</summary>
    public static (int NCellBack, int NCellRef, bool NCellMatch, List<int> SymbolDiff,
                   List<int> ExtraPipes, List<int> MissingPipes, bool TextExact, bool Exact)
        DiffToReference(Master back, Master reference)
    {
        int n = Math.Max(back.NCell, reference.NCell);
        var diff = new List<int>();
        for (int i = 0; i < n; i++)
        {
            char? a = i < back.NCell ? back.Cells[i] : null;
            char? b = i < reference.NCell ? reference.Cells[i] : null;
            if (a != b) diff.Add(i);
        }
        var backPipes = new HashSet<int>(back.Pipes);
        var refPipes = new HashSet<int>(reference.Pipes);
        bool exact = back.NCell == reference.NCell && diff.Count == 0 &&
                     backPipes.SetEquals(refPipes);
        return (back.NCell, reference.NCell, back.NCell == reference.NCell, diff,
                backPipes.Except(refPipes).OrderBy(x => x).ToList(),
                refPipes.Except(backPipes).OrderBy(x => x).ToList(),
                back.Text.Trim() == reference.Text.Trim(), exact);
    }
}