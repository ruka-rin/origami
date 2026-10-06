using Origami.Core;

namespace Origami.Desktop;

/// <summary>导入文件的种类。</summary>
public enum ImportKind
{
    /// <summary>母版字符串（纯文本）。</summary>
    MasterText,
    /// <summary>长条折痕图 SVG。</summary>
    StripSvg,
    /// <summary>方形折痕图 SVG。</summary>
    SquareSvg,
    /// <summary>成品 / 原子片 SVG —— 折痕信息已丢失，无法还原成母版。</summary>
    Unsupported,
}

/// <summary>一次导入的结果。</summary>
/// <param name="MasterText">还原出的母版字符串。</param>
/// <param name="PreferredView">导入后优先显示的视图（导入了什么就先看什么）。</param>
/// <param name="Color">从文件里读到的上色状态 —— 黑白 SVG 导入后开关应自动关掉。</param>
/// <param name="Warning">歧义等需要提醒用户的情况；无则 null。</param>
public sealed record ImportResult(
    string MasterText, ViewKind PreferredView, ColorMode Color, string? Warning);

/// <summary>导入：把外部文件还原成母版。</summary>
public static class Importer
{
    /// <summary>
    /// 从文件文本还原母版。
    /// <para>
    /// ★ 支持三类输入：<b>母版纯文本</b>、<b>长条折痕图 SVG</b>、<b>方形折痕图 SVG</b>，
    /// 各自彩色 / 黑白都支持。
    /// </para>
    /// <para>
    /// ★ <b>成品 / 原子片 SVG 导不进来</b>：那两类图只画折叠后的面片，折痕的
    /// 「层序 + 相邻关系」在生成时就没有写进文件，无法反推回母版。明确报错比
    /// 悄悄给个错结果好。
    /// </para>
    /// </summary>
    public static ImportResult Load(string text, string fileName)
    {
        text = text.Trim();
        if (text.Length == 0) throw new FormatException("文件是空的");

        if (!LooksLikeSvg(text))
            return new ImportResult(text, ViewKind.Strip, ColorMode.Peaks, null);

        // ---- SVG ----
        ColorMode color = DetectColor(text);

        var model = SvgIO.SvgToModel(text, strict: false);

        // ★ 先按 kind 分派，再检查折痕数。反过来会让「成品 / 原子片 SVG」报出
        //   「没识别到折痕」这种驴唇不对马嘴的话，而真实原因是「这类图压根不该导入」。
        if (model.Kind is "folded" or "pieces")
            throw new FormatException(
                $"「{fileName}」是{(model.Kind == "folded" ? "折叠成品" : "原子片分解")}图，" +
                "这类图只画折叠后的面片，没有折痕的结构信息（层序与相邻关系），" +
                "无法还原成母版。请导入长条或方形折痕图。");

        if (model.Folds.Count == 0)
            throw new FormatException(
                $"「{fileName}」里没有识别到折痕。" +
                "只支持本工具导出的折痕图（带 class/data-mv 标注），" +
                "或任何带 class 属性的折痕图 SVG。");

        switch (model.Kind)
        {
            case "strip":
            {
                var master = Model.StripModelToMaster(model);
                return new ImportResult(master.Text.Trim(), ViewKind.Strip, color, null);
            }

            case "square":
            {
                var plan = Model.SquarePlanFromModel(model);
                var r = Reverse.SquareToMasterAmbiguous(plan, strict: true);

                string? warn = r.Candidates > 1
                    ? $"该方形图对应 **{r.Candidates} 个等价母版**" +
                      $"（格数区间 {r.NCellLo}–{r.NCellHi}）——母版末尾的「素格 `.` + 尾部 pipe」" +
                      "在方形 CP 里不可见，所以无法唯一确定。" +
                      "已取第一个能正向复现原方形的候选，可放心继续操作。"
                    : null;

                return new ImportResult(r.Master.Text.Trim(), ViewKind.Square, color, warn);
            }

            default:
                throw new FormatException(
                    $"「{fileName}」的 data-kind={model.Kind}，不是折痕图（应为 strip 或 square）。");
        }
    }

    /// <summary>看内容判断是不是 SVG（而不是靠扩展名 —— 扩展名可以撒谎）。</summary>
    static bool LooksLikeSvg(string text)
        => text.Contains("<svg", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 判断这份 SVG 是上色还是黑白。
    /// <para>
    /// ★ 只看<b>描边色</b>，不能看 <c>data-mv</c>：本工具生成的<b>黑白</b> SVG 依然带
    /// <c>data-mv="M"/"V"</c>（峰谷是图的数据，去了上色也不能丢），拿它判断会把
    /// 黑白图误判成彩色。
    /// </para>
    /// </summary>
    static ColorMode DetectColor(string svg)
    {
        string s = svg.ToLowerInvariant();
        bool colored = s.Contains("#f00")      // 红 = 峰
                    || s.Contains("#00f")      // 蓝 = 谷
                    || s.Contains("#0f0")      // 绿 = 剪切线
                    || s.Contains("\"red\"")
                    || s.Contains("\"blue\"")
                    || s.Contains("\"green\"");
        return colored ? ColorMode.Peaks : ColorMode.Plain;
    }
}