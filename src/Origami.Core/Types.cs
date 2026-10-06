namespace Origami.Core;

/// <summary>
/// 平面上的数据点。坐标用 <see cref="double"/> 承载，但整条工具链里出现的坐标
/// 几乎都是格点整数倍 —— 比较与取整都按整数语义处理。
/// </summary>
public readonly record struct Point(double X, double Y) : IComparable<Point>
{
    public static readonly Point Origin = new(0, 0);

    /// <summary>字典序比较：先 x 后 y。</summary>
    public static bool operator <=(Point a, Point b) => Compare(a, b) <= 0;
    public static bool operator <(Point a, Point b) => Compare(a, b) < 0;
    public static bool operator >(Point a, Point b) => Compare(a, b) > 0;
    public static bool operator >=(Point a, Point b) => Compare(a, b) >= 0;

    public int CompareTo(Point other) => Compare(this, other);

    /// <summary>字典序比较：先比 x，相等再比 y。</summary>
    public static int Compare(Point a, Point b)
    {
        int c = a.X.CompareTo(b.X);
        return c != 0 ? c : a.Y.CompareTo(b.Y);
    }

    public override string ToString() => $"({Fmt(X)}, {Fmt(Y)})";

    internal static string Fmt(double v)
    {
        if (v == Math.Floor(v) && Math.Abs(v) < 1e15) return ((long)v).ToString();
        return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
    }
}

/// <summary>
/// 无向线段：两端点按字典序规范（左端点在前）。
/// </summary>
public readonly record struct Segment(Point A, Point B)
{
    public bool IsVertical => A.X == B.X;
    public bool IsHorizontal => A.Y == B.Y;

    /// <summary>把线段规范成字典序小的端点在前。</summary>
    public static Segment Norm(Point a, Point b) => a <= b ? new Segment(a, b) : new Segment(b, a);

    /// <summary>线段类别：<c>V</c> 竖直 / <c>H</c> 水平 / 对角（区分 <c>/</c> 与 <c>\</c>）。</summary>
    public string Class() => Geom.SegClass(A, B);

    public override string ToString() => $"({A} -> {B})";
}

/// <summary>RGB 颜色。★ 与 <see cref="SvgIO"/> 的配色常量严格对应。</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Red = new(255, 0, 0);       // 峰   #f00
    public static readonly Rgb Blue = new(0, 0, 255);      // 谷   #00f
    public static readonly Rgb Green = new(0, 255, 0);     // 剪切线 #0f0
    public static readonly Rgb Border = new(0x21, 0x21, 0x21);
}

/// <summary>3x3 齐次矩阵 —— 反射与刚体变换都用它表示。</summary>
public readonly struct Mat3
{
    public readonly double M00, M01, M02, M10, M11, M12, M20, M21, M22;

    public Mat3(double m00, double m01, double m02,
                double m10, double m11, double m12,
                double m20, double m21, double m22)
    {
        M00 = m00; M01 = m01; M02 = m02;
        M10 = m10; M11 = m11; M12 = m12;
        M20 = m20; M21 = m21; M22 = m22;
    }

    public static Mat3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static Mat3 operator *(Mat3 a, Mat3 b) => new(
        a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20,
        a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21,
        a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22,
        a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20,
        a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21,
        a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22,
        a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20,
        a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21,
        a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22);

    public Point Apply(Point p) => new(
        M00 * p.X + M01 * p.Y + M02,
        M10 * p.X + M11 * p.Y + M12);

    public override string ToString() =>
        $"[{M00} {M01} {M02}; {M10} {M11} {M12}; {M20} {M21} {M22}]";
}