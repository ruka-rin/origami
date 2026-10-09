using System.Text;
using Origami.Core;
using Xunit;

namespace Origami.Tests;

/// <summary>
/// 与<b>旧参考实现</b>的算法一致性核对。
/// <para>
/// 旧实现是一份 CoffeeScript 风格的 JavaScript（<c>旧的参考代码/strip.js</c>），
/// 它提供了本项目折纸字库与编码解析的<b>原始出处</b>。把它抽出来逐条比对，
/// 能回答一个很难靠读代码回答的问题：<b>本项目的行为与当年那份是否有偏差</b>。
/// </para>
/// <para>
/// 数据文件 <c>legacy_font.json</c> 是用 Node 直接运行旧代码的
/// <c>fontEnc</c> 与 <c>parseEnc</c> 导出的（不含任何手工转录），
/// 所以比对结果不受「抄错字库」影响。
/// </para>
/// </summary>
public class LegacyParityTests
{
    /// <summary>旧实现导出的每个字模：编码串、宽度、折痕表。</summary>
    sealed record LegacyLetter(string Encoding, int Width, int Rotate,
                               List<(int Xb, int Xt)> Creases);

    /// <summary>取一个字段；缺失时直接失败并指明是哪个字模。</summary>
    static JsonLite.Value Field(JsonLite.Value obj, string key, string who)
    {
        if (obj.TryGetValue(key, out var v)) return v;
        throw new InvalidOperationException($"{who} 缺少字段 {key}");
    }

    static Dictionary<string, LegacyLetter> LoadLegacy()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "legacy_font.json");
        var root = JsonLite.Parse(File.ReadAllText(path, Encoding.UTF8));

        var map = new Dictionary<string, LegacyLetter>();
        foreach (var (key, val) in root)
        {
            string enc = Field(val, "encoding", key).Str;
            int width = (int)Field(val, "width", key).Num;
            int rotate = (int)Field(val, "rotate", key).Num;
            var creases = Field(val, "creases", key).Items!
                .Select(p => ((int)p.Items![0].Num, (int)p.Items![1].Num))
                .ToList();

            map[key] = new LegacyLetter(enc, width, rotate, creases);
        }
        return map;
    }

    static readonly Dictionary<string, LegacyLetter> Legacy = LoadLegacy();

    /// <summary>本项目支持的全部字符（字库键，含词首变体）。</summary>
    static string[] OurKeys => FoldFont.Encodings.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    static string[] LegacyKeys => Legacy.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    // ------------------------------------------------------------ 字库

    /// <summary>两边支持的字符集合必须一致 —— 少一个就折不出那个字。</summary>
    [Fact]
    public void 字库字符集合一致()
    {
        var ours = OurKeys;
        var theirs = LegacyKeys;

        var onlyOurs = ours.Except(theirs, StringComparer.Ordinal).ToArray();
        var onlyTheirs = theirs.Except(ours, StringComparer.Ordinal).ToArray();

        Assert.True(onlyOurs.Length == 0 && onlyTheirs.Length == 0,
            $"字符集不一致。仅本项目有：[{string.Join(",", onlyOurs)}]；"
          + $"仅旧实现有：[{string.Join(",", onlyTheirs)}]");
    }

    /// <summary>逐字符核对字库编码串 —— 抄错一个反斜杠就会让字母折错。</summary>
    [Fact]
    public void 字库编码串逐字符相同()
    {
        var diffs = new List<string>();

        foreach (var key in LegacyKeys)
        {
            if (!FoldFont.Encodings.TryGetValue(key, out var ours)) continue;
            if (ours != Legacy[key].Encoding)
                diffs.Add($"  {key}: 旧「{Legacy[key].Encoding}」 vs 新「{ours}」");
        }

        Assert.True(diffs.Count == 0,
            $"{diffs.Count} 个字模的编码串不一致：\n{string.Join("\n", diffs)}");
    }

    // ------------------------------------------------------------ 编码解析

    /// <summary>
    /// 逐字模核对 <c>parseEnc</c> 的输出：折痕序列、宽度、旋转标记全都要一致。
    /// <para>
    /// 这是最实质的一条 —— 它验证的是「同一个编码串在两边解出同一组折痕」，
    /// 而不只是「编码串一样」。若本项目的 <c>flipped</c> 状态处理或
    /// 哨兵位置有偏差，这里就会炸。
    /// </para>
    /// </summary>
    [Fact]
    public void 编码解析结果逐条相同()
    {
        var diffs = new List<string>();

        foreach (var key in LegacyKeys)
        {
            if (!FoldFont.Encodings.ContainsKey(key)) continue;
            var want = Legacy[key];

            var got = FoldFont.ParseEnc(want.Encoding, continuous: false);

            if (got.Rotate != want.Rotate)
                diffs.Add($"  {key}: rotate 旧{want.Rotate} vs 新{got.Rotate}");

            if (got.Width != want.Width)
                diffs.Add($"  {key}: width 旧{want.Width} vs 新{got.Width}");

            if (got.Creases.Count != want.Creases.Count)
            {
                diffs.Add($"  {key}: 折痕数 旧{want.Creases.Count} vs 新{got.Creases.Count}");
            }
            else
            {
                for (int i = 0; i < want.Creases.Count; i++)
                {
                    var (wb, wt) = want.Creases[i];
                    var (gb, gt) = got.Creases[i];
                    if (wb != gb || wt != gt)
                    {
                        diffs.Add($"  {key}[{i}]: 旧({wb},{wt}) vs 新({gb},{gt})");
                        break;      // 每字模只报第一处差异，避免刷屏
                    }
                }
            }
        }

        Assert.True(diffs.Count == 0,
            $"{diffs.Count} 处解析差异：\n{string.Join("\n", diffs.Take(20))}");
    }

    /// <summary>
    /// 用旧编码串走一遍本项目的「编码 -&gt; 母版」，结果必须是<b>合法母版</b>，
    /// 且<b>每一条折痕都被母版如实表达</b>。
    /// <para>
    /// 旧实现没有「编码直接转母版」的函数（它直接画 SVG），所以这里验证的是
    /// 可用性与无损性：同一串编码解出的折痕，转成母版再解析回来，
    /// 斜折痕数与竖折痕数都必须与原折痕对得上 ——
    /// 少一条就意味着某个折痕在转换中丢了。
    /// </para>
    /// <para>
    /// ★ 母版的<b>格数</b>不能与编码宽度直接相等：编码尾部的 <c>_</c> 前进两格
    /// 但不留折痕，那一段在母版里就是没有折痕的素格（这正是「末尾多素格」
    /// 那次修复的判据）。同理 <b>x=0 的竖折痕</b>也表达不了 ——
    /// 母版格式的 <c>pipes</c> 取值只能是 1..格数（0 格之前没有纸），
    /// 旧实现里 9 个字模（1/2/5/6/E/F/G/L 等）都带这样一个折痕，
    /// 转换时按规则丢弃，属于既定设计而非缺陷。
    /// </para>
    /// <para>
    /// 空格字模 <c>~___~</c> 也要排除 —— 它只有一个 <c>~</c>，
    /// <c>ParseEnc</c> 按「取第一对 <c>~</c> 之间」处理后是空串。
    /// </para>
    /// </summary>
    [Fact]
    public void 编码转母版保留全部折痕()
    {
        foreach (var key in LegacyKeys)
        {
            if (key == " ") continue;                // 见上方说明
            if (key.Length != 1) continue;           // 只查单字符，跳过词首变体
            if (!FoldFont.Encodings.ContainsKey(key)) continue;

            var creases = FoldFont.ParseEnc(Legacy[key].Encoding).Creases;

            // ★ 必须按<b>位置</b>排除哨兵，不能按值排除：折痕表的首元素恒为 (0,0)，
            //而部分字模（1/2/5/6/E/F/G/L）也含一条真正的 x=0 竖折痕，
            //两者值相同、含义不同 —— 按值过滤会把真折痕一并丢掉，
            //也可能把首哨兵算进去，得到负数。
            var body = creases.Skip(1).Take(creases.Count - 2);

            int diagonals = body.Count(c => c.Xb != c.Xt);
            // 纸带两端的竖折痕（x=0 与 x=width）母版表达不了，按规则丢弃：
            // 前者换个地方记（母版开头的 # 与补偿旋转），后者与末尾哨兵重合
            int verticals = body.Count(c => c.Xb == c.Xt && c.Xb != 0 && c.Xb != creases[^1].Xb);

            var m = Master.ParseMaster(FoldFont.CreasesToMaster(creases));
            var back = m.CreasesInOrder();

            Assert.Equal(diagonals, back.Kinds.Count(k => k == 'D'));
            Assert.Equal(verticals, back.Kinds.Count(k => k == 'V'));
            Assert.True(m.NCell > 0, $"{key} 转换出零格母版");
        }
    }
}
