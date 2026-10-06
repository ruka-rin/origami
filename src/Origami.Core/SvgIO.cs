using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace Origami.Core;

/// <summary>
/// 折痕图 SVG &lt;-&gt; 数据模型双向转换（<b>序列化</b>，不是渲染；光栅化在 <see cref="PngIO"/>）。
/// <para>折痕图是<b>单文件自描述</b>的数据载体：<c>data-*</c> 属性携带全部语义，
/// <c>ModelToSvg</c> 与 <c>SvgToModel</c> 严格互逆。这里手写序列化而不用 SVG 库，
/// 是为了完全控制属性顺序与数字格式，保证 <c>svg -&gt; json -&gt; svg</c> 往返
/// 在几何与配色上逐条一致。</para>
/// <para><b>坐标系</b>：<c>strip</c> 是数学坐标（y 向上），写成 SVG 时
/// <c>svg_y = (ncell - paper_y) * unit</c>；<c>square</c> 是屏幕坐标（y 向下），
/// <c>svg_y = paper_y * unit</c>。</para>
/// <para><b>线宽层级</b>：红蓝折痕比绿剪切线与黑边框更细 —— 折痕是主体信息，
/// 辅助参照画粗反而喧宾夺主。折痕图<b>不画任何标题文字</b>。</para>
/// </summary>
public static class SvgIO
{
    public const string SvgNs = "http://www.w3.org/2000/svg";

    /// <summary>配色。与 <see cref="PngIO"/> 的 RGB 常量严格对应。</summary>
    public static readonly Dictionary<string, string> ColorMv = new()
    {
        ["M"] = "#f00",      // 红 = 峰
        ["V"] = "#00f",      // 蓝 = 谷
        ["E"] = "#00f",      // 折耳结构线同蓝（它也参与峰谷交替）
    };
    public const string ColorCut = "#0f0";        // 剪切线 绿
    public const string ColorBorder = "#212121";  // 外框 近黑
    public const string ColorM = "#f00";
    public const string ColorV = "#00f";
    public const string ColorPaper = "#ffffff";   // 纸底
    public const string ColorFaceEdge = "#3c3c3c";// 成品面片描边

    /// <summary>无色模式的统一线色：题面线稿与成品剪影都是纯黑。</summary>
    public const string InkOnly = "#000000";

    static readonly Dictionary<string, string> MvFromColor = new()
    {
        ["#f00"] = "M", ["red"] = "M",
        ["#00f"] = "V", ["blue"] = "V",
    };

    /// <summary>紧凑数字格式，去掉多余的 .0。</summary>
    internal static string Fmt(double v)
    {
        string s = v.ToString("F6", CultureInfo.InvariantCulture)
                    .TrimEnd('0').TrimEnd('.');
        return s is "" or "-0" ? "0" : s;
    }

    static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    // ---- 模型 -> SVG ----

    /// <summary>数据模型 -&gt; 折痕图 SVG <b>文本</b>。坐标系按 <c>kind</c> 自动选择。</summary>
    /// <remarks>不碰文件系统 —— 写盘由调用方（CLI）负责。</remarks>
    public static string ModelToSvg(Model model,
                                    double unit = 48.0, double? pad = null, double lw = 1.6,
                                    bool color = true)
    {
        string kind = model.Kind;
        var segs = new List<(string Role, Segment Seg, string? Mv)>();
        foreach (var s in model.Border) segs.Add(("border", s, null));
        foreach (var s in model.Cut) segs.Add(("cut", s, null));
        foreach (var f in model.Folds) segs.Add(("fold", f.Seg, f.MvType));

        // 长条是 1 x n 的细长条，高度方向只有 1 格 —— y 翻转必须用 1 而不是 n
        int n, wCells, hCells;
        Func<Point, double> Y;
        if (kind == "strip")
        {
            n = model.NCell;
            wCells = n; hCells = 1;
            Y = p => (hCells - p.Y) * unit;          // 数学坐标 -> 屏幕
        }
        else
        {
            n = model.N;
            wCells = n; hCells = n;
            Y = p => p.Y * unit;                     // 已是屏幕坐标
        }
        double padV = pad ?? unit;
        double X(Point p) => p.X * unit;

        double W = wCells * unit + 2 * padV, H = hCells * unit + 2 * padV;

        // 线段坐标直接写全局系（不加 <g transform>）—— 解析端不需反推平移量，往返必然精确
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"{SvgNs}\" version=\"1.1\" viewBox=\"0 0 {Fmt(W)} {Fmt(H)}\" ")
          .Append($"width=\"{Fmt(W)}px\" height=\"{Fmt(H)}px\" data-kind=\"{kind}\" ")
          .Append($"data-unit=\"{Fmt(unit)}\" data-pad=\"{Fmt(padV)}\" data-N=\"{n}\" ")
          .Append($"data-ncell=\"{model.NCell}\">\n");
        sb.Append($"  <rect x=\"0\" y=\"0\" width=\"{Fmt(W)}\" height=\"{Fmt(H)}\" fill=\"#ffffff\"/>\n");
        sb.Append("  <g fill=\"none\" stroke-linecap=\"round\">\n");

        // 先画粗的（边框、剪切线），再画细的（折痕）—— 细线不会被压住
        var order = new Dictionary<string, int> { ["border"] = 0, ["cut"] = 1, ["fold"] = 2 };
        var width = new Dictionary<string, double>
        {
            ["border"] = lw + 1.6, ["cut"] = lw + 0.8, ["fold"] = lw,
        };
        segs = segs.OrderBy(s => order[s.Role]).ToList();       // 稳定排序

        foreach (var (role, seg, mv) in segs)
        {
            // 上色关闭时一律纯黑：这就是「题面线稿」
            string col, extra;
            if (!color) { col = InkOnly; extra = $" data-mv=\"{mv}\""; }
            else if (role == "fold")
            {
                col = ColorMv.TryGetValue(mv ?? "E", out var c0) ? c0 : ColorMv["E"];
                extra = $" data-mv=\"{mv}\"";
            }
            else if (role == "cut") { col = ColorCut; extra = ""; }
            else { col = ColorBorder; extra = ""; }

            sb.Append($"    <line class=\"{role}\"{extra} ")
              .Append($"x1=\"{Fmt(padV + X(seg.A))}\" y1=\"{Fmt(padV + Y(seg.A))}\" ")
              .Append($"x2=\"{Fmt(padV + X(seg.B))}\" y2=\"{Fmt(padV + Y(seg.B))}\" ")
              .Append($"stroke=\"{col}\" stroke-width=\"{Fmt(width[role])}\"/>\n");
        }

        sb.Append("  </g>\n</svg>\n");
        return sb.ToString();
    }

    // ---- SVG -> 模型 ----

    /// <summary>折痕图 SVG <b>文本</b> -&gt; 数据模型。
    /// <paramref name="strict"/> 要求每条 &lt;line&gt; 都带 class，否则按颜色猜测。</summary>
    /// <remarks>不碰文件系统 —— 读盘由调用方（CLI）负责。</remarks>
    public static Model SvgToModel(string svg, bool strict = true)
    {
        var root = XDocument.Parse(svg).Root
                   ?? throw new InvalidOperationException("SVG 根元素缺失");

        double unit = ParseDouble(Attr(root, "data-unit") ?? "24") is var u && u != 0 ? u : 24;
        string? kind = Attr(root, "data-kind");
        int n = int.TryParse(Attr(root, "data-N") ?? "0", out var n0) ? n0 : 0;
        int ncell = int.TryParse(Attr(root, "data-ncell") ?? "0", out var nc) ? nc : 0;

        // pad：生成端写入 data-pad；外部 SVG 若用 <g transform> 则从那里取
        string? padAttr = Attr(root, "data-pad");
        double ox = 0, oy = 0;
        if (padAttr is not null) { ox = oy = ParseDouble(padAttr); }
        else
        {
            foreach (var g in root.Descendants().Where(e => e.Name.LocalName == "g"))
            {
                string? tr = Attr(g, "transform");
                if (tr is null) continue;
                var m = System.Text.RegularExpressions.Regex.Match(
                    tr, @"translate\(\s*(-?\d+(?:\.\d+)?)\s*[, ]\s*(-?\d+(?:\.\d+)?)\s*\)");
                if (m.Success) { ox = ParseDouble(m.Groups[1].Value); oy = ParseDouble(m.Groups[2].Value); break; }
            }
        }

        var cut = new List<Segment>();
        var folds = new List<Fold>();
        var border = new List<Segment>();

        foreach (var line in root.Descendants().Where(e => e.Name.LocalName == "line"))
        {
            var at = line.Attributes()
                .ToDictionary(a => a.Name.LocalName.ToLowerInvariant(), a => a.Value);

            string? cls = at.TryGetValue("class", out var c0) ? c0 : null;
            string col = (at.TryGetValue("stroke", out var s0) ? s0 : "").ToLowerInvariant();

            if (!at.TryGetValue("x1", out var x1s) || !at.TryGetValue("y1", out var y1s) ||
                !at.TryGetValue("x2", out var x2s) || !at.TryGetValue("y2", out var y2s))
                throw new InvalidOperationException("<line> 缺少坐标属性");

            double x1 = (ParseDouble(x1s) - ox) / unit;
            double y1 = (ParseDouble(y1s) - oy) / unit;
            double x2 = (ParseDouble(x2s) - ox) / unit;
            double y2 = (ParseDouble(y2s) - oy) / unit;

            // 屏幕 -> 论文坐标
            Point P(double x, double y) => kind == "strip"
                ? new Point(Geom.Round6(x), Geom.Round6(1 - y))
                : new Point(Geom.Round6(x), Geom.Round6(y));
            var a = P(x1, y1);
            var b = P(x2, y2);

            if (cls is null)
            {
                if (strict)
                    throw new InvalidOperationException(
                        "<line> 缺少 class 属性，无法判定是折痕/剪切线/外框" +
                        "（strict 模式要求显式标注）");
                cls = GuessRole(col);
            }

            switch (cls)
            {
                case "fold":
                    string mv = at.TryGetValue("data-mv", out var mvs) ? mvs : "";
                    if (mv is not (Mv.Mountain or Mv.Valley))
                        mv = MvFromColor.TryGetValue(col, out var mvc) ? mvc
                            : (col is "#1565c0" or "blue" or "#00f" ? "V" : Mv.Ear);
                    folds.Add(new Fold(Segment.Norm(a, b), mv));
                    break;
                case "cut": cut.Add(Segment.Norm(a, b)); break;
                default: border.Add(Segment.Norm(a, b)); break;
            }
        }

        var model = new Model
        {
            Kind = kind ?? "square",
            Unit = unit,
            N = n,
            NCell = ncell,
            Cut = cut,
            Folds = folds,
            Border = border,
        };
        ValidateModel(model);
        return model;
    }

    static string? Attr(XElement e, string name)
        => e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    static double ParseDouble(string s)
        => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    static string GuessRole(string col) => col switch
    {
        "#2e7d32" or "green" => "cut",
        "#212121" or "black" or "#000" => "border",
        _ => "fold",
    };

    // ---- JSON 读写 ----

    /// <summary>数据模型 -&gt; JSON 文件。</summary>
    /// <remarks>
    /// <b>格式约定</b>（<c>JsonToModel</c> 必须能原样读回）：
    /// <list type="bullet">
    /// <item>缩进 2 空格，每个标量各占一行，末尾无换行；</item>
    /// <item>线段写成 <c>[[x1,y1],[x2,y2]]</c>，坐标取整以避免浮点噪声；</item>
    /// <item>键顺序固定（<c>kind</c> → 尺寸 → <c>cut</c> → <c>folds</c> → <c>border</c>
    /// → <c>version</c> → <c>generator</c>），保证同一模型多次写出完全一致。</item>
    /// </list>
    /// </remarks>
    public static string ModelToJson(Model model, int indent = 2)
    {
        // 键顺序即格式约定：strip 与 square 的前几个键不同，其余一致；
        // _pipes 只在 strip 下出现。
        var fields = new List<(string Key, PyVal Val)>();

        if (model.Kind == "strip")
        {
            fields.Add(("kind", PyVal.Str(model.Kind)));
            fields.Add(("ncell", PyVal.Num(model.NCell)));
            fields.Add(("N", PyVal.Num(model.N)));
        }
        else
        {
            fields.Add(("kind", PyVal.Str(model.Kind)));
            fields.Add(("N", PyVal.Num(model.N)));
            fields.Add(("ncell", PyVal.Num(model.NCell)));
            if (model.Unit != 0) fields.Add(("unit", PyVal.Num(model.Unit)));
        }

        fields.Add(("cut", SegArrayVal(model.Cut)));
        fields.Add(("folds", FoldsVal(model.Folds)));
        fields.Add(("border", SegArrayVal(model.Border)));
        if (model.Pipes is not null)
            fields.Add(("_pipes", PyVal.Arr(model.Pipes.Select(p => (PyVal)PyVal.Num(p)).ToList())));

        fields.Add(("version", PyVal.Num(1)));
        fields.Add(("generator", PyVal.Str("origami.svgio")));

        var sb = new StringBuilder();
        PyVal.Obj(fields).Write(sb, 0, indent);
        return sb.ToString();
    }

    abstract class PyVal
    {
        public abstract void Write(StringBuilder sb, int lvl, int indent);

        public static PyVal Str(string s) => new PStr(s);
        public static PyVal Num(double d) => new PNum(d);
        public static PyVal Arr(List<PyVal> items) => new PArr(items);
        public static PyVal Obj(List<(string, PyVal)> fields) => new PObj(fields);
    }

    sealed class PStr(string s) : PyVal
    {
        public override void Write(StringBuilder sb, int lvl, int ind)
            => sb.Append(JsonLite.Quote(s));
    }

    sealed class PNum(double d) : PyVal
    {
        public override void Write(StringBuilder sb, int lvl, int ind)
            => sb.Append(d == Math.Floor(d) && Math.Abs(d) < 1e15
                ? ((long)d).ToString(CultureInfo.InvariantCulture)
                : d.ToString("R", CultureInfo.InvariantCulture));
    }

    sealed class PArr(List<PyVal> items) : PyVal
    {
        public override void Write(StringBuilder sb, int lvl, int ind)
        {
            if (items.Count == 0) { sb.Append("[]"); return; }
            string pad = new(' ', lvl * ind), padIn = new(' ', (lvl + 1) * ind);
            sb.Append("[\n");
            for (int i = 0; i < items.Count; i++)
            {
                sb.Append(padIn);
                items[i].Write(sb, lvl + 1, ind);
                sb.Append(i < items.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(pad).Append(']');
        }
    }

    sealed class PObj(List<(string Key, PyVal Val)> fields) : PyVal
    {
        public override void Write(StringBuilder sb, int lvl, int ind)
        {
            if (fields.Count == 0) { sb.Append("{}"); return; }
            string pad = new(' ', lvl * ind), padIn = new(' ', (lvl + 1) * ind);
            sb.Append("{\n");
            for (int i = 0; i < fields.Count; i++)
            {
                sb.Append(padIn).Append(JsonLite.Quote(fields[i].Key)).Append(": ");
                fields[i].Val.Write(sb, lvl + 1, ind);
                sb.Append(i < fields.Count - 1 ? ",\n" : "\n");
            }
            sb.Append(pad).Append('}');
        }
    }

    /// <summary>单条线段 -&gt; <c>[[x1,y1],[x2,y2]]</c>（不是扁平的四元组）。</summary>
    static PyVal SegVal(Segment s) => PyVal.Arr(new List<PyVal>
    {
        PyVal.Arr(new List<PyVal> { PyVal.Num(Geom.RoundInt(s.A.X)), PyVal.Num(Geom.RoundInt(s.A.Y)) }),
        PyVal.Arr(new List<PyVal> { PyVal.Num(Geom.RoundInt(s.B.X)), PyVal.Num(Geom.RoundInt(s.B.Y)) }),
    });

    static PyVal SegArrayVal(IReadOnlyList<Segment> segs)
        => PyVal.Arr(segs.Select(SegVal).ToList());

    static PyVal FoldsVal(IReadOnlyList<Fold> folds)
        => PyVal.Arr(folds.Select(f => (PyVal)PyVal.Obj(new List<(string, PyVal)>
        {
            ("seg", SegVal(f.Seg)),
            ("mv", PyVal.Str(f.MvType)),
        })).ToList());

    /// <summary>JSON 文件 -&gt; 数据模型。</summary>
    public static Model JsonToModel(string json)
    {
        var doc = JsonLite.Parse(json);
        string Str(string key, string d)
            => doc.TryGetValue(key, out var v) && v.Kind == JsonLite.JsonKind.String ? v.Str : d;
        double Dbl(string key)
            => doc.TryGetValue(key, out var v) && v.Kind == JsonLite.JsonKind.Number ? v.Num : 0;

        var m = new Model
        {
            Kind = Str("kind", "square"),
            N = (int)Dbl("N"),
            NCell = (int)Dbl("ncell"),
            Unit = Dbl("unit"),
            Cut = ReadSegs(doc, "cut"),
            Border = ReadSegs(doc, "border"),
            Folds = new List<Fold>(),
        };
        if (doc.TryGetValue("folds", out var fs) && fs.Kind == JsonLite.JsonKind.Array)
            foreach (var it in fs.Items!)
            {
                // 折痕的 seg 是<b>单条</b>线段 [[x1,y1],[x2,y2]]，不是线段数组
                var seg = ReadOneSeg(it.Map!, "seg");
                string mv = it.TryGetValue("mv", out var mvv) ? mvv.Str : Mv.Valley;
                if (seg is null)
                    throw new FormatException($"JSON 中存在缺少合法 seg 的折痕项（mv={mv}）");
                m.Folds.Add(new Fold(seg.Value, mv));
            }
        ValidateModel(m);
        return m;
    }

    /// <summary>读取<b>单条</b>线段 <c>[[x1,y1],[x2,y2]]</c>。</summary>
    static Segment? ReadOneSeg(IReadOnlyDictionary<string, JsonLite.Value> obj, string key)
    {
        if (obj is null || !obj.TryGetValue(key, out var v) ||
            v.Kind != JsonLite.JsonKind.Array) return null;
        return AsSeg(v);
    }

    /// <summary>把 <c>[[x1,y1],[x2,y2]]</c> 形式的 JSON 数组解析为线段。</summary>
    static Segment? AsSeg(JsonLite.Value v)
    {
        if (v.Kind != JsonLite.JsonKind.Array || v.Items!.Count != 2) return null;
        var p0 = v.Items[0];
        var p1 = v.Items[1];
        if (p0.Kind != JsonLite.JsonKind.Array || p1.Kind != JsonLite.JsonKind.Array) return null;
        if (p0.Items!.Count != 2 || p1.Items!.Count != 2) return null;
        return Segment.Norm(
            new Point(p0.Items![0].AsDouble(), p0.Items[1].AsDouble()),
            new Point(p1.Items![0].AsDouble(), p1.Items[1].AsDouble()));
    }

    /// <summary>读取<b>线段数组</b> <c>[[[x1,y1],[x2,y2]], ...]</c>。</summary>
    static List<Segment> ReadSegs(IReadOnlyDictionary<string, JsonLite.Value> obj, string key)
    {
        var o = new List<Segment>();
        if (obj is null || !obj.TryGetValue(key, out var v) ||
            v.Kind != JsonLite.JsonKind.Array) return o;

        foreach (var it in v.Items!)
        {
            var s = AsSeg(it);
            if (s is not null) o.Add(s.Value);
        }
        return o;
    }

    // ---- 校验 / 摘要 ----

    /// <summary>基本自洽性检查：线段无重复、坐标为整数格点、折痕都有合法峰谷。</summary>
    public static void ValidateModel(Model model, string src = "")
    {
        string tag = src.Length > 0 ? $"[{src}] " : "";
        var seen = new Dictionary<Segment, int>();
        void Bump(Segment s) => seen[s] = seen.TryGetValue(s, out var v) ? v + 1 : 1;

        foreach (var s in model.Cut) Bump(Geom.SegKey(s.A, s.B));
        foreach (var s in model.Border) Bump(Geom.SegKey(s.A, s.B));
        foreach (var f in model.Folds) Bump(Geom.SegKey(f.Seg.A, f.Seg.B));

        var dup = seen.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
        if (dup.Count > 0)
            throw new InvalidOperationException($"{tag}存在重复线段 {dup.Count} 条，例如 {dup[0]}");

        foreach (var f in model.Folds)
        {
            if (f.MvType is not (Mv.Mountain or Mv.Valley or Mv.Ear))
                throw new InvalidOperationException($"{tag}折痕缺少合法峰谷: {f.Seg}");
            foreach (var p in new[] { f.Seg.A, f.Seg.B })
                if (Math.Abs(p.X - Math.Round(p.X)) > 1e-6 ||
                    Math.Abs(p.Y - Math.Round(p.Y)) > 1e-6)
                    throw new InvalidOperationException($"{tag}坐标必须为整数格点: {f.Seg}");
        }
    }

    /// <summary>一行摘要。</summary>
    public static string Describe(Model model)
    {
        var folds = model.Folds;
        int nm = folds.Count(f => f.MvType == Mv.Mountain);
        var kinds = folds.GroupBy(f => f.Seg.Class())
                         .ToDictionary(g => g.Key, g => g.Count());
        int diag = (kinds.TryGetValue("/", out var d1) ? d1 : 0) +
                   (kinds.TryGetValue("\\", out var d2) ? d2 : 0);
        int nv = kinds.TryGetValue("V", out var v1) ? v1 : 0;
        int nh = kinds.TryGetValue("H", out var h1) ? h1 : 0;
        return $"{model.Kind} N={model.N} 格={model.NCell} " +
               $"折痕{folds.Count}(峰{nm}/谷{folds.Count - nm} 斜{diag}/竖{nv}/横{nh}) " +
               $"剪切{model.Cut.Count} 外框{model.Border.Count}";
    }

    // ---- 成品 / 原子片 —— 视图层与 CLI 共用 ----

    /// <summary>
    /// 折叠成品 -&gt; SVG 文本。面片按<b>层序</b>从下往上叠画，天然带遮挡关系。
    /// <paramref name="color"/> 关闭时是<b>纯黑剪影</b>（去掉颜色只看形状）。
    /// <para><paramref name="flipY"/> 决定输入坐标朝向：长条顺次折叠是数学坐标
    ///（y 向上）需 <c>true</c>；方形树反射已是屏幕坐标（y 向下）需 <c>false</c>。
    /// 混用会让成品上下颠倒。</para>
    /// </summary>
    public static string FoldedToSvg(IReadOnlyList<List<Point>> world,
                                     IReadOnlyList<int> rank,
                                     IReadOnlyDictionary<int, string>? mvMap,
                                     IReadOnlyList<string>? mvSeq = null,
                                     double unit = 20.0, double pad = 14.0,
                                     bool color = true, bool flipY = true)
    {
        var live = Enumerable.Range(0, world.Count)
                             .Where(i => Geom.PolyArea(world[i]) > 1e-9)
                             .ToList();
        if (live.Count == 0) throw new InvalidOperationException("没有可绘制的面片");

        var (x0, y0, x1, y1) = Geom.PolyBbox(world);
        int W = (int)((x1 - x0) * unit) + 2 * (int)pad;
        int H = (int)((y1 - y0) * unit) + 2 * (int)pad;

        double Sx(Point p) => pad + (p.X - x0) * unit;
        double Sy(Point p) => flipY ? pad + (y1 - p.Y) * unit : pad + (p.Y - y0) * unit;

        string FaceMv(int i)
        {
            if (mvMap is { Count: > 0 })
                return mvMap.TryGetValue(i, out var v) ? v : Mv.Valley;
            if (mvSeq is { Count: > 0 }) return i < mvSeq.Count ? mvSeq[i] : mvSeq[^1];
            return Mv.Valley;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append("  <g fill=\"none\" stroke-linejoin=\"round\" stroke-linecap=\"round\"")
          .Append(color ? $" stroke=\"{ColorFaceEdge}\" stroke-width=\"1\"" : " stroke=\"none\"")
          .Append(">\n");

        foreach (int i in live.OrderBy(k => rank[k]))
        {
            var pts = string.Join(" ", world[i].Select(p => $"{Fmt(Sx(p))},{Fmt(Sy(p))}"));
            string m = FaceMv(i);
            string fill = !color
                ? InkOnly
                : ColorMv.TryGetValue(m, out var c) ? c : ColorMv["E"];
            sb.Append($"    <polygon class=\"face\" data-mv=\"{m}\" data-rank=\"{rank[i]}\" ")
              .Append($"points=\"{pts}\" fill=\"{fill}\"/>\n");
        }

        sb.Append("  </g>\n");
        return Svg.Doc(W, H, "folded", sb.ToString(), $"data-unit=\"{Fmt(unit)}\"");
    }

    /// <summary>
    /// 原子片分解图 -&gt; SVG 文本：<b>每个面片一种颜色</b>。
    /// <para>用途是查漏面 / 查重叠：正确的折叠里每片颜色都应唯一可辨；若出现两片同色相邻、
    /// 或某片颜色「消失」，说明面片划分或层序有问题。颜色按面下标用黄金角散列生成 ——
    /// 不用 <c>Random</c>，保证跨平台一致。</para>
    /// </summary>
    public static string PiecesToSvg(IReadOnlyList<List<Point>> world,
                                     double unit = 18.0, double pad = 14.0,
                                     bool flipY = true, int seed = 7)
    {
        var live = Enumerable.Range(0, world.Count)
                             .Where(i => Geom.PolyArea(world[i]) > 1e-9)
                             .ToList();
        if (live.Count == 0) throw new InvalidOperationException("没有可绘制的面片");

        var (x0, y0, x1, y1) = Geom.PolyBbox(world);
        int W = (int)((x1 - x0) * unit) + 2 * (int)pad;
        int H = (int)((y1 - y0) * unit) + 2 * (int)pad;

        double Sx(Point p) => pad + (p.X - x0) * unit;
        double Sy(Point p) => flipY ? pad + (y1 - p.Y) * unit : pad + (p.Y - y0) * unit;

        var sb = new System.Text.StringBuilder();
        sb.Append($"  <g fill=\"none\" stroke=\"{ColorFaceEdge}\" stroke-width=\"0.6\" ")
          .Append("stroke-linejoin=\"round\">\n");

        int k = 0;
        foreach (int i in live)          // 按面下标顺序（层序无关 —— 分解图要看得见每一片）
        {
            var pts = string.Join(" ", world[i].Select(p => $"{Fmt(Sx(p))},{Fmt(Sy(p))}"));
            sb.Append($"    <polygon class=\"piece\" data-i=\"{i}\" points=\"{pts}\" ")
              .Append($"fill=\"{Svg.PieceColor(k++, seed)}\"/>\n");
        }

        sb.Append("  </g>\n");
        return Svg.Doc(W, H, "pieces", sb.ToString(), $"data-unit=\"{Fmt(unit)}\"");
    }
}