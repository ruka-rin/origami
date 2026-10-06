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
        return new FoldedModel(World.Select(f => RotPoly(f, k)).ToList(), new List<int>(Rank),
                               new Dictionary<int, string>(MvMap),
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
    /// 把成品转成<b>横向可读</b>（宽 &gt; 高）。
    /// 成品落在哪个朝向取决于母版第 0 格的折痕方向，两种都合法，统一在这里摆正。
    /// </summary>
    public FoldedModel OrientLandscape()
    {
        var (x0, y0, x1, y1) = Bbox();
        if ((x1 - x0) >= (y1 - y0)) return this;
        var cand = Rotate90(1);
        var (a0, b0, a1, b1) = cand.Bbox();
        // 只有转完明显更接近横向才采用
        return (a1 - a0) > (b1 - b0) ? cand : this;
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
/// 折叠与层序。
/// <para>两种折叠路径：</para>
/// <list type="bullet">
/// <item><b>长条（顺次滚动折叠）</b>：纸带折痕构成一条<b>链</b>。从起笔端出发，沿行进序把
/// 「其后的整条尾巴」绕当前折痕的<b>当时世界坐标</b>反射 180°。朝向由起笔端唯一确定。</item>
/// <item><b>方形（树反射）</b>：方形 CP 被剪切线切成若干独立纸片，每片内部折痕对偶图都是链。
/// 从 root 出发 BFS，每个面沿<b>父边折痕</b>反射 —— 反射必须用折痕在<b>父亲当前姿态下</b>的世界坐标。</item>
/// </list>
/// <para><b>层序</b>：顺次折叠中层序 == 行进序（<c>rank[i] = i</c>）；树反射中层序由折叠深度
/// 与峰谷共同决定。</para>
/// </summary>
public static class FoldOps
{
    // ==================================================================
    //  1. 顺次滚动折叠
    // ==================================================================

    /// <summary>
    /// 顺次滚动折叠：每次把「其后的整条尾巴」绕当前折痕反射 180°。
    /// 第 0 面恒为原位，所以返回坐标就是最终朝向，无需任何后处理。
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

    // ==================================================================
    //  2. 折痕邻接
    // ==================================================================

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

    /// <summary>从 <c>start</c> 沿折痕邻接走一条链；中途分叉则报错。</summary>
    public static (List<int> Chain, List<Segment> Creases) WalkChain(
        Dictionary<int, List<AdjEdge>> adj, int start)
    {
        var chain = new List<int>();
        var creases = new List<Segment>();
        // prev = -1 表示「无前驱」（面下标均 >= 0）
        int prev = -1, cur = start;

        while (true)
        {
            chain.Add(cur);
            var cands = new List<AdjEdge>();
            if (adj.TryGetValue(cur, out var l))
                foreach (var e in l)
                    if (e.Neighbor != prev) cands.Add(e);

            if (cands.Count == 0) break;
            if (cands.Count > 1)
                throw new InvalidOperationException(
                    $"面 {cur} 有 {cands.Count} 个未访问邻居，该分支不是一条链");
            prev = cands[0].Neighbor;
            cur = cands[0].Neighbor;
            creases.Add(cands[0].Crease);
        }
        return (chain, creases);
    }

    // ==================================================================
    //  3. 层序
    // ==================================================================

    /// <summary>
    /// 顺次滚动折叠的层序。
    /// <para>★ 物理过程：从起笔端顺次折，每一步把「面 i+1 及其之后的整条尾巴」绕折痕 i
    /// 翻到面 i 的上方。每一步折过来的东西都<b>盖在已折好的部分之上</b>，所以
    /// <b>层序 == 行进序</b>，即 <c>rank[i] = i</c>。峰谷只决定「落在上面还是下面」，
    /// 不改变次序（两种极性的成品互为翻面，剪影相同）。</para>
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

    // ==================================================================
    //  4. 方形：树反射
    // ==================================================================

    /// <summary>
    /// 方形 CP -&gt; 折叠成品（<b>树反射</b>，与长条顺次折不同的物理过程）。
    /// <para>方形 CP 是完整 N×N 方块，其面片邻接对偶图是一棵<b>树</b>。从 <c>root</c> 出发
    /// BFS，每个面沿父边折痕反射 180°，且必须用折痕在<b>父亲当前姿态下的世界坐标</b>：
    /// <c>T_child = Refl(T_parent(a), T_parent(b)) @ T_parent</c>。用纸面坐标会发散。</para>
    /// <para>峰谷沿树严格交替（父为峰则子为谷）—— 平面折纸里峰谷只决定层序，不影响剪影。</para>
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

        // BFS 定序 + 峰谷交替
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

        // 逐级反射
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
    /// 方形折叠的层序（谁盖住谁）。
    /// <para>★ 物理过程：折好一个纸堆后，某面被压住的位置取决于它相对基准面的
    /// <b>折叠深度</b>与<b>峰谷方向</b>：深度每增 1 层 z 量级翻倍（保证深处的面一定在外侧）；
    /// 同一深度带内峰（M）朝上、谷（V）朝下。用 <c>z = ±2^(depth/2)·(1+(depth%2)·1e-3)</c>。</para>
    /// <para>★ 并列（同一深度带内 z 相等）时按<b>面片质心的字典序</b>打破，而不是按面下标 ——
    /// 这样层序只依赖几何，不依赖 <c>halfedge_faces</c> 的枚举顺序，结果可复现。
    /// 返回 <c>rank[i]</c>（小 = 靠下）。</para>
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

        // 并列时用质心字典序（几何量）而不是面下标（枚举序）打破
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
    public static FoldedModel FoldStrip(Master master, IReadOnlyList<string> mv, bool orient = true)
    {
        var (faces, creases, _) = MasterOps.ChainFromMaster(master);
        var (world, _) = RollFold(faces, creases);
        var rank = LayerRanks(faces.Count, creases, mv);

        // ★ 同时给出 MvMap 与 MvSeq：渲染层优先读 MvMap（按面下标取），
        //   只给 MvSeq 的话 MvMap 是空的，着色会被静默跳过（成品一片空白）。
        var mvMap = new Dictionary<int, string>();
        for (int i = 0; i < creases.Count && i < mv.Count; i++)
            mvMap[CreaseOwner(creases[i], faces)] = mv[i];

        var fm = new FoldedModel(world, rank, mvMap, mv.ToList(), faces, creases);
        return orient ? fm.OrientLandscape() : fm;
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