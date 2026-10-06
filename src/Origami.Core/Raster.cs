using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace Origami.Core;

/// <summary>
/// 渲染目标：一块 RGB 位图。
/// <para>
/// 底层是 <b>ImageSharp</b>（Apache-2.0，纯托管、跨平台、无原生依赖），
/// 抗锯齿与 PNG 编码都交给它 —— 这类图形基础设施没有理由自己重造。
/// 本类只做<b>领域适配</b>：把折痕模型里的坐标体系（数学坐标 / 屏幕坐标、
/// 格单位 / 像素单位）翻译成画布坐标。</para>
/// </summary>
public sealed class Raster : IDisposable
{
    private readonly Image<Rgb24> _img;

    public int Width => _img.Width;
    public int Height => _img.Height;

    private Raster(Image<Rgb24> img) => _img = img;

    public static Raster Create(int width, int height, Rgb bg)
    {
        int w = Math.Max(1, width), h = Math.Max(1, height);

        // ★ 直接按像素填充，不用 Mutate(c => c.BackgroundColor(...))：
        //   ImageSharp 2.x 的 BackgroundColor 是「与现有像素混合」，而新建图像是
        //   透明黑 (0,0,0)，所以混白之后仍是黑色 —— 整张图背景会变黑。
        var buf = new byte[w * h * 3];
        for (int i = 0; i < buf.Length; i += 3)
        {
            buf[i] = bg.R;
            buf[i + 1] = bg.G;
            buf[i + 2] = bg.B;
        }
        return new Raster(Image.LoadPixelData<Rgb24>(buf, w, h));
    }

    internal static Color ToColor(Rgb c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>画一条<b>抗锯齿粗线</b>。</summary>
    public void DrawLine(double x1, double y1, double x2, double y2, Rgb color, double width)
        => _img.Mutate(c => c.DrawLine(ToColor(color), Math.Max(1f, (float)width),
                                       new PointF((float)x1, (float)y1),
                                       new PointF((float)x2, (float)y2)));

    /// <summary>
    /// 填充多边形（可选描边）。<paramref name="fill"/> 为 null 时<b>只描边不填充</b>。
    /// </summary>
    /// <remarks>
    /// ★ <paramref name="fill"/> 为 null 时<b>不能</b>调<code>FillPolygon</code>：
    /// ImageSharp 的多边形填充默认用透明黑，会把已画好的白底/下层面片覆盖成黑色。
    /// 这里靠「跳过填充」来保持背景，这正是「只描边」的语义。
    /// </remarks>
    public void FillPolygon(IReadOnlyList<Point> poly, Rgb? fill, Rgb? outline, double lw)
    {
        if (poly.Count < 2) return;
        var pts = new PointF[poly.Count];
        for (int i = 0; i < poly.Count; i++) pts[i] = new PointF((float)poly[i].X, (float)poly[i].Y);

        _img.Mutate(c =>
        {
            if (fill is not null) c.FillPolygon(ToColor(fill.Value), pts);
            if (outline is not null && lw > 0) c.DrawPolygon(ToColor(outline.Value), (float)lw, pts);
        });
    }

    /// <summary>缩放（双三次重采样）。用于超采样后降采样得到抗锯齿结果。</summary>
    public void Resize(int width, int height)
        => _img.Mutate(c => c.Resize(Math.Max(1, width), Math.Max(1, height),
                                     KnownResamplers.Triangle));

    /// <summary>
    /// 施加画布方向（整点旋转 + 镜像）：<b>重新采样</b>，即改变画布尺寸。
    /// </summary>
    /// <remarks>
    /// ★ 为什么在这里、而不是在绘图前把坐标变换掉：那要同时改
    ///   <c>PngIO.Render*</c> 里所有 <c>unit</c> / <c>pad</c> / <c>flipY</c> 的推导，
    ///   六条渲染路径都得改一遍，极易漏；而<b>先按原方向画完、再整体变换</b>
    ///   只有一处入口，且与 SVG 侧「生成后施加变换」的结构完全对称。
    ///   <br/>
    /// ★ 整点旋转与镜像在光栅上是<b>精确的像素置换</b>，没有插值损失。
    /// </remarks>
    public void Transform(CanvasTransform t)
    {
        if (t.IsIdentity) return;

        // 顺序与 SVG 侧一致：先翻转、后旋转。
        // 两次旋转合成一次调用，减少一次无谓的全图重排。
        if (t.FlipX) _img.Mutate(c => c.Flip(FlipMode.Horizontal));
        if (t.FlipY) _img.Mutate(c => c.Flip(FlipMode.Vertical));

        int q = ((t.QuarterTurns % 4) + 4) % 4;
        if (q != 0)
            _img.Mutate(c => c.Rotate(q * 90));   // ImageSharp 正角度 = 顺时针
    }

    /// <summary>把像素数据取出来（供测试逐像素比对）。</summary>
    public byte[] ToRgbBytes()
    {
        // ImageSharp 2.x：像素访问走 ImageFrameCollection
        // Rgb24 在内存里就是 3 字节/像素的紧凑排列
        var buf = new byte[Width * Height * 3];
        _img.Frames.RootFrame.CopyPixelDataTo(buf);
        return buf;
    }

    /// <summary>编码为 PNG 字节流（不碰文件系统）。</summary>
    public byte[] ToPngBytes()
    {
        using var ms = new MemoryStream();
        _img.SaveAsPng(ms);
        return ms.ToArray();
    }

    public void Dispose() => _img.Dispose();
}