using System.Text;

namespace Origami.Core;

/// <summary>折痕类型。<c>M</c>=峰(mountain/红)、<c>V</c>=谷(valley/蓝)、<c>E</c>=折耳结构线。</summary>
public static class Mv
{
    public const string Mountain = "M";
    public const string Valley = "V";
    public const string Ear = "E";
}

/// <summary>一条折痕。</summary>
public sealed class Fold
{
    public Segment Seg { get; set; }
    public string MvType { get; set; } = Mv.Valley;
    public Fold() { }
    public Fold(Segment seg, string mv) { Seg = seg; MvType = mv; }
}

/// <summary>
/// 数据模型：折痕图的核心数据结构。
/// <code>
/// { kind: 'strip'|'square', N, ncell, cut: [[a,b],...],
///   folds: [{seg:[[ax,ay],[bx,by]], mv:'M'|'V'}], border: [[a,b],...] }
/// </code>
/// <para><b>坐标系（★ 两套，别混）</b>：<c>strip</c> 是<b>数学坐标 y 向上</b>；
/// <c>square</c> 是<b>屏幕坐标 y 向下</b>，与原题图一致。</para>
/// <para><b>峰谷</b>：长条约定为沿纸带严格红蓝交替、<b>首条为蓝（谷）</b>；
/// 方形优先读 <c>fold_mv</c>（铺排时按绘制顺序交替写入，物理正确），无则按排序兜底。</para>
/// </summary>
public sealed class Model
{
    public string Kind { get; set; } = "square";
    /// <summary>方形边长（方形才有）。</summary>
    public int N { get; set; }
    /// <summary>长条格数（长条才有）。</summary>
    public int NCell { get; set; }
    public double Unit { get; set; }
    public List<Segment> Cut { get; set; } = new();
    public List<Fold> Folds { get; set; } = new();
    public List<Segment> Border { get; set; } = new();
    /// <summary>长条母版的 pipe 位置（逆向辅助，不参与几何）。</summary>
    public List<int>? Pipes { get; set; }

    // ==================================================================
    //  长条
    // ==================================================================

    public static Model StripModel(Master master, List<string>? mv = null)
    {
        var (cut, fold, border) = master.StripGeometry();
        var (creases, _, _) = master.CreasesInOrder();
        mv ??= MasterOps.DefaultMv(creases.Count);
        if (mv.Count != creases.Count)
            throw new InvalidOperationException($"峰谷数量 {mv.Count} != 折痕数 {creases.Count}");

        var m = new Model
        {
            Kind = "strip",
            NCell = master.NCell,
            N = 0,
            Cut = new List<Segment>(),
            Folds = creases.Select((c, i) => new Fold(c, mv[i])).ToList(),
            Pipes = new List<int>(master.Pipes),
        };
        var bs = border.ToList();
        bs.Sort(static (x, y) => { int c = x.A.CompareTo(y.A); return c != 0 ? c : x.B.CompareTo(y.B); });
        m.Border = bs;
        return m;
    }

    /// <summary>长条数据模型 -&gt; 母版字符串（逆向）。只能恢复符号与 pipe，峰谷信息丢失。</summary>
    public static Master StripModelToMaster(Model model)
    {
        if (model.Kind != "strip") throw new ArgumentException("该模型不是长条");
        int n = model.NCell;
        var cells = new char[n];
        Array.Fill(cells, '.');
        var pipes = new HashSet<int>(model.Pipes ?? new List<int>());

        foreach (var f in model.Folds)
        {
            var (p, q) = f.Seg.A.CompareTo(f.Seg.B) <= 0 ? (f.Seg.A, f.Seg.B) : (f.Seg.B, f.Seg.A);
            if (Math.Abs(p.X - q.X) < 1e-9) { pipes.Add((int)Math.Round(p.X)); continue; }
            int k = (int)Math.Round(Math.Min(p.X, q.X));
            // 数学坐标 y 向上：'/' 是 (k,0)->(k+1,1)，两点同号 -> '/'
            cells[k] = (q.Y - p.Y) * (q.X - p.X) > 0 ? '/' : '\\';
        }

        var sb = new StringBuilder();
        for (int k = 0; k < n; k++)
        {
            sb.Append(cells[k]);
            if (pipes.Contains(k + 1)) sb.Append('|');
        }
        return Master.ParseMaster(sb.ToString());
    }

    // ==================================================================
    //  方形
    // ==================================================================

    /// <summary>
    /// 方形 CP 计划 -&gt; 数据模型。
    /// <para>峰谷来源优先级：① 显式传入的 <c>mv</c>；② <c>plan.FoldMv</c>（铺排时按母版
    /// 折痕行进序交替写入，<b>物理正确</b>）；③ 都没有时按 <c>(x,y,类别)</c> 排序后交替兜底。</para>
    /// </summary>
    public static Model SquareModel(SquarePlan plan, Dictionary<Segment, string>? mv = null)
    {
        var fkeys = plan.Fold.ToList();
        fkeys.Sort(static (x, y) => { int c = x.A.CompareTo(y.A); return c != 0 ? c : x.B.CompareTo(y.B); });

        var src = mv is { Count: > 0 } ? mv : plan.FoldMv;
        var lut = new Dictionary<Segment, string>();
        for (int i = 0; i < fkeys.Count; i++)
        {
            var (a, b) = fkeys[i];
            var key = Geom.SegKey(a, b);
            lut[key] = src is { Count: > 0 } && src.TryGetValue(key, out var mv0)
                ? mv0
                : (i % 2 == 0 ? Mv.Mountain : Mv.Valley);
        }

        var m = new Model
        {
            Kind = "square",
            N = plan.N,
            NCell = 0,
            Cut = SortedSegments(plan.Cut),
            Folds = fkeys.Select(s => new Fold(s, lut[Geom.SegKey(s.A, s.B)])).ToList(),
            Border = SortedSegments(plan.Border),
        };
        return m;
    }

    /// <summary>方形数据模型 -&gt; <see cref="SquarePlan"/>（仅保留线段，丢弃生成中间量）。</summary>
    public static SquarePlan SquarePlanFromModel(Model model)
    {
        var cut = Geom.SegSet(model.Cut.Select(s => Geom.SegKey(s.A, s.B)));
        var fold = Geom.SegSet(model.Folds.Select(f => Geom.SegKey(f.Seg.A, f.Seg.B)));
        var border = Geom.SegSet(model.Border.Select(s => Geom.SegKey(s.A, s.B)));
        return SquareOps.PlanFromSegments(model.N,
            SegSets.Create(cut), SegSets.Create(fold), SegSets.Create(border));
    }

    /// <summary>母版 -&gt; 长条模型 + 方形模型（一次生成两条链路）。</summary>
    public static (Model Strip, Model Square) MasterToSquareModel(Master master,
                                                                 Dictionary<Segment, string>? mv = null)
        => (StripModel(master), SquareModel(SquareOps.PlanFromMaster(master), mv));

    static List<Segment> SortedSegments(IEnumerable<Segment> src)
    {
        var l = src.ToList();
        l.Sort(static (x, y) => { int c = x.A.CompareTo(y.A); return c != 0 ? c : x.B.CompareTo(y.B); });
        return l;
    }

    // ==================================================================
    //  校验
    // ==================================================================

    /// <summary>返回问题列表（空 = 通过）。</summary>
    public static List<string> CheckModel(Model model)
    {
        var msgs = new List<string>();
        string kind = model.Kind;
        if (kind is not ("strip" or "square"))
        {
            msgs.Add($"kind 非法: '{kind}'");
            return msgs;
        }
        if (kind == "strip")
        {
            if (model.NCell <= 0) msgs.Add("长条必须有 ncell > 0");
            if (model.Cut.Count > 0) msgs.Add("长条是连续纸带，不应有剪切线");
        }
        else
        {
            int n = model.N;
            if (n <= 0) msgs.Add("方形必须有 N > 0");
            else
            {
                foreach (var (role, segs) in new (string, IEnumerable<Segment>)[]
                         { ("cut", model.Cut), ("folds", model.Folds.Select(f => f.Seg)),
                           ("border", model.Border) })
                    foreach (var s in segs)
                    {
                        double x1 = s.A.X, y1 = s.A.Y, x2 = s.B.X, y2 = s.B.Y;
                        if (x1 < -1e-6 || x1 > n + 1e-6 || y1 < -1e-6 || y1 > n + 1e-6 ||
                            x2 < -1e-6 || x2 > n + 1e-6 || y2 < -1e-6 || y2 > n + 1e-6)
                            msgs.Add($"{role} 坐标越界 (应在 0..{n}): {s}");
                    }
            }
        }
        foreach (var f in model.Folds)
            if (f.MvType is not (Mv.Mountain or Mv.Valley or Mv.Ear))
                msgs.Add($"折痕峰谷非法: {f.Seg}");
        return msgs;
    }

    /// <summary>折痕统计（按类别与峰谷）。</summary>
    public static Dictionary<string, int> CreaseStats(Model model)
    {
        var st = new Dictionary<string, int>();
        void Bump(string k) => st[k] = st.TryGetValue(k, out var v) ? v + 1 : 1;
        foreach (var f in model.Folds)
        {
            Bump("total");
            Bump(f.Seg.Class());
            Bump(f.MvType);
        }
        st["cut"] = model.Cut.Count;
        st["border"] = model.Border.Count;
        return st;
    }
}