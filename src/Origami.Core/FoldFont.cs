namespace Origami.Core;

/// <summary>折纸字库：把<b>普通文本</b>编成折痕母版串。
/// <para>
/// 这是一条「拉一长条纸、逐格折出字母」的折法 —— 每个字符对应一小段折痕，
/// 折好后就拼出该字符。生成的结果与手写的母版是同一种东西，所以可以
/// 直接喂给 <see cref="Master"/> / <see cref="Layout"/>，铺方、折叠、
/// 逆向全都照常работа。</para>
/// <para><b>编码符号</b>（见 <see cref="ParseEnc"/>）：
/// <list type="bullet">
/// <item><c>_</c> / 空格 —— 前进两格（不折）</item>
/// <item><c>!</c> —— 前进一格</item>
/// <item><c>/</c> <c>\</c> —— 斜折（占一格）</item>
/// <item><c>|</c> —— 竖折（落在两格边界上）</item>
/// <item><c>#</c> —— 翻转后续斜折的方向</item>
/// <item><c>~</c> —— 字模自身的起止标记（成对，取第一对之间）</item>
/// <item><c>@</c> —— 旋转标记，紧跟 <c>~</c> 之后</item>
/// </list></para>
/// </summary>
public static class FoldFont
{
    /// <summary>字模表：字符 -&gt; 编码串。
    /// <para>这是<b>数据</b>不是逻辑，改任何一个字符都会让折出来的字母变形。
    /// <c>Xstart</c> 是「词首」变体，用于每行第一个字符（它能折进纸条的起始端，
    /// 不需要额外的对齐余量）。</para>
    /// </summary>
    public static readonly Dictionary<string, string> Encodings = new()
    {
        ["A"] = "_~/___/_\\_/__|___|/_\\~_",
        ["B"] = "_~/___/_\\_/__|__/_\\__#@@~#|____",
        ["C"] = "_/___/_\\_|~@@@_/_\\___/_\\_@@@~|_\\_",
        ["D"] = "_~/____|/_/___\\__#@@~#|____",
        ["E"] = "_/___/__~|@@__\\_/__|__/_\\__~/|/#",
        ["F"] = "_/___/__~|@@__\\_/__|__/_\\~\\|\\#__",
        ["G"] = "_/___/__~|@@__\\___/_\\_|#@~#_\\_",
        ["H"] = "_~/____|__/_\\__|____\\~_",
        ["I"] = "_~/____#@@@~#|____/#_",
        ["J"] = "_/_|~@_/_\\____@@@~|____\\_",
        ["K"] = "_~/____|__/\\\\\\_|_//_\\\\\\~_",
        ["L"] = " /|~@|    |    /  #~#/|/",
        ["M"] = "_~/___/\\_\\/_/\\___\\~_",
        ["N"] = "_~/____|_///\\__|____\\~_",
        ["O"] = "_/~@___/_\\___/_\\#@@@~#|/_\\|\\_",
        ["Ostart"] = "~@\\_/___\\_/___~/#_",
        ["P"] = "_~/___/_\\_/_/#@~#_\\___",
        ["Q"] = "/|~@/_\\___/_\\___/_//#~#\\\\#",
        ["R"] = "_~/___/_\\_/_|////#~_",
        ["S"] = "_\\|~@@@\\_\\_/_/_\\_/@~|\\_/_\\_\\_\\_",
        ["T"] = "__~/___\\_|___#~#|_/___\\__",
        ["Tstart"] = "__/~@@@___\\_|___|_/___/#~__",
        ["U"] = "_/____|~@____/_\\____@@@~|____\\_",
        ["V"] = "_/____|~@____//|//____#@@@~#|____/#_",
        ["W"] = "_/____|~@____/\\_\\/_/\\____@@@~|____\\_",
        ["X"] = "_~///_\\\\_|_///\\\\\\_|_//_\\\\\\~_",
        ["Y"] = "__~/_\\\\__|__//|//__#@@@~#|__\\\\_/#__",
        ["Z"] = "\\|\\\\ \\\\\\\\/  |~   |\\\\\\\\\\  |/  ~\\|\\#",
        ["1"] = "_\\|\\|~@@|_|\\____|\\\\@~|/\\___\\\\|\\#",
        ["2"] = "_/_/_/_\\__~@@|__/_\\_\\_/__#~#/|/",
        ["3"] = "_\\|\\#~#__\\_/__|__/_\\__@@~|__/___/#_",
        ["4"] = "___~/_\\_\\__|__/_\\__|____\\~_",
        ["5"] = "_\\|\\|~@@|__|\\_\\_\\__|/__#~#|__/_\\_\\_\\_",
        ["6"] = "_/|~@|____|__/_/_\\_/|\\_~/|/#_",
        ["7"] = "___~/___\\__|#~#__/___/#_",
        ["8"] = "_/|~@/|\\_/_/_\\_/_\\_\\_/_@@~|_\\|\\_",
        ["9"] = "___~/_\\_\\_/_\\__#@~#_\\_",
        ["0"] = "_/#~@@@#___/_\\___/_\\#@@@~#|/_\\|\\_",
        [" "] = "~___~",
    };

    /// <summary>字库收录的可输入字符（升序）。</summary>
    public static string Chars
        => string.Concat(Encodings.Keys
            .Where(k => !k.EndsWith("start", StringComparison.Ordinal))
            .Where(k => k.Length == 1)
            .OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>一段编码解析出的折痕。</summary>
    public sealed record EncodedLetter(List<(int Xb, int Xt)> Creases, int Width, int Rotate);

    /// <summary>
    /// 解析一段编码，返回折痕序列。
    /// <para>关键在 <c>flipped</c> 状态：<c>/</c> 与 <c>\</c> 的实际方向随它翻转。
    /// 折纸里同一个「向左斜」在纸翻面后会变成「向右斜」，字模正是用 <c>#</c>
    /// 标记这条边界 —— 少了这个状态，所有带翻面的字母都会折错方向。</para>
    /// </summary>
    public static EncodedLetter ParseEnc(string s, bool continuous = false)
    {
        if (continuous) s = s.Replace("~", "");
        else if (s.IndexOf('~') is int i1 && i1 >= 0)
        {
            int i2 = s.LastIndexOf('~');
            s = i1 == i2 ? s[(i1 + 1)..] : s[(i1 + 1)..i2].Replace("~", "");
        }

        int rotate = 0;
        while (s.Length > 0 && s[0] == '@') { rotate++; s = s[1..]; }

        s = s.Replace("@", "").Replace("||", "");

        int x = 0;
        var creases = new List<(int, int)> { (0, 0) };
        bool flipped = false;

        foreach (char c in s)
        {
            switch (c)
            {
                case ' ':
                case '_':
                    x += 2; break;
                case '!':
                    x += 1; break;
                case '|':
                    creases.Add((x, x)); break;
                case '/':
                case '\\':
                    // '/' 未翻面 或 '\' 已翻面 -> (x, x+2)；否则 (x+2, x)
                    if ((c == '\\') == flipped) creases.Add((x, x + 2));
                    else creases.Add((x + 2, x));
                    x += 2;
                    break;
                case '#':
                    flipped = !flipped; break;
            }
        }

        creases.Add((x, x));
        return new EncodedLetter(creases, x, rotate);
    }

    /// <summary>一段文本 -&gt; 折痕母版串。</summary>
    /// <param name="text">普通文本，换行分行；字库里没有的字符会被跳过。</param>
    /// <param name="mirror">
    /// 奇数行是否<b>镜像</b>；否则改为<b>反转</b>字符顺序。两者都能让多行沿长条
    /// 交替出现（折出蛇形），视觉结果不同。
    /// </param>
    /// <param name="left">
    /// 是否让第一行从纸条<b>最左端</b>起折（左侧补一个竖折痕）。
    /// 否则每行首字符用「词首」变体，起始折痕由字模自带。
    /// </param>
    public static string TextToMaster(string text, bool mirror = false, bool left = false)
        => TextToMasterWithRotate(text, mirror, left).Master;

    /// <summary>
    /// 文本 -&gt; 母版，<b>并给出成品该转多少个 90°</b>。
    /// <para>
    /// <paramref name="Rotate"/> 即字模编码里的 <c>@</c> 计数，由第一个字母决定
    /// ——旧实现用它「一次到位」地摆正成品，不靠 bbox 猜朝向。
    /// </para>
    /// </summary>
    public static (string Master, int Rotate) TextToMasterWithRotate(
        string text, bool mirror = false, bool left = false)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0) return ("", 0);

        int parity = left ? 1 : 0;
        var sb = new System.Text.StringBuilder();

        for (int row = 0; row < lines.Count; row++)
        {
            var line = new System.Text.StringBuilder();
            var chars = lines[row].ToCharArray();

            for (int col = 0; col < chars.Length; col++)
            {
                string key = char.ToUpperInvariant(chars[col]).ToString();
                if (!Encodings.ContainsKey(key)) continue;      // 不支持的字符跳过

                // 每行第一个字符用「词首」变体（若字库提供）
                if (col == 0 && !left && Encodings.ContainsKey(key + "start"))
                    key += "start";

                line.Append(StripLead(Encodings[key]));
            }

            string lineEnc = line.ToString();

            if (row % 2 == 1 - parity)
            {
                // 镜像行前后各加 #（翻面标记），且**前面还要加一个 _**（对齐余量）——
                // 这两个符号缺一不可：少了 _ 会让镜像行比反转行短一格，
                // 多行文本折出来的字母会错位（旧实现是 `#_${lineEnc}#`）。
                lineEnc = mirror ? $"#_{lineEnc}#" : Reverse(lineEnc);
            }

            // 行间转折符：斜杠串的<b>末尾只有一个</b>反斜杠。
            // 旧实现写的是 JS 的 '_/_____\\_'，求值后是「_/」加 5 下划线加 1 反斜杠
            // （共 8 字符）—— 这里曾误写成两个反斜杠，多行文本的中间行就会
            // 多出一道斜折痕，母版多一格、折出的字母错位。
            if (row < lines.Count - 1)
                lineEnc += row % 2 == parity ? "\\_____/" : "_/_____\\_";

            sb.Append(lineEnc);
        }

        string enc = sb.ToString();

        if (left)
        {
            int idx = enc.IndexOf('~');
            while (idx + 1 < enc.Length && enc[idx + 1] == '@') idx++;
            if (idx >= 0)
                enc = enc[..(idx + 1)] + (mirror ? "" : "#") + "|" + enc[(idx + 1)..];
        }

        var parsed = ParseEnc(enc, false);
        return (CreasesToMaster(parsed.Creases), parsed.Rotate);
    }

    /// <summary>字模编码开头的 <c>_</c> / 空格是「对齐余量」，不计入折叠。</summary>
    static string StripLead(string enc)
        => enc.Length > 0 && (enc[0] == '_' || enc[0] == ' ') ? enc[1..] : enc;

    static string Reverse(string s)
    {
        var chars = s.ToCharArray();
        Array.Reverse(chars);
        return new string(chars);
    }

    /// <summary>折痕序列 -&gt; 母版串（<see cref="Layout.MasterToCreases"/> 的逆运算）。</summary>
    /// <remarks>
    /// 竖折痕是<b>插进去</b>而不是替换格子：母版串里 <c>|</c> 不占格位，
    /// <c>p=k</c> 表示「第 k 格<b>之前</b>有一条竖折痕」，而第 k-1 格的斜折痕
    /// 落在 x=2(k-1) 上、竖折痕落在 x=2k 上，两者并不冲突。
    /// 同理 x=0 的竖折痕要丢掉 —— 母版格式里 <c>pipes</c> 取值只能是 1..格数。
    /// </remarks>
    public static string CreasesToMaster(IReadOnlyList<(int Xb, int Xt)> creases)
    {
        if (creases.Count < 2) return "";

        // ★ 格数就是<b>编码占用的总宽度</b>，<b>不</b>裁前导也<b>不</b>裁尾部。
        //
        // 旧实现的面片是「相邻两条折痕之间的梯形」，宽度可以大于 1：
        // V 的 creases（半格单位）是
        //   (0,0) (8,10) (10,12) (12,12) (12,14) (14,16) (24,24)
        // 对应 6 个面片，宽度（格）依次是 4,1,1,0,1,5 —— 首尾两块
        // 没有折痕的空白<b>各是一个整片</b>，正是字母的上下两端。
        //
        // 母版串是均匀格模型：连续的无折痕格会被 ChainFromMaster 的剖分
        // 合并成同一个面片，所以保留全部 12 格才能复现旧实现的 6 片结构。
        // 曾经用「最后一条折痕」定格数、又裁掉前导空白，等于把首尾两块
        // 空白片扔了 —— V 只剩下中间的 4 格，折出来自然只有半个字母。
        int bySentinel = creases[^1].Xb / 2;
        int ncell = bySentinel;
        if (ncell <= 0) return "";

        var cells = new char[ncell];
        Array.Fill(cells, '.');
        var pipes = new HashSet<int>();

        // 跳过首尾哨兵
        for (int i = 1; i < creases.Count - 1; i++)
        {
            var (xb, xt) = creases[i];

            if (xb == xt)
            {
                // ★ p=0 必须保留。x=0 处的竖折痕是纸带起点端的一道<b>真实的折</b>：
                // 它切出的面片宽度为零（L/68S 等字模的 creases 里就有一条 (0,0)），
                // 平面剖分产生不出零宽面片，但那条反射线会镜像其后<b>所有</b>面片
                // —— 等价于整个成品沿 x=0 左右翻转。
                // 曾经按「pipes 只能取 1..格数」把它丢弃，于是 L / 68S 少了这次
                // 镜像，与旧实现的重合度只有 0.62（V/S/SOS 这类没有 x=0 折痕的
                // 字模则是 0.998）。折叠层会据「pipes 含 0」补上这次镜像。
                if (xt % 2 == 0) pipes.Add(xt / 2);
                continue;
            }

            int idx = Math.Min(xb, xt) / 2;
            if (idx >= 0 && idx < ncell)
                cells[idx] = xt > xb ? '/' : '\\';
        }

        var sb = new System.Text.StringBuilder(ncell + pipes.Count);
        for (int i = 0; i <= ncell; i++)
        {
            if (pipes.Contains(i)) sb.Append('|');
            if (i < ncell) sb.Append(cells[i]);
        }
        return sb.ToString();
    }
}