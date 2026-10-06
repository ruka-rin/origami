namespace Origami.Core;

/// <summary>
/// 线段 / 点的集合构造与字典序工具。
/// <para>
/// <b>为什么排序是显式的</b>：几何算法里有几处需要稳定的遍历顺序（面片枚举、
/// 多边形顶点旋转、层序并列打破）。若直接依赖 <c>HashSet</c> / <c>Dictionary</c>
/// 的迭代顺序，结果就会绑死在哈希实现上 —— 换运行时、换版本、甚至换个
/// 插入顺序都可能变。因此所有这些位置一律<b>显式排序</b>（见 <see cref="Sorted"/>、
/// <see cref="SortedPoints"/>），让输出只依赖输入本身。
/// </para>
/// <para>线段与点用 <c>record struct</c> 的值语义相等即可，不需要自定义哈希。</para>
/// </summary>
public static class SegSets
{
    /// <summary>按字典序规范后的线段集合（值语义相等，去重）。</summary>
    public static HashSet<Segment> Create(IEnumerable<Segment> src)
    {
        var s = new HashSet<Segment>();
        foreach (var x in src) s.Add(x);
        return s;
    }

    public static HashSet<Point> CreatePoints(IEnumerable<Point> src)
    {
        var s = new HashSet<Point>();
        foreach (var x in src) s.Add(x);
        return s;
    }
}

/// <summary>线段 / 点的字典序比较（等价于按 (x, y) 逐分量比较）。</summary>
public static class SegOrder
{
    /// <summary>取不到键时用工厂创建并插入。</summary>
    public static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> map,
                                                 TKey key, Func<TValue> factory)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out var v)) map[key] = v = factory();
        return v;
    }

    public static int Compare(Segment a, Segment b)
    {
        int c = Point.Compare(a.A, b.A);
        return c != 0 ? c : Point.Compare(a.B, b.B);
    }

    public static int Compare(Point a, Point b) => Point.Compare(a, b);

    /// <summary>线段按字典序升序 —— 需要稳定输出时显式调用。</summary>
    public static List<Segment> Sorted(IEnumerable<Segment> src)
    {
        var l = src.ToList();
        l.Sort(Compare);
        return l;
    }

    public static List<Point> SortedPoints(IEnumerable<Point> src)
    {
        var l = src.ToList();
        l.Sort(Compare);
        return l;
    }

    /// <summary>线段规范键（带 6 位容差），用于浮点坐标比对。</summary>
    public static Segment Key(Point a, Point b) => Geom.SegKey(a, b);

    public static HashSet<Segment> Keyed(IEnumerable<Segment> src)
    {
        var s = new HashSet<Segment>();
        foreach (var x in src) s.Add(Geom.SegKey(x.A, x.B));
        return s;
    }
}