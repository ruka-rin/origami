using System.Globalization;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Media;

namespace Origami.Desktop;

/// <summary>一个已解析的 SVG 图元。</summary>
public abstract record SvgNode
{
    /// <summary>把图元按<b>用户坐标</b>画到给定变换下。</summary>
    public abstract void Draw(DrawingContext ctx, Matrix transform);
}

sealed record SvgLine(Point A, Point B, Color Stroke, double Width) : SvgNode
{
    public override void Draw(DrawingContext ctx, Matrix m)
    {
        var pen = new Pen(new SolidColorBrush(Stroke), Width);
        ctx.DrawGeometry(null, pen,
            new LineGeometry(new Point(A.X, A.Y), new Point(B.X, B.Y)));
    }
}

sealed record SvgRect(double X, double Y, double W, double H, Color Fill) : SvgNode
{
    public override void Draw(DrawingContext ctx, Matrix m)
        => ctx.DrawRectangle(new SolidColorBrush(Fill),
                             null, new Rect(X, Y, W, H));
}

sealed record SvgPolygon(IReadOnlyList<Point> Points, Color? Fill, Color? Stroke, double Width)
    : SvgNode
{
    public override void Draw(DrawingContext ctx, Matrix m)
    {
        if (Points.Count < 2) return;
        // PolylineGeometry(points, isFilled)：是否闭合由「要不要填充」决定
        var geo = new PolylineGeometry(Points.Select(p => new Point(p.X, p.Y)),
                                       Fill is not null);
        IBrush? brush = Fill is null ? null : new SolidColorBrush(Fill.Value);
        IPen? pen = Stroke is null || Width <= 0
            ? null : new Pen(new SolidColorBrush(Stroke.Value), Width);
        ctx.DrawGeometry(brush, pen, geo);
    }
}

/// <summary>
/// 解析好的 SVG 文档。
/// <para>
/// ★ 只支持本项目自己生成的子集：<c>&lt;rect&gt;</c> / <c>&lt;line&gt;</c> /
/// <c>&lt;polygon&gt;</c> + 少量样式属性。这不是「通用 SVG 渲染器」，
/// 也不打算是 —— 折痕图的元素种类是我们完全可控的有限集合，
/// 支持全特性 SVG 只会带来一堆用不上的代码和攻击面。</para>
/// </summary>
public sealed class SvgDocument
{
    public required Rect ViewBox { get; init; }
    public required IReadOnlyList<SvgNode> Nodes { get; init; }

    public static SvgDocument Parse(string svg)
    {
        var root = XDocument.Parse(svg).Root
                   ?? throw new FormatException("SVG 根元素缺失");

        var vb = Attr(root, "viewBox") is { } s
            ? ParseRect(s)
            : new Rect(0, 0, ParseDouble(Attr(root, "width") ?? "100"),
                             ParseDouble(Attr(root, "height") ?? "100"));

        var nodes = new List<SvgNode>();

        // ★ 必须递归：生成端把所有图元包在 <g> 里，只遍历 root.Elements() 会漏掉
        //   整个 <g> 的内容（表现是「纸底画出来了、线条一条都没有」）。
        //
        // ★ <g> 上的 transform 必须<b>真正施加到坐标上</b>，不能只当分组看。
        //   画布方向（旋转 / 镜像）正是靠 <g transform="matrix(...)"> 表达的
        //   （见 Core 的 SvgTransform.Apply），而 viewBox 此时<b>已经</b>换成
        //   变换后的尺寸。若忽略它：viewBox 变成 50×100 而图形仍是 100×50 的
        //   原始朝向 —— 画面既没转又被裁掉一半，「预览与导出一致」当场失效。
        Walk(root, Matrix.Identity, nodes);

        return new SvgDocument { ViewBox = vb, Nodes = nodes };
    }

    /// <summary>递归遍历，累积祖先链上的变换。</summary>
    static void Walk(XElement e, Matrix parent, List<SvgNode> outp)
    {
        var m = parent * ParseTransform(Attr(e, "transform"));

        switch (e.Name.LocalName)
        {
            case "rect": AddRect(e, m, outp); break;
            case "line": AddLine(e, m, outp); break;
            case "polygon": AddPolygon(e, m, outp); break;
            default: break;      // <g> 只是分组容器，样式已内联到每个图元
        }

        foreach (var child in e.Elements())
            Walk(child, m, outp);
    }

    /// <summary>
    /// 解析 <c>transform</c> 属性。
    /// <para>★ 只认 <c>matrix(a b c d e f)</c>（本项目唯一会写出的形式）。
    /// 其它关键字（<c>rotate</c> / <c>translate</c> / <c>scale</c>）来自外部 SVG，
    /// 遇到就<b>报错</b>而不是静默忽略 —— 静默忽略会画出一张方向与比例全错的图，
    /// 用户却以为是自己看错了。</para>
    /// </summary>
    static Matrix ParseTransform(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Matrix.Identity;

        var parts = s.Trim()
            .Replace('(', ' ').Replace(')', ' ')
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Matrix.Identity;

        if (!parts[0].Equals("matrix", StringComparison.OrdinalIgnoreCase))
            throw new FormatException(
                $"暂不支持 transform=\"{s.Trim()}\"；本工具只生成 matrix(...) 形式");

        if (parts.Length != 7)
            throw new FormatException($"matrix(...) 需要 6 个参数，实际给了 {parts.Length - 1} 个");

        var v = new double[6];
        for (int i = 0; i < 6; i++) v[i] = ParseDouble(parts[i + 1]);

        // SVG: x' = a·x + c·y + e ;  y' = b·x + d·y + f
        return new Matrix(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    static void AddRect(XElement e, Matrix m, List<SvgNode> outp)
    {
        if (Attr(e, "fill") is not { } f || ParseColor(f) is not { } col) return;
        double x = ParseDouble(Attr(e, "x") ?? "0");
        double y = ParseDouble(Attr(e, "y") ?? "0");
        double w = ParseDouble(Attr(e, "width") ?? "0");
        double h = ParseDouble(Attr(e, "height") ?? "0");
        if (w <= 0 || h <= 0) return;

        // 变换后取外接框：整点旋转与镜像下，矩形的像仍是轴对齐矩形
        var p0 = m.Transform(new Point(x, y));
        var p1 = m.Transform(new Point(x + w, y));
        var p2 = m.Transform(new Point(x, y + h));
        var p3 = m.Transform(new Point(x + w, y + h));

        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        outp.Add(new SvgRect(minX, minY, maxX - minX, maxY - minY, col));
    }

    static void AddLine(XElement e, Matrix m, List<SvgNode> outp)
    {
        string? sc = Attr(e, "stroke");
        if (sc is null || ParseColor(sc) is not { } col) return;

        double x1 = ParseDouble(Attr(e, "x1") ?? "0");
        double y1 = ParseDouble(Attr(e, "y1") ?? "0");
        double x2 = ParseDouble(Attr(e, "x2") ?? "0");
        double y2 = ParseDouble(Attr(e, "y2") ?? "0");
        double lw = ParseDouble(Attr(e, "stroke-width") ?? "1");
        if (lw <= 0) return;
        outp.Add(new SvgLine(m.Transform(new Point(x1, y1)),
                             m.Transform(new Point(x2, y2)), col, lw));
    }

    static void AddPolygon(XElement e, Matrix m, List<SvgNode> outp)
    {
        if (Attr(e, "points") is not { } ps) return;
        var pts = ParsePoints(ps);
        if (pts.Count < 2) return;

        for (int i = 0; i < pts.Count; i++) pts[i] = m.Transform(pts[i]);

        Color? fill = Attr(e, "fill") is { } f && f != "none"
            ? ParseColor(f) : null;
        Color? stroke = Attr(e, "stroke") is { } s2 && s2 != "none"
            ? ParseColor(s2) : null;
        double lw = ParseDouble(Attr(e, "stroke-width") ?? "1");
        outp.Add(new SvgPolygon(pts, fill, stroke, lw));
    }

    // ---------------------------------------------------------------- 解析工具

    static string? Attr(XElement e, string name)
        => e.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

    static double ParseDouble(string s)
        => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    static Rect ParseRect(string s)
    {
        var p = s.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        return p.Length >= 4
            ? new Rect(ParseDouble(p[0]), ParseDouble(p[1]),
                       ParseDouble(p[2]), ParseDouble(p[3]))
            : new Rect(0, 0, 100, 100);
    }

    /// <summary>解析 <c>points="x,y x,y ..."</c>。</summary>
    public static List<Point> ParsePoints(string s)
    {
        var pts = new List<Point>();
        foreach (var tok in s.Split(new[] { ' ', '\t', '\n', '\r' },
                                   StringSplitOptions.RemoveEmptyEntries))
        {
            var xy = tok.Split(',');
            if (xy.Length >= 2)
                pts.Add(new Point(ParseDouble(xy[0]), ParseDouble(xy[1])));
        }
        return pts;
    }

    /// <summary>解析颜色。支持 <c>#rgb</c> / <c>#rrggbb</c> / 常见命名色 / <c>none</c>。</summary>
    public static Color? ParseColor(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        if (s.Equals("none", StringComparison.OrdinalIgnoreCase)) return null;

        if (s.StartsWith('#'))
        {
            string h = s[1..];
            if (h.Length == 3)   // #rgb 每位重复一次即得 #rrggbb
            {
                int r = Hex1(h[0]), g = Hex1(h[1]), b = Hex1(h[2]);
                return Color.FromRgb((byte)(r * 17), (byte)(g * 17), (byte)(b * 17));
            }
            if (h.Length >= 6)
                return Color.FromRgb((byte)Hex2(h, 0), (byte)Hex2(h, 2), (byte)Hex2(h, 4));
        }

        return Named.TryGetValue(s, out var c) ? c : null;
    }

    /// <summary>单个十六进制字符 -> 0..15。</summary>
    static int Hex1(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => 0,
    };

    /// <summary>从 <paramref name="i"/> 处取两位十六进制。</summary>
    static int Hex2(string h, int i)
        => int.Parse(h.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    static readonly Dictionary<string, Color> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = Colors.Black,
        ["white"] = Colors.White,
        ["red"] = Color.FromRgb(0xFF, 0, 0),
        ["green"] = Color.FromRgb(0, 0x80, 0),
        ["blue"] = Color.FromRgb(0, 0, 0xFF),
        ["gray"] = Color.FromRgb(0x80, 0x80, 0x80),
        ["grey"] = Color.FromRgb(0x80, 0x80, 0x80),
    };
}