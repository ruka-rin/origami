namespace Origami.Core;

/// <summary>折叠成品：世界坐标面片 + 层序 + 峰谷。</summary>
public sealed class FoldedModel
{
    public List<List<Point>> World { get; }
    public List<int> Rank { get; }
    public Dictionary<int, string> MvMap { get; }
    public List<string> MvSeq { get; }
    public List<List<Point>> FacesFlat { get; }
    public List<Segment> Creases { get; }
    /// <summary>每个独立纸片的 <c>(面下标列表, 起始层号)</c>。</summary>
    public List<(List<int> Faces, int StartRank)> Groups { get; }

    public FoldedModel(List<List<Point>> world, List<int> rank, Dictionary<int, string> mvMap,
                       List<string> mvSeq, List<List<Point>> facesFlat,
                       List<Segment> creases,
                       List<(List<int>, int)>? groups = null)
    {
        World = world;
        Rank = rank;
        MvMap = mvMap;
        MvSeq = mvSeq;
        FacesFlat = facesFlat;
        Creases = creases;
        Groups = groups ?? new List<(List<int>, int)> { (Enumerable.Range(0, world.Count).ToList(), 0) };
    }

    public FoldedModel(List<List<Point>> world, List<int> rank, Dictionary<int, string> mvMap,
                       List<List<Point>> facesFlat, List<Segment> creases,
                       List<(List<int>, int)>? groups = null)
        : this(world, rank, mvMap, new List<string>(), facesFlat, creases, groups)
    {
    }

    public FoldedModel(List<List<Point>> world, List<int> rank, List<string> mvSeq,
                       List<List<Point>> facesFlat, List<Segment> creases)
        : this(world, rank, new Dictionary<int, string>(), facesFlat, creases)
        => MvSeq = mvSeq;

    public int NLayers => Rank.Count == 0 ? 0 : Rank.Max() - Rank.Min() + 1;

    /// <summary>整体绕原点旋转 k 个 90°（k=1 逆时针）。</summary>
    public FoldedModel Rotate90(int k = 1)
    {
        k %= 4;
        if (k == 0) return this;
        // 必须走 7 参构造器：6 参那个会把 MvSeq 置空，
        // 而 OrientLandscape 默认就会走到这里。
        return new FoldedModel(World.Select(f => RotPoly(f, k)).ToList(), new List<int>(Rank),
                               new Dictionary<int, string>(MvMap),
                               new List<string>(MvSeq),
                               FacesFlat.Select(f => RotPoly(f, k)).ToList(),
                               new List<Segment>(Creases), Groups);
    }

    static List<Point> RotPoly(IReadOnlyList<Point> f, int k)
    {
        var o = new List<Point>(f.Count);
        foreach (var p in f)
        {
            double x = p.X, y = p.Y;
            for (int i = 0; i < ((k % 4) + 4) % 4; i++) { (x, y) = (-y, x); }
            o.Add(new Point(x, y));
        }
        return o;
    }

    /// <summary>
    /// 把成品转成<b>横向可读</b>（宽 &gt;= 高）。
    /// 成品落在哪个朝向取决于母版第 0 格的折痕方向，两种都合法，统一在这里摆正。
    /// <para>
    /// ★ 问题出在<b>bbox 恰好是正方形</b>的情形。沿 45° 对角延伸的纸带
    /// （母版是若干个 <c>\</c> 时正是如此）的 bbox 是 N×N：
    /// 奇数格是真方形，偶数格是纵向，于是 <c>width &gt;= height</c> 在奇数格判「已横向」
    /// 而放弃旋转、偶数格判「竖直」而旋转 —— <b>隔一个跳一次</b>，
    /// 视觉上就是斜线方向反复变。
    /// </para>
    /// <para>
    /// 修法：正方形不是「已横向」的证据。正方形时统一<b>旋转</b>，
    /// 让朝向在整个区间上连续；非正方形时仍按宽高比判断。
    /// 这样奇偶格的处理一致，斜线方向不再跳变。
    /// </para>
    /// </summary>
    public FoldedModel OrientLandscape()
    {
        var (x0, y0, x1, y1) = Bbox();
        double w = x1 - x0, h = y1 - y0;
        const double eps = 1e-9;

        bool square = Math.Abs(w - h) <= eps;

        // 明确的宽 < 高：竖向，该转。
        // 明确的宽 > 高：横向，不转。
        // 恰好正方形：方向定不下来（对角带的巧合），**统一旋转**，
        // 与相邻格数保持一致 —— 这是本函数存在的意义（摆正），
        // 两种朝向本就都合法，不该在退化点上来回摇摆。
        bool needsRotate = square ? true : (w < h);

        if (!needsRotate) return this;

        var cand = Rotate90(1);
        var (a0, b0, a1, b1) = cand.Bbox();
        // 转完未必更横向，只在确实变宽时采用。
        // 正方形时两者相等，故一律采用（避免又出现「两个答案」的分支）。
        return (square || (a1 - a0) > (b1 - b0)) ? cand : this;
    }

    public double AreaFlat() => FacesFlat.Sum(Geom.PolyArea);

    public double AreaFolded() => World.Sum(Geom.PolyArea);

    public (double X0, double Y0, double X1, double Y1) Bbox() => Geom.PolyBbox(World);

    public override string ToString()
    {
        var (x0, y0, x1, y1) = Bbox();
        return $"<FoldedModel {World.Count}片 {NLayers}层 " +
               $"{x1 - x0:0}x{y1 - y0:0}格 {Groups.Count}纸片>";
    }
}

/// <summary>折痕邻接的一条边：邻居面下标 + 该折痕的规范键。</summary>
public readonly record struct AdjEdge(int Neighbor, Segment Crease);

/// <summary>
/// 折叠与层序。两条物理路径：
/// <list type="bullet">
/// <item><b>长条（顺次滚动折叠）</b>：纸带折痕构成一条链。从起笔端出发，沿行进序把
/// 「其后的整条尾巴」绕当前折痕反射 180°。层序 == 行进序（<c>rank[i] = i</c>）。</item>
/// <item><b>方形（树反射）</b>：方形 CP 被剪切线切成若干独立纸片，邻接对偶图是一棵树。
/// 从 root 出发 BFS，每个面沿<b>父边折痕</b>反射。层序由折叠深度与峰谷共同决定。</item>
/// </list>
/// <para>平面折纸里峰谷只决定层序，不影响剪影 —— 两种极性的成品互为翻面。</para>
/// </summary>
public static class FoldOps
{
    // ---- 1. 顺次滚动折叠 ----

    /// <summary>
    /// 顺次滚动折叠。第 0 面恒为原位，所以返回坐标就是最终朝向，无需任何后处理。
    /// </summary>
    public static (List<List<Point>> World, List<Mat3> Transforms) RollFold(
        IReadOnlyList<List<Point>> faces, IReadOnlyList<Segment> creases)
    {
        var T = Mat3.Identity;
        var output = new List<List<Point>>(faces.Count);
        var ts = new List<Mat3>(faces.Count);

        for (int i = 0; i < faces.Count; i++)
        {
            if (i > 0)
            {
                var (a, b) = creases[i - 1];
                var wa = T.Apply(a);
                var wb = T.Apply(b);
                T = Geom.ReflectMatrix(wa, wb) * T;
            }
            output.Add(Geom.ApplyPoly(T, faces[i]));
            ts.Add(T);
        }
        return (output, ts);
    }

    // ---- 2. 折痕邻接 ----

    /// <summary>边的规范键 <c>((x1,y1),(x2,y2))</c>。</summary>
    public static Segment EdgeKey(Point a, Point b) => Geom.SegKey(a, b);

    static Dictionary<Segment, List<int>> EdgeFaces(IReadOnlyList<List<Point>> faces)
    {
        var ef = new Dictionary<Segment, List<int>>();
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            for (int k = 0; k < f.Count; k++)
                ef.GetOrAdd(EdgeKey(f[k], f[(k + 1) % f.Count]), () => new List<int>()).Add(i);
        }
        return ef;
    }

    /// <summary>折痕邻接 -&gt; <c>(adj, creaseKeys)</c>。</summary>
    public static (Dictionary<int, List<AdjEdge>> Adj, HashSet<Segment> CreaseKeys)
        CreaseAdjacency(IReadOnlyList<List<Point>> faces, IReadOnlyList<Segment> creases)
    {
        var ef = EdgeFaces(faces);
        var fk = new HashSet<Segment>(creases.Select(s => EdgeKey(s.A, s.B)));

        var adj = new Dictionary<int, List<AdjEdge>>();
        foreach (var e in SegOrder.Sorted(ef.Keys))
        {
            var fl = ef[e];
            if (fl.Count == 2 && fk.Contains(e))
            {
                int a = fl[0], b = fl[1];
                var ke = EdgeKey(e.A, e.B);
                adj.GetOrAdd(a, () => new List<AdjEdge>()).Add(new AdjEdge(b, ke));
                adj.GetOrAdd(b, () => new List<AdjEdge>()).Add(new AdjEdge(a, ke));
            }
        }
        return (adj, fk);
    }

    // ---- 3. 层序 ----

    /// <summary>
    /// 顺次滚动折叠的层序：<c>rank[i] = i</c>。
    /// <para>每一步折过来的东西都盖在已折好的部分之上，故层序 == 行进序。峰谷只决定
    /// 「落在上面还是下面」，不改变次序。</para>
    /// </summary>
    public static List<int> LayerRanks(int faceCount, IReadOnlyList<Segment> creases,
                                        IReadOnlyList<string>? mv = null)
    {
        if (faceCount == 0) return new List<int>();
        if (mv is not null && mv.Count < faceCount - 1)
            throw new InvalidOperationException($"峰谷序列长度 {mv.Count} < 折痕数 {faceCount - 1}");
        return Enumerable.Range(0, faceCount).ToList();
    }

    /// <summary>
    /// 层序自检：每条折痕两侧的面在层序上必须直接相邻。
    /// 这是顺次滚动折叠的<b>充要</b>判据 —— 层序即行进序时恒成立。
    /// 返回违规列表。</summary>
    public static List<(int Crease, List<int> Between)> VerifyLayers(
        IReadOnlyList<Segment> creases, IReadOnlyList<int> rank)
    {
        var bad = new List<(int, List<int>)>();
        for (int i = 0; i < creases.Count; i++)
        {
            if (i + 1 >= rank.Count) break;
            if (Math.Abs(rank[i] - rank[i + 1]) != 1)
            {
                int lo = Math.Min(rank[i], rank[i + 1]), hi = Math.Max(rank[i], rank[i + 1]);
                bad.Add((i, Enumerable.Range(0, rank.Count)
                        .Where(k => rank[k] > lo && rank[k] < hi).ToList()));
            }
        }
        return bad;
    }

    // ---- 4. 方形：树反射 ----

    /// <summary>
    /// 方形 CP -&gt; 折叠成品（<b>树反射</b>，与长条顺次折不同的物理过程）。
    /// <para>反射必须用折痕在<b>父亲当前姿态下的世界坐标</b>：
    /// <c>T_child = Refl(T_parent(a), T_parent(b)) @ T_parent</c>。用纸面坐标会发散。</para>
    /// <para>峰谷沿树严格交替（父为峰则子为谷）。</para>
    /// </summary>
    public static (List<List<Point>> World, Dictionary<int, string> Mv,
                   Dictionary<int, int> Depth, List<int> Order,
                   List<List<Point>> Faces)
        SquareFold(SquarePlan plan, int? rootIdx = null)
    {
        var faces = Geom.HalfedgeFaces(plan.Cut, plan.Fold, plan.Border);
        var ef = EdgeFaces(faces);
        var fk = new HashSet<Segment>();
        foreach (var s in plan.Fold) fk.Add(EdgeKey(s.A, s.B));

        var dual = new Dictionary<int, List<int>>();
        var creaseOf = new Dictionary<(int, int), Segment>();
        foreach (var e in SegOrder.Sorted(ef.Keys))
        {
            var fl = ef[e];
            if (fl.Count == 2 && fk.Contains(e))
            {
                int a = fl[0], b = fl[1];
                dual.GetOrAdd(a, () => new List<int>()).Add(b);
                dual.GetOrAdd(b, () => new List<int>()).Add(a);
                creaseOf[(Math.Min(a, b), Math.Max(a, b))] = EdgeKey(e.A, e.B);
            }
        }

        int nCrease = dual.Values.Sum(l => l.Count) / 2;
        if (nCrease == 0)
        {
            var flat = faces.Select(f => (List<Point>)new List<Point>(f)).ToList();
            return (flat, new Dictionary<int, string>(),
                    faces.Select((_, i) => i).ToDictionary(i => i, _ => 0),
                    Enumerable.Range(0, faces.Count).ToList(), faces);
        }
        if (nCrease != faces.Count - 1)
            throw new InvalidOperationException(
                $"方形 CP 的对偶图不是树：{faces.Count} 面 / {nCrease} 折痕邻接" +
                $"（应为 {faces.Count - 1}）。常见原因：面片构建时删掉了共线顶点，导致折痕端点丢失。");

        // root：缺省取 x+y 最小的叶子（题面左上角）
        if (rootIdx is null)
        {
            var leaves = Enumerable.Range(0, faces.Count)
                .Where(i => dual.TryGetValue(i, out var l) && l.Count == 1).ToList();
            if (leaves.Count == 0) rootIdx = 0;
            else
            {
                var best = leaves.MinBy(i =>
                {
                    var c = Geom.PolyCentroid(faces[i]);
                    return (c.X + c.Y, c.X);
                });
                rootIdx = best;
            }
        }
        int root = rootIdx.Value;

        var mv = new Dictionary<int, string> { [root] = Mv.Mountain };
        var depth = new Dictionary<int, int> { [root] = 0 };
        var par = new Dictionary<int, int?> { [root] = null };
        var dq = new Queue<int>();
        dq.Enqueue(root);
        var order = new List<int> { root };

        while (dq.Count > 0)
        {
            int u = dq.Dequeue();
            if (!dual.TryGetValue(u, out var nb)) continue;
            foreach (int v in nb)
            {
                if (mv.ContainsKey(v)) continue;
                mv[v] = mv[u] == Mv.Mountain ? Mv.Valley : Mv.Mountain;
                depth[v] = depth[u] + 1;
                par[v] = u;
                order.Add(v);
                dq.Enqueue(v);
            }
        }
        if (order.Count != faces.Count)
            throw new InvalidOperationException($"方形 CP 面片不连通 {order.Count}/{faces.Count}");

        // 逐级反射：T[v] = Refl(父姿态下的折痕) * T[父]
        var T = new Dictionary<int, Mat3> { [root] = Mat3.Identity };
        foreach (int v in order)
        {
            if (!par.TryGetValue(v, out var pu) || pu is null) continue;
            int u = pu.Value;
            var (a, b) = creaseOf[(Math.Min(u, v), Math.Max(u, v))];
            var wa = T[u].Apply(a);
            var wb = T[u].Apply(b);
            T[v] = Geom.ReflectMatrix(wa, wb) * T[u];
        }

        var world = new List<List<Point>>(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            var tm = T.TryGetValue(i, out var m0) ? m0 : Mat3.Identity;
            world.Add(Geom.ApplyPoly(tm, faces[i]));
        }
        return (world, mv, depth, order, faces);
    }

    /// <summary>
    /// 方形折叠的层序（谁盖住谁），返回 <c>rank[i]</c>（小 = 靠下）。
    /// <para>某面被压住的位置取决于它相对基准面的<b>折叠深度</b>与<b>峰谷方向</b>：
    /// 深度每增 1 层 z 量级翻倍（保证深处的面一定在外侧）；同一深度带内峰（M）朝上、
    /// 谷（V）朝下。用 <c>z = ±2^(depth/2)·(1+(depth%2)·1e-3)</c>。</para>
    /// <para>并列（z 相等）时按<b>面片质心的字典序</b>打破，而不是按面下标 ——
    /// 这样层序只依赖几何，不依赖 <c>halfedge_faces</c> 的枚举顺序，结果可复现。</para>
    /// </summary>
    public static List<int> SquareLayerOrder(int n, Dictionary<int, string> mv,
                                              Dictionary<int, int> depth,
                                              IReadOnlyList<List<Point>>? faces = null)
    {
        if (n == 0) return new List<int>();
        var z = new double[n];
        for (int i = 0; i < n; i++)
        {
            int d = depth.TryGetValue(i, out var dd) ? dd : 0;
            double s = !mv.TryGetValue(i, out var m) || m == Mv.Mountain ? 1.0 : -1.0;
            z[i] = s * Math.Pow(2.0, d / 2) * (1.0 + (d % 2) * 1e-3);
        }

        var centroids = new Point[n];
        for (int i = 0; i < n; i++)
            centroids[i] = faces is not null && i < faces.Count
                ? Geom.PolyCentroid(faces[i])
                : Point.Origin;

        var idx = Enumerable.Range(0, n)
            .OrderBy(i => z[i])
            .ThenBy(i => centroids[i].X)
            .ThenBy(i => centroids[i].Y)
            .ToList();
        var rank = new List<int>(new int[n]);
        for (int r = 0; r < idx.Count; r++) rank[idx[r]] = r;
        return rank;
    }

    /// <summary>方形 CP -&gt; <see cref="FoldedModel"/>（树反射）。</summary>
    public static FoldedModel FoldSquare(SquarePlan plan, bool orient = true)
    {
        var (world, mv, depth, _, faces) = SquareFold(plan);
        var rank = SquareLayerOrder(world.Count, mv, depth, faces);
        var creases = plan.Fold.Select(s => EdgeKey(s.A, s.B)).OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        var fm = new FoldedModel(world, rank, mv, world, creases,
            new List<(List<int>, int)> { (Enumerable.Range(0, world.Count).ToList(), 0) });
        return orient ? fm.OrientLandscape() : fm;
    }

    /// <summary>长条母版 -&gt; <see cref="FoldedModel"/>（一张连续的纸，链式顺次折）。</summary>
    /// <param name="mv">峰谷序列，长度须等于折痕数。</param>
    /// <param name="orient">
    /// 是否摆正成品。
    /// <para>
    /// 优先用 <see cref="Master.Rotate"/>（来自字模编码的 <c>@</c> 计数，
    /// 由第一个字母决定）—— 旧实现就是这么「一次到位」的。
    /// 该信息缺失时（手工输入的母版串没有编码来源）才退回
    /// <see cref="OrientLandscape"/> 按 bbox 猜。
    /// </para>
    /// </param>
    public static FoldedModel FoldStrip(Master master, IReadOnlyList<string> mv, bool orient = true)
    {
        var (faces, creases, _) = MasterOps.ChainFromMaster(master);
        var (world, _) = RollFold(faces, creases);
        var rank = LayerRanks(faces.Count, creases, mv);

        // 必须同时给出 MvMap 与 MvSeq：渲染层优先读 MvMap（按面下标取），
        // 只给 MvSeq 的话 MvMap 是空的，着色会被静默跳过（成品一片空白）。
        var mvMap = new Dictionary<int, string>();
        for (int i = 0; i < creases.Count && i < mv.Count; i++)
            mvMap[CreaseOwner(creases[i], faces)] = mv[i];

        var fm = new FoldedModel(world, rank, mvMap, mv.ToList(), faces, creases);
        if (!orient) return fm;

        // 字模编码里带了旋转次数就用它 —— 确定性，不会摇摆
        if (master.Rotate > 0) return fm.Rotate90(master.Rotate % 4);

        return fm.OrientLandscape();
    }

    /// <summary>找出这条折痕所归属的面（拿它当着色字典的键）。</summary>
    static int CreaseOwner(Segment crease, IReadOnlyList<List<Point>> faces)
    {
        var key = Geom.SegKey(crease.A, crease.B);
        for (int i = 0; i < faces.Count; i++)
        {
            var f = faces[i];
            for (int k = 0; k < f.Count; k++)
                if (Geom.SegKey(f[k], f[(k + 1) % f.Count]).Equals(key)) return i;
        }
        return 0;
    }
}