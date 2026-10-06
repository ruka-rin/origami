namespace Origami.Core;

/// <summary>
/// 画布方向：<b>整点旋转</b> + <b>镜像翻转</b>。
/// <para>
/// 用途：折痕推出来的成品偶尔朝向不对（字母倒了、镜像了），与其去改折叠算法里的
/// 方向约定，不如在<b>显示与导出层</b>给整个画布一个变换 —— 这样同一张图可以来回对照，
/// 而且预览与导出走<b>同一个变换</b>，不会出现「屏幕上是对的、导出后又不���」的情况。</para>
/// <para>
/// ★ 只支持<b>90° 的整数倍</b>与水平/垂直镜像。这个限制是刻意的，也是它可靠的��因：
///   任意角度要重采样，位图导出会糊、矢量导出会走变形路径，两边对不上；
///   而整点旋转与镜像在光栅上是<b>精确的像素置换</b> —— 零损失、可逆、
///   且四个旋转档位与两个镜像开关共 16 种组合可以用<b>同一套矩阵</b>表达。</para>
/// <para>
/// <b>约定</b>：<b>先翻转、后旋转</b>，两步都以变换当下的画布尺寸为基准。
/// 见 <see cref="Matrix"/>。</para>
/// </summary>
/// <param name="QuarterTurns">顺时针 90° 的次数，归一化到 0..3。</param>
/// <param name="FlipX">是否左右镜像。</param>
/// <param name="FlipY">是否上下镜像。</param>
public readonly record struct CanvasTransform(int QuarterTurns, bool FlipX, bool FlipY)
{
    /// <summary>恒等变换（不旋转、不翻转）。</summary>
    public static readonly CanvasTransform Identity = new(0, false, false);

    /// <summary>构造并把旋转次数归一化到 0..3（负数 / 超范围都能正确处理）。</summary>
    public static CanvasTransform Create(int quarterTurns, bool flipX = false, bool flipY = false)
        => new(((quarterTurns % 4) + 4) % 4, flipX, flipY);

    /// <summary>是否是恒等变换（可以不产生任何额外工作）。</summary>
    public bool IsIdentity => QuarterTurns == 0 && !FlipX && !FlipY;

    /// <summary>再顺时针转 90°。</summary>
    public CanvasTransform RotateCw() => Create(QuarterTurns + 1, FlipX, FlipY);

    /// <summary>再逆时针转 90°。</summary>
    public CanvasTransform RotateCcw() => Create(QuarterTurns - 1, FlipX, FlipY);

    /// <summary>左右镜像 / 还原。</summary>
    public CanvasTransform MirrorX(bool on = true) => new(QuarterTurns, on, FlipY);

    /// <summary>上下镜像 / 还原。</summary>
    public CanvasTransform MirrorY(bool on = true) => new(QuarterTurns, FlipX, on);

    /// <summary>
    /// 变换后的画布尺寸：转了奇数次（90°/270°）就宽高互换。
    /// </summary>
    public (double W, double H) SizeOf(double w, double h)
        => QuarterTurns % 2 == 0 ? (w, h) : (h, w);

    // ---------------------------------------------------------------- 矩阵

    // SVG matrix(a b c d e f) 的语义：x' = a·x + c·y + e ;  y' = b·x + d·y + f
    //
    // ★ 这里刻意<b>不用手推switch</b>，而是把三个基元（左右镜像 / 上下镜像 / 顺时针 90°）
    //   按「先翻转后旋转」的次序<b>组合</b>出来。手推要分 16 种组合分别写死系数，
    //   实测写错了 4 种（q=1/3 与镜像同时开启时整体平移量算错），而且错得极隐蔽：
    //   图形还在、只是位置偏了，肉眼很难立刻归因到矩阵系数。
    //   组合写法只有一条构造路径，「转 4 次回恒等」「镜像两次回恒等」这类不变式
    //   可以直接验证（见 CanvasTransformTests）。

    readonly record struct M(double A, double B, double C, double D, double E, double F)
    {
        public static readonly M Identity = new(1, 0, 0, 1, 0, 0);

        /// <summary>本变换之后再施加 <paramref name="next"/>。</summary>
        public M Then(M next) => new(
            next.A * A + next.C * B, next.B * A + next.D * B,
            next.A * C + next.C * D, next.B * C + next.D * D,
            next.A * E + next.C * F + next.E,
            next.B * E + next.D * F + next.F);

        public (double X, double Y) Apply(double x, double y)
            => (A * x + C * y + E, B * x + D * y + F);
    }

    /// <summary>左右镜像：x -&gt; cw - x。</summary>
    static M FlipXAt(double cw) => new(-1, 0, 0, 1, cw, 0);

    /// <summary>上下镜像：y -&gt; ch - y。</summary>
    static M FlipYAt(double ch) => new(1, 0, 0, -1, 0, ch);

    /// <summary>顺时针 90°：(x, y) -&gt; (ch - y, x)。</summary>
    static M RotCw(double ch) => new(0, 1, -1, 0, ch, 0);

    /// <summary>
    /// SVG <c>matrix(a b c d e f)</c> 形式的变换，可直接写进 <c>&lt;g transform="matrix(...)"&gt;</c>。
    /// </summary>
    /// <param name="w">变换<b>前</b>的画布宽。</param>
    /// <param name="h">变换<b>前</b>的画布高。</param>
    /// <returns>(a, b, c, d, e, f)，语义同 SVG <c>matrix()</c>。</returns>
    public (double A, double B, double C, double D, double E, double F) Matrix(double w, double h)
    {
        var t = M.Identity;

        // 1) 翻转（不改变画布尺寸，故两者用同一组 w/h）
        if (FlipX) t = t.Then(FlipXAt(w));
        if (FlipY) t = t.Then(FlipYAt(h));

        // 2) 旋转：每次都按<b>当前</b>画布高构造基元，然后<b>交换宽高</b>
        //
        // ★ 必须把宽高两个值都带着交换，不能只留一个变量：
        //   转 180° 后画布高度要变回原来的 h，若只写 ch = w，
        //   第二圈起基元就用错了参照，整体平移量会算错
        //   （表现为「转 90° 正常、转 270° 图偏出去半张」）。
        double cw = w, ch = h;
        for (int i = 0; i < QuarterTurns; i++)
        {
            t = t.Then(RotCw(ch));
            (cw, ch) = (ch, cw);
        }

        return (t.A, t.B, t.C, t.D, t.E, t.F);
    }

    /// <summary>把一个点按本变换映射过去（供光栅化与测试使用）。</summary>
    public Point Apply(Point p, double w, double h)
    {
        var (a, b, c, d, e, f) = Matrix(w, h);
        return new Point(a * p.X + c * p.Y + e, b * p.X + d * p.Y + f);
    }

    /// <summary>供界面显示的简短描述。</summary>
    public string Describe()
    {
        if (IsIdentity) return "原始方向";
        var parts = new List<string>(3);
        if (QuarterTurns != 0) parts.Add($"旋转 {QuarterTurns * 90}°");
        if (FlipX) parts.Add("左右镜像");
        if (FlipY) parts.Add("上下镜像");
        return string.Join(" · ", parts);
    }
}