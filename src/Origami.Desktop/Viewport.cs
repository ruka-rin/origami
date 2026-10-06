using Avalonia;

namespace Origami.Desktop;

/// <summary>
/// 预览视口的缩放 / 平移运算，<b>纯数学</b>，不碰任何 Avalonia 控件状态。
/// <para>
/// 单独抽出来是为了能直接写单元测试钉死：这套公式错过一次，而且错得
/// <b>看起来还挺正常</b> —— 肉眼只是觉得「缩放没以光标为中心」，很容易当成偏好问题放过。
/// </para>
/// <para>
/// <b>坐标约定</b>：屏幕坐标 = 倍率 × 用户坐标 + 平移量，平移量在缩放<b>之后</b>叠加
/// （所以它是屏幕像素，与倍率无关）。用户坐标的原点由 SVG 的 viewBox 决定，见 <see cref="SvgView"/>。
/// </para>
/// </summary>
public static class Viewport
{
    /// <summary>把屏幕锚点固定住地缩放后，新的平移量。</summary>
    /// <param name="pan">缩放前的平移量（屏幕像素）。</param>
    /// <param name="anchor">光标所在的屏幕坐标。</param>
    /// <param name="before">缩放前的实际倍率。</param>
    /// <param name="after">缩放后的实际倍率。</param>
    /// <returns>缩放后的平移量，使光标下的图像点仍停在原处。</returns>
    /// <remarks>
    /// 推导：光标下的用户坐标 u 满足 <c>anchor = before × u + pan</c>，
    /// 所以 <c>u = (anchor - pan) / before</c>。要让 u 缩放后仍在 anchor 处：
    /// <c>anchor = after × u + pan'</c>，代入得
    /// <c>pan' = anchor - (anchor - pan) × after / before</c>。
    /// </remarks>
    public static double AnchorPan(double pan, double anchor, double before, double after)
        => before <= 0 || after <= 0
            ? pan
            : anchor - (anchor - pan) * (after / before);

    /// <summary>把图缩放到整个视口可见所需的倍率。</summary>
    public static double FitScale(Size viewport, Size content)
        => content.Width <= 0 || content.Height <= 0
           || viewport.Width <= 0 || viewport.Height <= 0
            ? 1.0
            : Math.Min(viewport.Width / content.Width, viewport.Height / content.Height);

    /// <summary>
    /// 限制平移，别让图被拖出视野。图比视口小的方向上<b>居中</b>而不是停在原处 ——
    /// 否则缩小一点就会有一大片空白贴在一边、图偏在角落。
    /// </summary>
    public static double ClampPan(double pan, double viewportSize, double contentSize)
        => contentSize <= viewportSize
            ? (viewportSize - contentSize) / 2
            : Math.Clamp(pan, viewportSize - contentSize, 0);
}