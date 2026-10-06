namespace Origami.Core;

/// <summary>
/// SVG 构建的公共约定。
/// <para>
/// 折痕图是<b>单文件自描述</b>的：<c>data-*</c> 属性携带全部语义，无需 sidecar
/// 即可还原数据模型，所以 <c>svg -&gt; model -&gt; svg</c> 往返必然精确。</para>
/// <para><b>上色是一个开关，不是两种产物</b>。同一张图，关掉颜色就是题面线稿
/// （全黑）或成品剪影（纯黑块）。因此 <c>ModelToSvg</c> /
/// <see cref="FoldedToSvg"/> / <see cref="PiecesToSvg"/> 都接受
/// <c>color</c> 参数，视图层只管切开关，不需要为黑白/彩色各写一套生成逻辑。</para>
/// </summary>
public static class Svg
{
    /// <summary>
    /// 原子片分解图的配色：按面下标稳定地取一个<b>浅色</b>。
    /// <para>用黄金角步进而不是 <c>Random</c>：同一个母版在任何机器、任何平台上
    /// 必须得到同一张图，否则没法拿它做回归对比。</para>
    /// </summary>
    public static string PieceColor(int index, int seed = 7)
    {
        // 黄金角散列，保证相邻面片的颜色差异足够大
        double h = (index + 1) * 0.618033988749895 + seed * 0.381966011250105;
        h -= Math.Floor(h);
        return Hsv(h, 0.55, 0.92);
    }

    /// <summary>HSL 转 hex（这里固定 S/V，只用 H 变化）。</summary>
    static string Hsv(double h, double s, double v)
    {
        var (r, g, b) = HsvToRgb(h, s, v);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>HSV -> RGB。</summary>
    /// <remarks>
    /// 只用到 H 变化、S/V 固定的场景，但保留完整公式以便调整。
    /// i 取 6 个色扇区间之一，p/q/t 分别是该扇区的三个分量。
    /// </remarks>
    static (int R, int G, int B) HsvToRgb(double h, double s, double v)
    {
        int i = (int)Math.Floor(h * 6.0) % 6;
        double f = h * 6.0 - Math.Floor(h * 6.0);
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);

        double r, g, b;
        switch (i)
        {
            case 0: (r, g, b) = (v, t, p); break;
            case 1: (r, g, b) = (q, v, p); break;
            case 2: (r, g, b) = (p, v, t); break;
            case 3: (r, g, b) = (p, q, v); break;
            case 4: (r, g, b) = (t, p, v); break;
            default: (r, g, b) = (v, p, q); break;
        }
        return (R(r), R(g), R(b));
    }

    static int R(double r) => (int)Math.Round(r * 255);

    /// <summary>SVG 文档骨架：声明 + 纸底 + 图形组。</summary>
    public static string Doc(int w, int h, string kind, string inner, string? extraAttrs = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append($"<svg xmlns=\"{SvgIO.SvgNs}\" version=\"1.1\" viewBox=\"0 0 {w} {h}\" ")
          .Append($"width=\"{w}px\" height=\"{h}px\" data-kind=\"{kind}\"");
        if (!string.IsNullOrEmpty(extraAttrs)) sb.Append(' ').Append(extraAttrs);
        sb.Append(">\n");
        sb.Append($"  <rect x=\"0\" y=\"0\" width=\"{w}\" height=\"{h}\" fill=\"{SvgIO.ColorPaper}\"/>\n");
        sb.Append(inner);
        sb.Append("</svg>\n");
        return sb.ToString();
    }
}