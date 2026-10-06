using System.Globalization;
using System.Xml.Linq;

namespace Origami.Core;

/// <summary>
/// 把 <see cref="CanvasTransform"/> 施加到一段已生成的 SVG 文本上。
/// <para>
/// <b>预览与导出走同一条路径</b>：预览渲染的 SVG 与导出的 SVG 是<b>同一段文本</b>，
/// 所以「屏幕上看到的方向」与「存到文件里的方向」不可能不一致 —— 这正是把方向做成
/// 显示层变换、而不是去改折叠算法约定的原因。</para>
/// </summary>
public static class SvgTransform
{
    /// <summary>
    /// 就地施加画布方向：把全部图元包进 <c>&lt;g transform="matrix(...)"&gt;</c>，
    /// 并按变换后的尺寸改写 <c>viewBox</c> / <c>width</c> / <c>height</c>。
    /// </summary>
    /// <remarks>
    /// <b>不改写任何图元坐标</b>：整点旋转与镜像在 SVG 里就是一个仿射矩阵，包一层
    /// <c>&lt;g&gt;</c> 即可，保持了折痕图「坐标即数据」的直白性
    /// （<see cref="SvgIO.SvgToModel"/> 仍能原样读回 —— 变换只影响呈现，不影响数据）。
    /// <b>viewBox 必须同步改</b>：转 90° 后画布宽高互换，若沿用原 viewBox，
    /// 内容会被裁掉一半。纸底 <c>&lt;rect&gt;</c> 同样要一起变换，否则会露出一块没被旋转的底色。
    /// </remarks>
    public static string Apply(string svg, CanvasTransform t)
    {
        if (t.IsIdentity) return svg;

        XDocument doc;
        try { doc = XDocument.Parse(svg); }
        catch (Exception ex)
        {
            throw new FormatException("SVG 解析失败，无法施加方向变换：" + ex.Message, ex);
        }

        var root = doc.Root ?? throw new FormatException("SVG 根元素缺失");
        if (root.Name.LocalName != "svg")
            throw new FormatException($"根元素是 <{root.Name.LocalName}>，不是 <svg>");

        var (w, h) = ReadSize(root);
        var (nw, nh) = t.SizeOf(w, h);

        var (mA, mB, mC, mD, mE, mF) = t.Matrix(w, h);

        // 把所有子元素（含纸底 rect）移进一个新的 <g>
        var content = new XElement("g",
            new XAttribute("transform", MatrixText(mA, mB, mC, mD, mE, mF)));
        foreach (var child in root.Elements().ToList())
        {
            child.Remove();
            content.Add(child);
        }
        root.Add(content);

        // viewBox / width / height 同步为变换后的尺寸
        root.SetAttributeValue("viewBox", $"0 0 {Num(nw)} {Num(nh)}");
        root.SetAttributeValue("width", Num(nw) + "px");
        root.SetAttributeValue("height", Num(nh) + "px");

        // 记录当前方向，便于读回时判断（纯呈现信息，不参与数据还原）
        root.SetAttributeValue("data-xform", MatrixText(mA, mB, mC, mD, mE, mF));

        return doc.Declaration == null
            ? doc.ToString(SaveOptions.DisableFormatting)
            : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
    }

    static string MatrixText(double a, double b, double c, double d, double e, double f)
        => $"matrix({Num(a)} {Num(b)} {Num(c)} {Num(d)} {Num(e)} {Num(f)})";

    static string Num(double v) => v.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>从 <c>viewBox</c>（优先）或 <c>width</c>/<c>height</c> 读出画布尺寸。</summary>
    static (double W, double H) ReadSize(XElement root)
    {
        string? vb = root.Attribute("viewBox")?.Value;
        if (!string.IsNullOrWhiteSpace(vb))
        {
            var p = vb.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 &&
                double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double w) &&
                double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double h) &&
                w > 0 && h > 0)
                return (w, h);
        }

        double pw = ParseLen(root.Attribute("width")?.Value);
        double ph = ParseLen(root.Attribute("height")?.Value);
        if (pw > 0 && ph > 0) return (pw, ph);

        throw new FormatException("SVG 没有可用的 viewBox / width / height，无法施加方向变换");
    }

    static double ParseLen(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var t = s.Trim().TrimEnd('p', 'x', 'P', 'X');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }
}