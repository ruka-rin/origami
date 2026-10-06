namespace Origami.Core;

/// <summary>
/// 母版字符串的解析与长条几何。
/// <para>
/// 母版字符串语义：<c>/</c>、<c>\</c>、<c>.</c> 各占一格，<c>/</c> 与 <c>\</c> 是该格内的
/// <b>对角折痕</b>，<c>.</c> 是无折痕的素格；<c>|</c> <b>不占格</b>，只表示「前一格与后一格
/// 之间有一条竖直折痕」。去掉所有 <c>|</c> 后剩下的字符数 = 纸带总格数。
/// </para>
/// <para>坐标系：数学坐标（y 向上）。格 <c>k</c> 占 <c>[k,k+1]×[0,1]</c>。
/// <c>/</c> 折痕从 <c>(k,0)</c> 到 <c>(k+1,1)</c>；<c>\</c> 从 <c>(k,1)</c> 到 <c>(k+1,0)</c>；
/// pipe <c>k</c> 的折痕是 <c>(k,0)-(k,1)</c>。</para>
/// <para>★ 长条是一张<b>连续纸带</b>：没有 <c>|</c> 的格边界上根本没有线（纸是连续的），
/// 只有 <c>|</c> 才是竖直折痕。这一点是整条工具链的地基。</para>
/// </summary>
public sealed class Master
{
    public const char BS = '\\';
    public const char Pipe = '|';
    private static readonly char[] DiagChars = { '/', '\\', '.' };

    public string Text { get; }
    public List<char> Cells { get; }
    public List<int> Pipes { get; }
    public HashSet<int> PipeSet { get; }

    public Master(string text)
    {
        Text = text;
        var cells = new List<char>();
        var pipes = new List<int>();
        var pipeset = new HashSet<int>();
        int k = 0;

        foreach (char ch in text)
        {
            if (ch == Pipe)
            {
                pipeset.Add(k);
            }
            else if (Array.IndexOf(DiagChars, ch) >= 0)
            {
                cells.Add(ch);
                k++;
            }
            else if (char.IsWhiteSpace(ch))
            {
                continue;
            }
            else
            {
                throw new ArgumentException(
                    $"母版含未知字符 '{ch}' (U+{(int)ch:X4})；只允许 / \\ . | 和空白");
            }
        }

        if (cells.Count == 0) throw new ArgumentException("母版为空");

        Cells = cells;
        PipeSet = pipeset;
        Pipes = pipeset.OrderBy(x => x).ToList();
    }

    public int NCell => Cells.Count;

    public List<char> DiagSymbols() => new(Cells);

    public override string ToString() => $"<Master {NCell}格 竖折痕{Pipes.Count}条>";

    /// <summary>
    /// 长条纸带几何 -&gt; <c>(cut, fold, border)</c>。
    /// <c>cut</c> 恒为空集（纸带连续）；<c>fold</c> = 对角折痕 + 竖直折痕；
    /// <c>border</c> = 上下两条长边 + 左右两个短边。
    /// </summary>
    public (HashSet<Segment> Cut, HashSet<Segment> Fold, HashSet<Segment> Border) StripGeometry()
    {
        int n = NCell;
        var cut = new HashSet<Segment>();
        var fold = new HashSet<Segment>();
        var border = new HashSet<Segment>();

        for (int k = 0; k < n; k++)
        {
            char c = Cells[k];
            if (c == '/') fold.Add(Segment.Norm(new Point(k, 0), new Point(k + 1, 1)));
            else if (c == BS) fold.Add(Segment.Norm(new Point(k, 1), new Point(k + 1, 0)));
            if (PipeSet.Contains(k + 1))
                fold.Add(Segment.Norm(new Point(k + 1, 0), new Point(k + 1, 1)));
        }

        for (int k = 0; k < n; k++)
        {
            border.Add(Segment.Norm(new Point(k, 0), new Point(k + 1, 0)));
            border.Add(Segment.Norm(new Point(k, 1), new Point(k + 1, 1)));
        }
        border.Add(Segment.Norm(new Point(0, 0), new Point(0, 1)));
        border.Add(Segment.Norm(new Point(n, 0), new Point(n, 1)));

        return (cut, fold, border);
    }

    /// <summary>
    /// <b>行进顺序</b>下的折痕列表 —— 从起笔端（最左）沿纸带走到底依次遇到的折痕，
    /// 与几何排列无关。这个顺序是折痕朝向与层序的唯一决定者。
    /// <para>返回 <c>(creases, kinds, cellIds)</c>；<c>kinds</c> 为 <c>D</c>/<c>V</c>，
    /// <c>cellIds</c> 为该折痕所在格号（竖直折痕记为右邻格）。</para>
    /// </summary>
    public (List<Segment> Creases, List<char> Kinds, List<int> CellIds) CreasesInOrder()
    {
        var creases = new List<Segment>();
        var kinds = new List<char>();
        var cellIds = new List<int>();

        for (int k = 0; k < NCell; k++)
        {
            char c = Cells[k];
            if (c == '/')
            {
                creases.Add(Segment.Norm(new Point(k, 0), new Point(k + 1, 1)));
                kinds.Add('D'); cellIds.Add(k);
            }
            else if (c == BS)
            {
                creases.Add(Segment.Norm(new Point(k, 1), new Point(k + 1, 0)));
                kinds.Add('D'); cellIds.Add(k);
            }
            if (PipeSet.Contains(k + 1))
            {
                creases.Add(Segment.Norm(new Point(k + 1, 0), new Point(k + 1, 1)));
                kinds.Add('V'); cellIds.Add(k + 1);
            }
        }
        return (creases, kinds, cellIds);
    }

    public static Master ParseMaster(string text) => new(text);
}

/// <summary>
/// 母版 -&gt; 顺次折叠所需的链式面片序列：把纸带切成面片（由折痕分开），
/// 再按「从起笔端沿链行进」排序。返回 <c>(faces, creases, kinds)</c>。
/// </summary>
public static class MasterOps
{
    public static (List<List<Point>> Faces, List<Segment> Creases, List<char> Kinds)
        ChainFromMaster(Master master)
    {
        var (cut, fold, border) = master.StripGeometry();
        var faces = Geom.HalfedgeFaces(cut, fold, border);
        if (fold.Count == 0) return (faces, new List<Segment>(), new List<char>());

        var (dual, creaseOf) = Geom.BuildDual(faces, fold);

        // 对偶图必须是链（路径，恰好 2 个叶子）
        var deg = new int[faces.Count];
        for (int i = 0; i < faces.Count; i++)
            deg[i] = dual.TryGetValue(i, out var l) ? l.Count : 0;
        var distinct = deg.Distinct().Where(d => d is 1 or 2).Count();
        int nLeaf1 = deg.Count(d => d == 1);
        if (distinct != deg.Distinct().Count() || nLeaf1 != 2)
        {
            var hist = deg.GroupBy(d => d).OrderBy(g => g.Key)
                         .ToDictionary(g => g.Key, g => g.Count());
            throw new InvalidOperationException(
                $"纸带对偶图不是链(度分布 {string.Join(", ", hist.Select(kv => $"{kv.Key}:{kv.Value}"))})，顺次滚动折叠不适用");
        }

        // 起笔端 = 两个叶子中质心 x 较小者（纸带最左端）
        var leaves = Enumerable.Range(0, faces.Count).Where(i => deg[i] == 1).ToList();
        int root = leaves.MinBy(i => Geom.PolyCentroid(faces[i]).X)!;

        var order = new List<int> { root };
        // prev = -1 表示「无前驱」（面下标均 >= 0，故 -1 可作哨兵）
        int prev = -1, cur = root;
        while (true)
        {
            int? nxt = null;
            if (dual.TryGetValue(cur, out var nb))
            {
                foreach (int v in nb)
                    if (v != prev) { nxt = v; break; }
            }
            if (nxt is null) break;
            order.Add(nxt.Value);
            prev = cur; cur = nxt.Value;
        }
        if (order.Count != faces.Count)
            throw new InvalidOperationException($"纸带面片不连通: {order.Count}/{faces.Count}");

        var creases = new List<Segment>();
        var kinds = new List<char>();
        for (int i = 0; i + 1 < order.Count; i++)
        {
            int a = order[i], b = order[i + 1];
            var e = creaseOf[(Math.Min(a, b), Math.Max(a, b))];
            creases.Add(e);
            kinds.Add(Math.Abs(e.A.X - e.B.X) < Geom.Eps ? 'V' : 'D');
        }
        return (order.Select(i => faces[i]).ToList(), creases, kinds);
    }

    /// <summary>
    /// 没有外部资料时的峰谷兜底：沿纸带严格交替，<b>首条为谷(V)</b>。
    /// 这与已验证的权威 <c>长条.svg</c> 完全一致（48 条 / 24 峰 24 谷）。
    /// </summary>
    public static List<string> DefaultMv(int n)
    {
        var o = new List<string>(n);
        for (int i = 0; i < n; i++) o.Add(i % 2 == 0 ? "V" : "M");
        return o;
    }

    /// <summary>从 SVG 线段记录读峰谷，按位置匹配到折痕序列。</summary>
    public static List<string> MvFromSvgSegments(
        IReadOnlyList<(double X1, double Y1, double X2, double Y2, string Col)> records,
        IReadOnlyList<Segment> creases, double scale = 2.0, double ymax = 2.0)
    {
        var lut = new Dictionary<Segment, string>();
        foreach (var (x1, y1, x2, y2, col) in records)
        {
            var a = new Point(Geom.Round6(x1 / scale), Geom.Round6(ymax / scale - y1 / scale));
            var b = new Point(Geom.Round6(x2 / scale), Geom.Round6(ymax / scale - y2 / scale));
            string c = col.ToLowerInvariant();
            lut[Segment.Norm(a, b)] = c is "#f00" or "red" ? "M" : "V";
        }

        var outv = new List<string>();
        foreach (var e in creases)
        {
            var key = Segment.Norm(new Point(Geom.Round6(e.A.X), Geom.Round6(e.A.Y)),
                                   new Point(Geom.Round6(e.B.X), Geom.Round6(e.B.Y)));
            if (!lut.TryGetValue(key, out var mv))
                throw new InvalidOperationException($"SVG 中找不到折痕 {e}");
            outv.Add(mv);
        }
        return outv;
    }
}