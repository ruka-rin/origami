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

    /// <summary>沿 y 轴镜像（左右翻转）。</summary>
    public FoldedModel MirrorAcrossY()
    {
        var b = Bbox();
        double axis = (b.X0 + b.X1) / 2;
        return Map(p => new Point(2 * axis - p.X, p.Y));
    }

    /// <summary>
    /// 关于<b>水平</b>中心轴镜像（即上下翻转）。
    /// <para>
    /// 用来把方形折叠的<b>屏幕坐标</b>（y 向下）转成与长条一致的<b>数学坐标</b>
    /// （y 向上）。两条路径的坐标系统一后，「摆正」才能共用同一套画布变换 ——
    /// 否则同一个 rotate 约定在两种坐标里旋转手性相反，会各转各的。
    /// </para>
    /// </summary>
    public FoldedModel MirrorAboutX()
    {
        var b = Bbox();
        double axis = (b.Y0 + b.Y1) / 2;
        return Map(p => new Point(p.X, 2 * axis - p.Y));
    }

    /// <summary>
    /// 平移到<b>第一象限</b>（y 从 0 向上为正）。
    /// <para>
    /// 旋转 90° 后顶点可能落到负半轴（如 SOS 的 y∈[-5,0]），而渲染层
    /// 的 <c>flipY</c> 用 <c>y1 - p.Y</c> 映射到屏幕，负坐标会画到画布上方，
    /// 看起来就是「上下颠倒」。把成品归位到第一象限即可。
    /// </para>
    /// </summary>
    public FoldedModel Normalize()
    {
        var b = Bbox();
        double dx = b.X0 < 0 ? -b.X0 : 0;
        double dy = b.Y0 < 0 ? -b.Y0 : 0;
        if (dx == 0 && dy == 0) return this;
        return Map(p => new Point(p.X + dx, p.Y + dy));
    }

    /// <summary>对所有顶点施加同一映射，其余数据原样保留。</summary>
    FoldedModel Map(Func<Point, Point> f) =>
        new(World.Select(poly => poly.Select(f).ToList()).ToList(), new List<int>(Rank),
            new Dictionary<int, string>(MvMap),
            new List<string>(MvSeq),
            FacesFlat.Select(poly => poly.Select(f).ToList()).ToList(),
            new List<Segment>(Creases), Groups);

    /// <summary>
    /// 成品上书写方向是否自左向右 —— 折痕链的前段落在长轴左端。
    /// <para>
    /// 取链首尾各 1/4 的面片，比较顶点质心在长轴上的位置。
    /// 用 1/4 而不是单个面片：单片可能只是局部笔画像画到中间，取平均更稳。
    /// </para>
    /// </summary>
    public bool IsWritingLeftToRight()
    {
        if (World.Count < 4) return true;      // 面片太少，无从判断，保持原样

        int q = Math.Max(1, World.Count / 4);
        var head = World.Take(q).SelectMany(f => f);
        var tail = World.Skip(World.Count - q).SelectMany(f => f);
        return head.Average(p => p.X) < tail.Average(p => p.X);
    }

    /// <param name="rotate">母版声明的旋转次数（<see cref="Master.Rotate"/>，0..3）。</param>
    /// <summary>
    /// 把成品摆成<b>统一的书写朝向</b>：横向，且<b>首字在左</b>。
    /// <para>
    /// 光靠 <see cref="Master.Rotate"/>（90° 旋转）不够：rotate=1 的字模
    /// （6 8 J L O Q U V W）转完后 bbox 与 rotate=0 同样大小，
    /// 却<b>左右翻转</b> —— 视觉上是两个不同的字。这就是「除了转向还有翻面」。
    /// </para>
    /// <para>
    /// 判据用<b>书写方向</b>这个几何事实：折纸链沿 x 推进，成品上链首与链尾
    /// 在长轴上的相对位置就标出了书写方向。不对就镜像一次。
    /// </para>
    /// <para>
    /// ★ 判据不能换成「首格折痕是 / 还是 \」—— 实测二者<b>并无</b>对应关系：
    /// 首格同为 '/' 的 A 与 M 方向相反，首格为 '\' 的 S 反而是正的。
    /// 首格折痕只是<b>相关</b>，书写方向才是<b>因果</b>。
    /// </para>
    /// </summary>
    /// <summary>
    /// 成品摆正：<b>按字模的 rotate 约定旋转，再归位到第一象限</b>。
    /// <para>
    /// rotate 是字模自带的属性（编码里 <c>@</c> 的个数），它决定字母
    /// <b>本身的站立方向</b>。实测（格为单位）：
    /// <list type="bullet">
    /// <item><b>A</b> rotate=0：折叠结果 3×5，与旧实现 6×10（半格）吻合，无需旋转</item>
    /// <item><b>V</b> rotate=1：折叠结果 5×3，旋转 90° 后 3×5 = 旧实现的 6×10</item>
    /// <item><b>L</b> rotate=1：同上</item>
    /// </list>
    /// 多字母文本（KRENIO/PROTEIN/SOS）的首字母 rotate 决定整行朝向，
    /// 旧实现的 bbox 也是横向的（如 SOS 22×10 半格 = 11×5 格）。
    /// </para>
    /// <para>
    /// ★ 曾一度撤掉这里的旋转，理由是「渲染出的 SOS 上下颠倒」——
    /// 但当时母版还缺了首尾两块空白片（见 <c>CreasesToMaster</c>），
    /// 形状本身就是错的，那个判断不成立。<b>先修数据，再判方向。</b>
    /// </para>
    /// <para>
    /// ★ <b>旋转量取负</b>（<c>Rotate90(4 - rotate)</c>）。旧实现的
    /// <c>rotatePoint</c> 是 <c>(x,y) → (-y,x)</c>，作用在它自己的坐标系上；
    /// 本项目走「数学坐标（y 向上）+ 渲染层 flipY」两条腿，而 flipY 本身
    /// 是一次镜像 —— 镜像会翻转旋转的手性，所以同一个约定值在本项目里
    /// 要按<b>相反方向</b>转才能落到同样的视觉朝向。
    /// </para>
    /// <para>
    /// 实测（把成品栅格化成 72×72 掩码后与旧实现求 IoU，1.0 为完全重合）：
    /// <code>
    /// 约定 rotate :  0     1     2     3
    /// 应取 rotate :  0     3     2     1
    /// 该 rotate 下 :  0.996 0.997 1.000 0.998   (X/M/A/E/F/G/…)
    /// </code>
    /// 18 个字模/词在正确 rotate 下 IoU 全部 ≥ 0.996
    /// （V 0.998、L 1.000、6 0.998、68S 0.997、S 0.998、SOS 0.998、KRENIO 0.997）。
    /// </para>
    /// <para>
    /// <b>归位</b>不可省：<c>Rotate90</c> 绕原点转会把顶点甩到负半轴，
    /// 渲染层的 <c>flipY</c>（<c>Sy = y1 - p.Y</c>）要求规整的 bbox。
    /// </para>
    /// </summary>
    public FoldedModel OrientWriting(int rotate)
        => Rotate90((4 - rotate % 4) % 4).Normalize();

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

        // root：缺省取<b>贴着方形左上角 (0,0) 的那片叶子</b> —— 那是纸带的「起笔端」，
        // 与长条取「质心 x 最小」是同一个物理位置（铺排都是从左上的原点起笔）。
        //
        // ★ 判据用「面内顶点的最小 x+y」，<b>不能</b>用「质心 x+y 最小」：
        // 纸带第 0 格可能是个细长条，质心离角点很远，而它旁边那片「贴左边但不在
        // 顶行」的叶子质心反而更小 —— 于是起点被选错，成品比长条多转 180°。
        // 实测 68S / V 正是如此（默认 root=1，正确 root=0）；SOS 则是巧合选对。
        if (rootIdx is null)
        {
            var leaves = Enumerable.Range(0, faces.Count)
                .Where(i => dual.TryGetValue(i, out var l) && l.Count == 1).ToList();
            if (leaves.Count == 0) rootIdx = 0;
            else
            {
                var best = leaves.MinBy(i =>
                {
                    double minSum = faces[i].Min(p => p.X + p.Y);
                    double minX = faces[i].Min(p => p.X);
                    return (minSum, minX);
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
    public static FoldedModel FoldSquare(SquarePlan plan, bool orient = true,
                                         int rotate = 0, bool rotateKnown = false,
                                         int? rootIdx = null)
    {
        var (world, mv, depth, _, faces) = SquareFold(plan, rootIdx);

        // ★ 与长条同一个物理含义：母版含 0 号竖折痕（x=0）时，成品要左右镜像一次
        // （详见 FoldStrip 里的同名处理）。方形与长条的差别只有两点：
        //   ① 方形是屏幕坐标（y 向下），长条是数学坐标（y 向上）；
        //   ② 图形由树反射而非顺次滚动得到。
        // 实测（与长条成品求栅格化 IoU，1.0 为完全重合）：
        //   补镜像 + Rotate90(rotate)   -> 18 个字模/词里 17 个达到 1.0
        //   不补镜像                    -> G/F/L/6/1/5/123456/68S 只有 0.1~0.6
        //   用 Rotate90(4-rotate)（长条的那套）-> L/6/SOS/S 反过来错
        // 原因同长条的「取负」：两套坐标系互为镜像，旋转手性相反 —— 长条取负、
        // 方形不取负，落到屏幕上才是同一个朝向。
        if (plan.Pipes.Contains(0))
            world = world.Select(f => f.Select(p => new Point(-p.X, p.Y)).ToList()).ToList();

        var rank = SquareLayerOrder(world.Count, mv, depth, faces);
        var creases = plan.Fold.Select(s => EdgeKey(s.A, s.B)).OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        var fm = new FoldedModel(world, rank, mv, world, creases,
            new List<(List<int>, int)> { (Enumerable.Range(0, world.Count).ToList(), 0) });

        // ★ 统一到<b>数学坐标</b>（y 向上），与长条一致。
        // 这样「摆正」能共用长条那套 OrientWriting，同一个 rotate 约定在两条路径
        // 上含义相同（此前方形是屏幕坐标、旋转手性相反，只能各写一套规则）。
        // 渲染层随之对方形也做 flipY —— 用户要的「通用渲染」正是这个意思。
        fm = fm.MirrorAboutX();

        if (!orient) return fm;
        if (rotateKnown) return fm.OrientWriting(rotate);
        return fm.OrientLandscape();
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

        // ★ 母版以 `|` 开头（<c>Pipes</c> 含 0）表示纸带起点端有一道竖折痕。
        // 它切出的面片宽度为零，平面剖分产生不出这种退化面片，但<b>那条反射线
        // 是真实的</b>：旧实现会对每个面片依次反射之前所有折痕，其中就包含它，
        // 效果等于把整个成品沿 x=0 左右翻转（矩阵上是最外层的一次 Reflect）。
        // 少了这一次镜像，L / 68S 的形状就与旧实现不符。
        //
        // 必须补在<b>最外层</b>（rotate 之前 —— 本函数在 OrientWriting 前调用，
        // 故此处正好），因为旧实现里 x=0 折痕是所有面片共有的最外一层。
        if (master.Pipes.Contains(0))
            world = world.Select(f => f.Select(p => new Point(-p.X, p.Y)).ToList()).ToList();

        // 必须同时给出 MvMap 与 MvSeq：渲染层优先读 MvMap（按面下标取），
        // 只给 MvSeq 的话 MvMap 是空的，着色会被静默跳过（成品一片空白）。
        var mvMap = new Dictionary<int, string>();
        for (int i = 0; i < creases.Count && i < mv.Count; i++)
            mvMap[CreaseOwner(creases[i], faces)] = mv[i];

        var fm = new FoldedModel(world, rank, mvMap, mv.ToList(), faces, creases);
        if (!orient) return fm;

        // ★ 判据是「rotate 有没有来源」，不是「rotate 是否非零」。
        // 曾经写成 `master.Rotate > 0`，于是 rotate=0 的整组字模
        // （A K M N P R T X Y Z …）被当成「没有 rotate 信息」而走 bbox 猜法，
        // 把本来竖立的成品（A 折叠结果 3×5，与旧实现吻合）
        // 强行转成横向 5×3 —— 字母全部躺倒。
        //
        // 有编码来源（文本生成）-> 用字模的 rotate 约定，即便它是 0
        if (master.RotateKnown) return fm.OrientWriting(master.Rotate);

        // 手工输入的母版串没有编码来源 -> 退回按 bbox 猜
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