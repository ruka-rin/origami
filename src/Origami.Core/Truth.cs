using System.Text;
using System.Text.RegularExpressions;

namespace Origami.Core;

/// <summary>
/// 权威题面基准的<b>解析与交叉校验</b>。
/// <para>本模块<b>不是生成链路的一环</b>，唯一用途是<b>回归测试的黄金样本</b>：
/// 任何时候改动了铺排算法，只要 <see cref="Crosscheck"/> 仍报零差异，就说明没有破坏题面。</para>
/// <para>基准 SVG 的坐标是 1 格 = 2 用户单位，且剪切线画成<b>整行长线</b>；本模块会把它拆成
/// 逐格段（几何等价：整行贯通 = 每格都剪开）。</para>
/// </summary>
public static class Truth
{
    public const string ColorRed = "#f00";
    public const string ColorBlue = "#00f";
    public const string ColorCut = "#0f0";
    /// <summary>基准 SVG 里 1 格 = 多少用户单位。</summary>
    public const double TruthUnit = 2.0;

    static readonly Regex LineRe = new(
        @"<line x1=""([-\d.]+)"" y1=""([-\d.]+)"" x2=""([-\d.]+)"" y2=""([-\d.]+)""[^>]*stroke=""([^""]+)""",
        RegexOptions.Compiled);

    /// <summary>手绘折痕图 SVG -&gt; 朴素线段表（格坐标）。</summary>
    public static (int N, HashSet<Segment> Cut, HashSet<Segment> Fold,
                   Dictionary<Segment, string> FoldMv, HashSet<Segment> Border)
        ParseCreaseSvg(string svg, double unit = TruthUnit)
    {
        var recs = LineRe.Matches(svg).Cast<Match>()
                        .Select(m => (
                            X1: double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                            Y1: double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
                            X2: double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture),
                            Y2: double.Parse(m.Groups[4].Value, System.Globalization.CultureInfo.InvariantCulture),
                            Col: m.Groups[5].Value)).ToList();
        if (recs.Count == 0)
            throw new InvalidOperationException("SVG 里没有找到 <line> 元素");

        double h = 1.0 / unit;
        int n = 0;
        var cutRaw = new List<(Point, Point)>();
        var fold = new HashSet<Segment>();
        var foldMv = new Dictionary<Segment, string>();
        var border = new HashSet<Segment>();

        foreach (var r in recs)
        {
            var a = new Point(Geom.Round6(r.X1 * h), Geom.Round6(r.Y1 * h));
            var b = new Point(Geom.Round6(r.X2 * h), Geom.Round6(r.Y2 * h));
            if (r.Col == ColorCut) cutRaw.Add((a, b));
            else if (r.Col is ColorRed or ColorBlue)
            {
                var k = Segment.Norm(a, b);
                fold.Add(k);
                foldMv[k] = r.Col == ColorRed ? Mv.Mountain : Mv.Valley;
            }
            else border.Add(Segment.Norm(a, b));

            n = Math.Max(n, (int)Math.Round(Math.Max(a.X, b.X)));
            n = Math.Max(n, (int)Math.Round(Math.Max(a.Y, b.Y)));
        }

        // 剪切线：整行长线 -> 逐格段
        var cut = new HashSet<Segment>();
        foreach (var (a, b) in cutRaw)
        {
            if (Math.Abs(a.Y - b.Y) > Geom.Eps)
                throw new InvalidOperationException($"剪切线不是水平的: {a}-{b}");
            double lo = Math.Min(a.X, b.X), hi = Math.Max(a.X, b.X);
            for (int x = (int)Math.Round(lo); x < (int)Math.Round(hi); x++)
                cut.Add(Segment.Norm(new Point(x, a.Y), new Point(x + 1, a.Y)));
        }

        // 外框补齐（基准 SVG 只画了一个 <rect>）
        for (int x = 0; x < n; x++)
        {
            border.Add(Segment.Norm(new Point(x, 0), new Point(x + 1, 0)));
            border.Add(Segment.Norm(new Point(x, n), new Point(x + 1, n)));
        }
        for (int y = 0; y < n; y++)
        {
            border.Add(Segment.Norm(new Point(0, y), new Point(0, y + 1)));
            border.Add(Segment.Norm(new Point(n, y), new Point(n, y + 1)));
        }
        return (n, cut, fold, foldMv, border);
    }

    public sealed class Diff
    {
        public List<Segment> OnlyGen { get; init; } = new();
        public List<Segment> OnlyTruth { get; init; } = new();
        public int Shared { get; init; }
    }

    public sealed class CheckResult
    {
        public int N { get; init; }
        public Diff Fold { get; init; } = new();
        public Diff Cut { get; init; } = new();
        public Diff Border { get; init; } = new();
        public List<Segment> MvMismatch { get; init; } = new();
        public List<Segment> MvOnlyTruth { get; init; } = new();
        public List<Segment> MvOnlyGen { get; init; } = new();
        public bool Ok { get; init; }
    }

    static Diff DiffSets(IEnumerable<Segment> gen, HashSet<Segment> truth)
    {
        var g = gen.ToHashSet();
        var onlyGen = g.Except(truth).OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        var onlyTruth = truth.Except(g).OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        return new Diff { OnlyGen = onlyGen, OnlyTruth = onlyTruth, Shared = g.Intersect(truth).Count() };
    }

    /// <summary>母版推导出的方形 CP vs 权威题面基准 —— 逐项差异报告。<c>Ok</c> 表示全部零差异。</summary>
    public static CheckResult Crosscheck(SquarePlan plan, string truthSvg)
    {
        var t = ParseCreaseSvg(truthSvg);
        var mv = plan.FoldMv;

        var fold = DiffSets(plan.Fold, t.Fold);
        var cut = DiffSets(plan.Cut, t.Cut);
        var border = DiffSets(plan.Border, t.Border);

        var mismatch = t.FoldMv.Where(kv => mv.TryGetValue(kv.Key, out var v) && v != kv.Value)
                                .Select(kv => kv.Key).OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        var onlyTruth = t.FoldMv.Keys.Where(k => !mv.ContainsKey(k))
                                .OrderBy(s => s.A).ThenBy(s => s.B).ToList();
        var onlyGen = mv.Keys.Where(k => !t.FoldMv.ContainsKey(k))
                           .OrderBy(s => s.A).ThenBy(s => s.B).ToList();

        bool ok = fold.OnlyGen.Count == 0 && fold.OnlyTruth.Count == 0 &&
                  cut.OnlyGen.Count == 0 && cut.OnlyTruth.Count == 0 &&
                  border.OnlyGen.Count == 0 && border.OnlyTruth.Count == 0 &&
                  mismatch.Count == 0 && onlyTruth.Count == 0 && onlyGen.Count == 0;

        return new CheckResult
        {
            N = plan.N,
            Fold = fold,
            Cut = cut,
            Border = border,
            MvMismatch = mismatch,
            MvOnlyTruth = onlyTruth,
            MvOnlyGen = onlyGen,
            Ok = ok,
        };
    }

    /// <summary>把 <see cref="Crosscheck"/> 结果格式化成人类可读的报告。</summary>
    public static string FormatReport(string name, CheckResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== {name}: N={res.N}  {(res.Ok ? "全部一致 ✔" : "存在差异 ✘")}");
        foreach (var (role, label) in new[] { ("fold", "折痕"), ("cut", "剪切线"), ("border", "外框") })
        {
            var d = role == "fold" ? res.Fold : role == "cut" ? res.Cut : res.Border;
            bool diff = d.OnlyGen.Count > 0 || d.OnlyTruth.Count > 0;
            sb.AppendLine($"  [{(diff ? "DIFF" : "OK")}] {label}: 共有 {d.Shared}  " +
                          $"仅生成 {d.OnlyGen.Count}  仅基准 {d.OnlyTruth.Count}");
            foreach (var k in d.OnlyGen.Take(6)) sb.AppendLine($"         +生成 {k}");
            foreach (var k in d.OnlyTruth.Take(6)) sb.AppendLine($"         -基准 {k}");
        }
        bool mvDiff = res.MvMismatch.Count + res.MvOnlyTruth.Count + res.MvOnlyGen.Count > 0;
        sb.AppendLine($"  [{(mvDiff ? "DIFF" : "OK")}] 峰谷: 不一致 {res.MvMismatch.Count}  " +
                      $"缺峰谷 {res.MvOnlyTruth.Count}  多出 {res.MvOnlyGen.Count}");
        foreach (var k in res.MvMismatch.Take(6)) sb.AppendLine($"         !峰谷 {k}");
        return sb.ToString().TrimEnd();
    }
}
