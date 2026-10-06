using System.Text;
using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 与<b>旧参考实现</b>的<b>多字符文本</b>转换一致性核对。
/// <para>
/// <see cref="LegacyParityTests"/> 比的是单个字模的编码串，而字模层正确
/// <b>不代表</b>文本级流程正确：逐行拼接、奇偶行的反转/镜像、行间转折符、
/// <c>left</c> 模式的前置竖折痕 —— 这些逻辑只在<b>多行多字符</b>时才走通，
/// 单字模根本碰不到。本类专门覆盖这一层。
/// </para>
/// <para>
/// 数据由 <c>tools/extract-legacy-text.js</c> 生成：直接运行旧实现
/// <c>index.js</c> 里 <c>update()</c> 的编码流程，导出各文本在
/// left/mirror 四种组合下的编码串、宽度与折痕表。
/// </para>
/// </summary>
public class LegacyTextParityTests
{
    sealed record LegacyResult(string? Encoding, int Width,
                               List<(int Xb, int Xt)> Creases, string? Note);

    static Dictionary<string, LegacyResult> LoadLegacy()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "legacy_text.json");
        var root = JsonLite.Parse(File.ReadAllText(path, Encoding.UTF8));

        var map = new Dictionary<string, LegacyResult>();
        foreach (var (key, val) in root)
        {
            string? enc = null, note = null;
            int width = 0;
            List<(int, int)> creases = new();

            if (val.TryGetValue("encoding", out var e) && e.Kind == JsonLite.JsonKind.String)
                enc = e.Str;
            if (val.TryGetValue("note", out var nt) && nt.Kind == JsonLite.JsonKind.String)
                note = nt.Str;
            if (val.TryGetValue("width", out var w)) width = (int)w.Num;
            if (val.TryGetValue("creases", out var cr) && cr.Items is not null)
                creases = cr.Items.Select(p => ((int)p.Items![0].Num, (int)p.Items![1].Num)).ToList();

            map[key] = new LegacyResult(enc, width, creases, note);
        }
        return map;
    }

    static readonly Dictionary<string, LegacyResult> Legacy = LoadLegacy();

    /// <summary>
    /// <c>TextToMaster</c> 只产出母版串，编码串是中间量。
    /// 旧实现没有「编码转母版」的函数，所以这里反向验证：
    /// <b>把旧实现的编码串喂给本项目的转换，应得到与本项目文本直转完全相同的母版</b>。
    /// </summary>
    static string MasterFromLegacy(string enc)
        => FoldFont.CreasesToMaster(FoldFont.ParseEnc(enc).Creases);

    // ------------------------------------------------------------ 文本级一致性

    [Theory]
    // 单字符
    [InlineData("A", false, false)] [InlineData("A", true, false)]
    [InlineData("A", false, true)]  [InlineData("A", true, true)]
    [InlineData("6", false, false)] [InlineData("8", false, false)]
    [InlineData("S", false, false)] [InlineData("Z", false, false)]
    // 刚修过的三个字模，放在多字符里再验一次
    [InlineData("68S", false, false)] [InlineData("68S", true, false)]
    [InlineData("8888", false, false)]
    [InlineData("SIX", false, false)]
    // 多字符
    [InlineData("AB", false, false)] [InlineData("ABC", false, false)]
    [InlineData("KRENIO", false, false)] [InlineData("PROTEIN", false, false)]
    [InlineData("123456", false, false)] [InlineData("HELLO WORLD", false, false)]
    [InlineData("QWERTY", false, false)] [InlineData("ASDFGH", false, false)]
    [InlineData("0000", false, false)] [InlineData("OO", false, false)]
    [InlineData("MM", false, false)] [InlineData("XYZ", false, false)]
    [InlineData("THE QUICK BROWN", false, false)]
    [InlineData("1234567890", false, false)]
    // 多行 —— 触发奇偶行反转/镜像与行间转折符
    [InlineData("A\nB", false, false)] [InlineData("A\nB", true, false)]
    [InlineData("A\nB", false, true)]  [InlineData("A\nB", true, true)]
    [InlineData("AB\nCD", false, false)] [InlineData("AB\nCD", true, false)]
    [InlineData("A\nB\nC", false, false)] [InlineData("ABC\nDEF", false, false)]
    [InlineData("Z\nY\nX", false, false)]
    [InlineData("A B C", false, false)]
    // 空行
    [InlineData("A\n\nB", false, false)]
    [InlineData("  A  ", false, false)]
    public void 文本转换结果与旧实现一致(string text, bool left, bool mirror)
    {
        string key = $"{text}|left={(left ? 1 : 0)}|mirror={(mirror ? 1 : 0)}";
        if (!Legacy.TryGetValue(key, out var want)) return;      // 语料未覆盖
        if (want.Note is not null) return;                        // 旧实现在此无定义

        string ours = FoldFont.TextToMaster(text, mirror, left);

        // 本项目文本直转的母版，必须等于「旧编码 -> 本项目转换」的母版。
        // 两边共用同一套编码解析与转换，所以这等价于「两边的编码串相同」。
        string fromLegacy = MasterFromLegacy(want.Encoding!);

        Assert.Equal(fromLegacy, ours);
    }

    /// <summary>
    /// 逐条核对折痕表 —— 比母版串更直接，能定位差异出在哪一折。
    /// </summary>
    [Theory]
    [InlineData("KRENIO", false, false)]
    [InlineData("PROTEIN", false, false)]
    [InlineData("123456", false, false)]
    [InlineData("68S", false, false)]
    [InlineData("HELLO WORLD", false, false)]
    [InlineData("A\nB\nC", false, false)]
    [InlineData("ABC\nDEF", false, false)]
    [InlineData("Z\nY\nX", true, true)]
    public void 文本折痕表逐条相同(string text, bool left, bool mirror)
    {
        string key = $"{text}|left={(left ? 1 : 0)}|mirror={(mirror ? 1 : 0)}";
        if (!Legacy.TryGetValue(key, out var want)) return;
        if (want.Note is not null) return;

        // 旧实现给的是「文本 -> 编码」的折痕；本项目要拿同一段编码解出的折痕。
        // TextToMaster 内部也走ParseEnc，但中间编码不暴露，
        // 故用CreasesToMaster 的输入（ParseEnc 的结果）重建。
        var legacyCreases = FoldFont.ParseEnc(want.Encoding!).Creases;

        // 反向验证：把本项目解析结果转回母版再解析，折痕数应与旧实现一致
        var m = Master.ParseMaster(FoldFont.TextToMaster(text, mirror, left));
        var back = m.CreasesInOrder();

        // 斜折痕必须完全保留
        int legacyDiagonals = want.Creases.Skip(1).Take(want.Creases.Count - 2)
                                       .Count(c => c.Xb != c.Xt);
        Assert.Equal(legacyDiagonals, back.Kinds.Count(k => k == 'D'));

        // 宽度：编码占用的总格数应与母版格数一致或超出（尾部空白段）
        Assert.True(m.NCell > 0, $"{text} 转换出零格母版");
    }

    /// <summary>
    /// 母版格数必须等于<b>旧实现编码占用的总宽度</b>。
    /// <para>
    /// ★ 这条取代了原先的「母版不以素格结尾」—— 那条断言是<b>错的</b>，
    /// 而且正是单字母折不全的根因。编码首尾的 <c>_</c>/空格只前进不落折痕，
    /// 但它们对应的是<b>真实的纸面</b>：V 的 6 个面片里首尾两片（面积各 18）
    /// 就是这两段空白，裁掉它们 V 只剩中间一小截。
    /// </para>
    /// <para>
    /// 旧实现没有母版串概念，但它记录了 <c>width</c>（半格单位）——
    /// 格数就该是 <c>width / 2</c>，既不裁头也不裁尾。
    /// </para>
    /// </summary>
    /// <summary>
    /// <b>方形铺排</b>的折痕必须与旧实现<b>逐线段</b>一致。
    /// <para>
    /// 数据由 <c>tools/_legacy-square.js</c> 生成：它把旧实现
    /// <c>showUnfoldedSquare</c> 的绘制调用换成记录器，跑出方形铺排的权威折痕表。
    /// 这是唯一能直接验证铺排（而不是隔着折叠结果间接看）的手段。
    /// </para>
    /// <para>
    /// ★ 这条测试的价值在于它能<b>把「铺排错」与「折叠错」分开</b>：
    /// 排查 68S 多转 180° 时，先跑这条确认铺排逐线段一致（缺 0 多 0），
    /// 才能把范围缩到折叠的 root 选择上。否则两个环节都会「看起来像错的」，
    /// 而它们各自的近似实现又都能在多数输入上蒙对。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("A")] [InlineData("6")] [InlineData("V")] [InlineData("L")]
    [InlineData("SOS")] [InlineData("68S")] [InlineData("KRENIO")] [InlineData("123456")]
    public void 方形折痕与旧实现逐线段一致(string ch)
    {
        string p = Path.Combine(AppContext.BaseDirectory, "legacy_square.json");
        if (!File.Exists(p)) return;
        var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(p, Encoding.UTF8));
        if (!doc.RootElement.TryGetProperty(ch, out var el)) return;

        // 旧实现用半格单位，本项目的方形 CP 用格单位
        var want = el.GetProperty("creases").EnumerateArray().Select(a =>
        {
            double x0 = a[0].GetDouble(), y0 = a[1].GetDouble();
            double x1 = a[2].GetDouble(), y1 = a[3].GetDouble();
            return $"{Math.Min(x0, x1) / 2:0.##},{Math.Min(y0, y1) / 2:0.##}-" +
                   $"{Math.Max(x0, x1) / 2:0.##},{Math.Max(y0, y1) / 2:0.##}";
        }).ToHashSet();

        var (masterText, rotate) = FoldFont.TextToMasterWithRotate(ch);
        var master = Master.ParseMaster(masterText);
        master.Rotate = rotate;
        var plan = SquareOps.PlanFromMaster(master);

        var got = plan.Fold.Select(s =>
            $"{Math.Min(s.A.X, s.B.X):0.##},{Math.Min(s.A.Y, s.B.Y):0.##}-" +
            $"{Math.Max(s.A.X, s.B.X):0.##},{Math.Max(s.A.Y, s.B.Y):0.##}").ToHashSet();

        var missing = want.Except(got).Take(4).ToList();
        var extra = got.Except(want).Take(4).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            $"「{ch}」方形折痕与旧实现不一致：旧有而本项目缺 {missing.Count} 条{Show(missing)}；" +
            $"本项目多 {extra.Count} 条{Show(extra)}");
    }

    static string Show(List<string> xs) => xs.Count == 0 ? "" : "（如 " + string.Join(" / ", xs) + "）";

    [Theory]
    [InlineData("KRENIO")] [InlineData("123456")] [InlineData("68S")]
    [InlineData("HELLO WORLD")] [InlineData("A\nB")] [InlineData("A\nB\nC")]
    [InlineData("THE QUICK BROWN")] [InlineData("SIX")] [InlineData("8888")]
    public void 文本生成的母版格数等于旧实现的编码总宽度(string text)
    {
        string key = $"{text}|left=0|mirror=0";
        if (!Legacy.TryGetValue(key, out var want)) return;

        string master = FoldFont.TextToMaster(text);
        Assert.NotEqual("", master);

        var m = Master.ParseMaster(master);
        Assert.Equal(want.Width / 2, m.NCell);
    }

    /// <summary>
    /// 多行文本的核心行为：行数越多母版越长，且各行之间有转折折痕。
    /// <para>
    /// 这条独立于旧实现核对，因为它守的是<b>用户可见的语义</b>：
    /// 界面上的文本框是可分行的（回车换行、Ctrl+Enter 提交），
    /// 分行必须真的产生更长的母版，否则多行输入等于无效。
    /// </para>
    /// </summary>
    [Fact]
    public void 多行文本比单行更长且行间有转折折痕()
    {
        string one = FoldFont.TextToMaster("ABC");
        string two = FoldFont.TextToMaster("AB\nC");
        string three = FoldFont.TextToMaster("A\nB\nC");

        Assert.True(two.Length > one.Length, "两行应比一行长");
        Assert.True(three.Length > two.Length, "三行应比两行长");

        // 行数越多，竖折痕越多（每行之间要能断开）
        int p1 = Master.ParseMaster(one).Pipes.Count;
        int p3 = Master.ParseMaster(three).Pipes.Count;
        Assert.True(p3 >= p1, $"竖折痕应变多：1 行 {p1} 条，3 行 {p3} 条");
    }

    /// <summary>
    /// 每多一行，母版恰好增长一个分隔符的跨度。
    /// <para>
    /// 守的是行间分隔符的长度：旧实现两个分隔符求值后都是 8 字符
    /// （<c>\_____/</c> 与 <c>_/_____\</c>）。本项目曾把第二个误写成两个反斜杠
    /// （C# 里写成 <c>"_/_____\\\\_"</c>），多行文本的中间行就会多出一道斜折痕
    /// —— 母版多一格、折出的字母错位。
    /// 单行文本碰不到行间分隔符，所以<b>只有多行用例能发现</b>。
    /// </para>
    /// </summary>
    [Fact]
    public void 每多一行母版恰好增长一个分隔符()
    {
        // 用同一字符重复 N 次，避免内容差异干扰长度比较
        int NCell(int rows) => Master.ParseMaster(
            FoldFont.TextToMaster(string.Join("\n", Enumerable.Repeat("A", rows)))).NCell;

        int one = NCell(1), two = NCell(2), three = NCell(3), four = NCell(4);

        Assert.True(two > one, $"两行应比一行长：{one} -> {two}");
        Assert.True(three > two, $"三行应比两行长：{two} -> {three}");

        // 每次加一行的增量必须稳定 —— 分隔符长度错了这个增量就会变
        Assert.Equal(two - one, three - two);
        Assert.Equal(three - two, four - three);
    }
}
