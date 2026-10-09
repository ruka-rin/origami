namespace Origami.Core;

/// <summary>
/// 母版开头的<b>方向标记</b>，与字模编码用的记号同一套（见 <see cref="FoldFont"/>）：
/// <c>@</c> —— 成品顺时针转 90°，取个数对 4 取模，只改画布；
/// <c>#</c> —— 整份母版上下翻转（<c>/</c> 与 <c>\</c> 互换），改的是折痕，
/// 铺排与折叠都跟着变。连写偶数个 <c>#</c> 互相抵消。
/// </summary>
/// <param name="QuarterTurns">顺时针 90° 的次数（0..3）。</param>
/// <param name="Flip">是否上下翻转。</param>
public readonly record struct MasterMarks(int QuarterTurns, bool Flip)
{
    /// <summary>没有标记。</summary>
    public static MasterMarks None => default;

    /// <summary>是否什么都没标。</summary>
    public bool IsNone => QuarterTurns == 0 && !Flip;

    /// <summary>写回母版串开头的那段文本。</summary>
    public string Prefix => new string(Master.RotateMark, QuarterTurns) + (Flip ? "#" : "");

    /// <summary>
    /// 该给画布施加的方向变换。翻转<b>不在这里</b> —— 它已改过折痕，再翻一次会抵消。
    /// </summary>
    public CanvasTransform Canvas => CanvasTransform.Create(QuarterTurns);
}

/// <summary>
/// 母版字符串的解析与长条几何。
/// <para>
/// 母版字符串语义：<c>/</c>、<c>\</c>、<c>.</c> 各占一格，<c>/</c> 与 <c>\</c> 是该格内的
/// <b>对角折痕</b>，<c>.</c> 是无折痕的素格；<c>|</c> <b>不占格</b>，只表示「前一格与后一格
/// 之间有一条竖直折痕」。去掉所有 <c>|</c> 后剩下的字符数 = 纸带总格数。
/// </para>
/// <para>
/// <b>开头</b>可以带一段<b>方向标记</b>（见 <see cref="MasterMarks"/>）；
/// 正文里再出现 <c>@</c>/<c>#</c> 即按非法字符处理。
/// 于是母版串<b>自带</b>展示方向，导出、保存、再粘贴回来都不会丢。
/// </para>
/// <para>坐标系：数学坐标（y 向上）。格 <c>k</c> 占 <c>[k,k+1]×[0,1]</c>。
/// <c>/</c> 折痕从 <c>(k,0)</c> 到 <c>(k+1,1)</c>；<c>\</c> 从 <c>(k,1)</c> 到 <c>(k+1,0)</c>；
/// pipe <c>k</c> 的折痕是 <c>(k,0)-(k,1)</c>。</para>
/// <para>长条是一张<b>连续纸带</b>：没有 <c>|</c> 的格边界上根本没有线（纸是连续的），
/// 只有 <c>|</c> 才是竖直折痕。这一点是整条工具链的地基。</para>
/// </summary>
public sealed class Master
{
    public const char BS = '\\';

    /// <summary>竖直折痕：<b>不占格</b>，插在两格之间。</summary>
    public const char Pipe = '|';

    /// <summary>方向标记之一：出现一次即成品转 90°，取总数对 4 取模。</summary>
    public const char RotateMark = '@';

    /// <summary>方向标记之一：把整份母版<b>上下翻转</b>（所有 <c>/</c> 与 <c>\</c> 互换）。</summary>
    public const char FlipMark = '#';

    private static readonly char[] DiagChars = { '/', '\\', '.' };

    public string Text { get; }

    /// <summary>开头那段<b>方向标记</b>（<c>@</c> 与 <c>#</c>）。</summary>
    public MasterMarks Marks { get; }

    public List<char> Cells { get; }
    public List<int> Pipes { get; }
    public HashSet<int> PipeSet { get; }

    public Master(string text)
    {
        Text = text;
        int p = 0;
        int turns = 0;
        bool flip = false;
        while (p < Text.Length && (Text[p] == RotateMark || Text[p] == FlipMark))
        {
            if (Text[p] == RotateMark) turns++;
            else flip = !flip;
            p++;
        }
        Marks = new MasterMarks(turns % 4, flip);
        RawBody = Text[p..];

        var cells = new List<char>();
        var pipes = new List<int>();
        var pipeset = new HashSet<int>();
        int k = 0;

        for (int i = p; i < Text.Length; i++)
        {
            char ch = Text[i];
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
                    $"母版含未知字符 '{ch}' (U+{(int)ch:X4})；只允许 / \\ . | 和空白，" +
                    $"开头另可有 {RotateMark}（旋转）与 {FlipMark}（翻转）");
            }
        }

        if (cells.Count == 0) throw new ArgumentException("母版为空");

        // 翻转标记落在<b>折痕本身</b>：整条纸带上下翻转（y -> 1-y），
        // 每格的 / 与 \ 互换，格数与竖折痕位置不变。
        if (flip)
            cells = cells.Select(c => c == '/' ? BS : c == BS ? '/' : c).ToList();

        Cells = cells;
        PipeSet = pipeset;
        Pipes = pipeset.OrderBy(x => x).ToList();
    }

    /// <summary>
    /// 母版<b>原始</b>正文：去掉了开头的方向标记，但<b>没有</b>按标记翻转。
    /// <para>与 <see cref="Cells"/> 的区别只在「是否带着 <c>#</c> 的效果」——
    /// 想换一套标记重新解析时用这个，用 <see cref="Cells"/> 会被翻两次。</para>
    /// </summary>
    public string RawBody { get; }

    /// <summary>去掉开头方向标记后的正文（<b>已按</b> <see cref="Marks"/> 翻转）。</summary>
    public string Body => string.Concat(DiagSymbols());

    /// <summary>带上方向标记、可再被 <see cref="Master"/> 原样解析的写法。</summary>
    public string MarkedText => Marks.Prefix + Body;

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
    /// 这与已验证的权威 <c>长条.svg</c> 完全一致。
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