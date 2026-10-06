using System.Text;
using Origami.Core;

namespace Origami.Cli;

/// <summary>
/// 统一命令行入口。
/// <para><b>子命令</b>：
/// <c>master2svg</c>、<c>master2square</c>、<c>svg2json</c>、<c>json2svg</c>、<c>svg2png</c>、
/// <c>json2folded</c>、<c>reverse</c>、<c>crosscheck</c>、<c>build</c>、<c>verify</c>。</para>
/// <para>母版 -&gt; 方形 CP 是<b>完整可推导</b>的：不需要手绘基准参与生成，
/// 基准只用于 <c>crosscheck</c> 做回归验证。</para>
/// <para>示例：<c>origami build -m "/....|../.." -o out/</c></para>
/// </summary>
public static class Cli
{
    public static int Main(string[] argv)
    {
        try
        {
            return Run(argv);
        }
        catch (UserError e)
        {
            Console.Error.WriteLine($"错误: {e.Message}");
            return 2;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"异常: {e.GetType().Name}: {e.Message}");
            if (Environment.GetEnvironmentVariable("ORIGAMI_TRACE") == "1")
                Console.Error.WriteLine(e.StackTrace);
            return 3;
        }
    }

    sealed class UserError : Exception
    {
        public UserError(string m) : base(m) { }
    }

    // ==================================================================
    //  文件 I/O —— 全部集中在这里。库（Origami.Core）只做纯计算：
    //  输入是文本 / 数据结构，输出是文本 / 位图，不碰文件系统。
    // ==================================================================

    static string ReadText(string path)
    {
        try { return File.ReadAllText(path); }
        catch (Exception e) { throw new UserError($"读取失败 {path}: {e.Message}"); }
    }

    /// <summary>解析 --unit 类选项。非法数字是用户错误，应报 UserError（退出码 2）而非异常。</summary>
    static double ParseUnit(string opt, string raw)
        => double.TryParse(raw, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out double v) && v > 0
            ? v
            : throw new UserError($"{opt} 需要一个正数，收到「{raw}」");

    static void WriteText(string path, string content)
    {
        EnsureDir(path);
        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    static void WritePng(string path, Raster img)
    {
        EnsureDir(path);
        // Raster 持有ImageSharp 的池化内存，必须释放，否则批量导出时进程内存只涨不落
        using (img) File.WriteAllBytes(path, img.ToPngBytes());
    }

    static void EnsureDir(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    static int Run(string[] argv)
    {
        if (argv.Length == 0 || argv[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return argv.Length == 0 ? 2 : 0;
        }

        // 全局选项
        double unit = 64.0, foldUnit = 48.0;
        var rest = new List<string>();
        for (int i = 0; i < argv.Length; i++)
        {
            switch (argv[i])
            {
                case "--unit" when i + 1 < argv.Length: unit = ParseUnit("--unit", argv[++i]); break;
                case "--fold-unit" when i + 1 < argv.Length: foldUnit = ParseUnit("--fold-unit", argv[++i]); break;
                default: rest.Add(argv[i]); break;
            }
        }
        if (rest.Count == 0) { PrintHelp(); return 2; }

        string cmd = rest[0];
        var args = Args.Parse(rest.Skip(1).ToArray());
        // --fold-unit 有自己的默认值 48，**不跟随** --unit：折痕图是按格对齐的规整网格
        // （适合大 unit 便于打印），折叠成品是斜边为主的剪影（大 unit 只是徒增文件体积）

        return cmd switch
        {
            "master2svg" => CmdMaster2Svg(args, unit),
            "master2square" => CmdMaster2Square(args, unit),
            "svg2json" => CmdSvg2Json(args),
            "json2svg" => CmdJson2Svg(args),
            "svg2png" => CmdSvg2Png(args),
            "json2folded" => CmdJson2Folded(args, foldUnit),
            "reverse" => CmdReverse(args),
            "crosscheck" => CmdCrosscheck(args),
            "text" => CmdText(args),
            "export" => CmdExport(args, unit, foldUnit),
            "build" => CmdBuild(args, unit, foldUnit),
            "verify" => CmdVerify(args, unit),
            _ => Unknown(cmd),
        };
    }

    static int Unknown(string cmd)
    {
        Console.Error.WriteLine($"未知子命令: {cmd}");
        PrintHelp();
        return 2;
    }

    static void PrintHelp()
    {
        Console.WriteLine("""
        origami —— 折纸谜题折痕母版工具链

        用法: origami [--unit PX] [--fold-unit PX] <子命令> [选项]

        全局选项:
          --unit PX        每格像素数（折痕图；默认 64）
          --fold-unit PX   折叠成品的每格像素数（默认同 --unit）

        子命令:
          master2svg    长条母版 -> 长条折痕图 SVG（红蓝）
          master2square 长条母版 -> 方形折痕图 SVG（红蓝）
          svg2json      折痕图 SVG -> 数据模型 JSON
          json2svg      数据模型 JSON -> 折痕图 SVG
          svg2png       折痕图 SVG -> 纯黑 PNG
          json2folded   长条 JSON -> 折叠成品 SVG + PNG
          reverse       方形折痕图 -> 长条母版字符串（精确逆向）
          crosscheck    母版推导的方形 CP vs 权威基准逐项比对
          text          普通文本 -> 折痕母版串（折纸字库）
          export        导出单个视图（SVG/PNG、彩色/黑白、PNG 分辨率）
          build         一条龙：母版 -> 全部产物
          verify        往返一致性自检

        母版输入（对 master2svg/master2square/crosscheck/build/verify 有效）:
          -m, --master <字符串>       直接给母版字符串
          -f, --master-file <路径>    从文件读母版

        导出选项（仅 export）:
          --view <名称>            长条|长条成品|方形|方形成品|长条原子片|方形原子片
          --svg                    输出 SVG（矢量，默认）
          --png                    输出 PNG（位图）
          --plain                  黑白/剪影（默认彩色）
          --png-scale <倍率>       PNG 分辨率倍率，默认 2

        示例:
          origami build -m "/....|../.." -o out/
          origami svg2json out/长条折痕图.svg -o out/长条.json
        """);
    }

    // ==================================================================
    //  母版读入
    // ==================================================================

    static Master ReadMaster(Args a)
    {
        if (a.Has("m") || a.Has("master"))
            return Master.ParseMaster(a.Get("m") ?? a.Get("master")!);
        if (a.Has("f") || a.Has("master-file"))
        {
            string p = a.Get("f") ?? a.Get("master-file")!;
            // 走 ReadText：路径写错是最高频的用户错误，应当报成 UserError（退出码 2）
            return Master.ParseMaster(ReadText(p).Trim());
        }
        throw new UserError("需要 -m 母版字符串 或 --master-file 路径");
    }

    // ==================================================================
    //  子命令实现
    // ==================================================================

    static int CmdMaster2Svg(Args a, double unit)
    {
        var master = ReadMaster(a);
        var strip = Model.StripModel(master);
        string outDir = a.Get("o", "out");
        Directory.CreateDirectory(outDir);
        string name = a.Get("name-strip", "长条折痕图");

        string svgPath = Path.Combine(outDir, name + ".svg");
        WriteText(svgPath, SvgIO.ModelToSvg(strip, unit));
        WriteText(Path.Combine(outDir, name + ".json"), SvgIO.ModelToJson(strip));
        WritePng(Path.Combine(outDir, name + ".png"), PngIO.RenderCreaseBlack(strip));
        WritePng(Path.Combine(outDir, name + "_彩色.png"), PngIO.RenderCreaseColor(strip));

        Console.WriteLine($"长条 {SvgIO.Describe(strip)}");
        Console.WriteLine($"  -> {svgPath}");
        return 0;
    }

    static int CmdMaster2Square(Args a, double unit)
    {
        var master = ReadMaster(a);
        var plan = SquareOps.PlanFromMaster(master);
        var sq = Model.SquareModel(plan);
        string outDir = a.Get("o", "out");
        Directory.CreateDirectory(outDir);
        string name = a.Get("name-square", "方形折痕图");

        string svgPath = Path.Combine(outDir, name + ".svg");
        WriteText(svgPath, SvgIO.ModelToSvg(sq, unit));
        WriteText(Path.Combine(outDir, name + ".json"), SvgIO.ModelToJson(sq));
        WritePng(Path.Combine(outDir, name + ".png"), PngIO.RenderCreaseBlack(sq));
        WritePng(Path.Combine(outDir, name + "_彩色.png"), PngIO.RenderCreaseColor(sq));

        Console.WriteLine($"方形 {SvgIO.Describe(sq)}");
        Console.WriteLine($"  -> {svgPath}");
        return 0;
    }

    static int CmdSvg2Json(Args a)
    {
        string svg = a.Positional(0) ?? throw new UserError("需要 SVG 路径");
        var m = SvgIO.SvgToModel(ReadText(svg), strict: !a.Flag("lenient"));
        string jpath = a.Get("o") ?? Path.ChangeExtension(svg, ".json");
        WriteText(jpath, SvgIO.ModelToJson(m));
        Console.WriteLine(SvgIO.Describe(m));
        Console.WriteLine($"  -> {jpath}");
        return 0;
    }

    static int CmdJson2Svg(Args a)
    {
        string json = a.Positional(0) ?? throw new UserError("需要 JSON 路径");
        var m = SvgIO.JsonToModel(ReadText(json));
        string spath = a.Get("o") ?? Path.ChangeExtension(json, ".svg");
        WriteText(spath, SvgIO.ModelToSvg(m));
        Console.WriteLine(SvgIO.Describe(m));
        Console.WriteLine($"  -> {spath}");
        return 0;
    }

    static int CmdSvg2Png(Args a)
    {
        string svg = a.Positional(0) ?? throw new UserError("需要 SVG 路径");
        var m = SvgIO.SvgToModel(ReadText(svg), strict: false);
        string p = a.Get("o") ?? Path.ChangeExtension(svg, ".png");
        WritePng(p, PngIO.RenderCreaseBlack(m));
        Console.WriteLine($"  -> {p}");
        return 0;
    }

    static int CmdJson2Folded(Args a, double foldUnit)
    {
        string json = a.Positional(0) ?? throw new UserError("需要 JSON 路径");
        var m = SvgIO.JsonToModel(ReadText(json));
        if (m.Kind != "strip")
            throw new UserError(
                $"折叠成品目前只支持长条模型（kind=strip），该模型 kind={m.Kind}");

        var master = Model.StripModelToMaster(m);
        var (faces, creases, _) = MasterOps.ChainFromMaster(master);
        var mv = m.Folds.Select(f => f.MvType).ToList();
        if (mv.Count != creases.Count)
            throw new UserError($"峰谷数 {mv.Count} != 折痕数 {creases.Count}");

        var (world0, _) = FoldOps.RollFold(faces, creases);
        var rank0 = FoldOps.LayerRanks(faces.Count, creases, mv);
        var bad = FoldOps.VerifyLayers(creases, rank0);

        var fm = new FoldedModel(world0, rank0, mv, faces, creases).OrientLandscape();

        string outDir = a.Get("o", "out");
        Directory.CreateDirectory(outDir);
        string stem = Path.Combine(outDir, a.Get("name-folded", "折叠成品"));

        WriteText(stem + ".svg", SvgIO.FoldedToSvg(fm.World, fm.Rank, null, mv, unit: foldUnit, color: true, flipY: true));
        WritePng(stem + ".png",
                 PngIO.RenderFolded(fm.World, unit: foldUnit, rank: fm.Rank, mvSeq: mv));
        WritePng(stem + "_原子片.png", PngIO.RenderPieces(fm.World));

        var (x0, y0, x1, y1) = fm.Bbox();
        Console.WriteLine($"折叠: {fm.World.Count}片  {fm.NLayers}层  成品 {x1 - x0:0}x{y1 - y0:0} 格");
        Console.WriteLine($"  面积守恒 {faces.Sum(Geom.PolyArea):F4} -> {fm.World.Sum(Geom.PolyArea):F4}");
        Console.WriteLine($"  折痕贴合违规 {bad.Count}/{creases.Count}");
        Console.WriteLine($"  -> {stem}.svg / {stem}.png");
        return 0;
    }

    static int CmdReverse(Args a)
    {
        string svg = a.Positional(0) ?? throw new UserError("需要方形折痕图 SVG 路径");
        var m = SvgIO.SvgToModel(ReadText(svg), strict: false);
        if (m.Kind != "square")
            throw new UserError($"需要方形折痕图（kind=square），该图 kind={m.Kind}");

        var plan = Model.SquarePlanFromModel(m);
        var dec = Reverse.Decompose(plan);
        Console.WriteLine($"方形 CP 分解: N={plan.N} 转折 {dec.NTurn} 次 " +
                          $"主折痕 {dec.NMain} 条 转折耳线 {dec.Ear.Count} 条");
        Console.WriteLine($"  转折点(yOffset): [{string.Join(", ", dec.Turns)}]");

        var rep = Reverse.VerifyRoundtrip(plan);
        var back = rep.Master;
        Console.WriteLine($"逆向母版: {back.NCell} 格  竖折痕 {back.Pipes.Count} 条");
        Console.WriteLine($"  闭环自检(还原后重新铺排是否复现原方形): {(rep.Ok ? "通过" : "未通过")}");
        if (!rep.Ok)
        {
            Console.WriteLine($"    仅还原多出: {FmtSegs(rep.OnlyGen)}");
            Console.WriteLine($"    仅原图有: {FmtSegs(rep.OnlyInput)}");
        }
        Console.WriteLine($"  {back.Text.Trim()}");

        if (a.Has("o"))
        {
            string op = a.Get("o")!;
            WriteText(op, back.Text.Trim() + "\n");
            Console.WriteLine($"  -> {op}");
        }

        if (a.Has("f"))
        {
            string refPath = a.Get("f")!;
            var reference = Master.ParseMaster(ReadText(refPath).Trim());
            var d = Reverse.DiffToReference(back, reference);
            Console.WriteLine();
            Console.WriteLine($"与权威母版比对（{refPath}）:");
            Console.WriteLine($"  格数    : 逆向 {d.NCellBack}  权威 {d.NCellRef}  " +
                              $"{(d.NCellMatch ? "一致" : "不一致")}");
            Console.WriteLine($"  对角符号: 差异 {d.SymbolDiff.Count} 处 " +
                              $"[{string.Join(", ", d.SymbolDiff.Take(10))}]");
            Console.WriteLine($"  竖折痕| : 多出 [{string.Join(", ", d.ExtraPipes)}]  " +
                              $"缺少 [{string.Join(", ", d.MissingPipes)}]");
            Console.WriteLine($"  字符串逐字相同: {d.TextExact}");
            Console.WriteLine($"  => {(d.Exact ? "完全一致（逆向精确可逆）" : "存在差异")}");
        }
        return rep.Ok ? 0 : 1;
    }

    static string FmtSegs(List<Segment> segs)
        => string.Join(", ", segs.Take(6).Select(s => s.ToString()));

    static int CmdCrosscheck(Args a)
    {
        var master = ReadMaster(a);
        var plan = SquareOps.PlanFromMaster(master);
        string path = a.Get("t") ?? throw new UserError("需要 -t 权威方形折痕图 SVG 路径");
        if (!File.Exists(path)) throw new UserError($"找不到基准 SVG: {path}");
        var res = Truth.Crosscheck(plan, ReadText(path));
        Console.WriteLine(Truth.FormatReport("方形CP", res));
        return res.Ok ? 0 : 1;
    }

    static int CmdBuild(Args a, double unit, double foldUnit)
    {
        var master = ReadMaster(a);
        string outDir = a.Get("o", "out");
        Directory.CreateDirectory(outDir);

        Console.WriteLine(new string('=', 66));
        Console.WriteLine($"母版: {master.NCell} 格  竖折痕 {master.Pipes.Count} 条");
        Console.WriteLine(new string('=', 66));

        // 长条
        var strip = Model.StripModel(master);
        WriteText(Path.Combine(outDir, "长条折痕图.svg"), SvgIO.ModelToSvg(strip, unit));
        WriteText(Path.Combine(outDir, "长条折痕图.json"), SvgIO.ModelToJson(strip));
        WritePng(Path.Combine(outDir, "长条折痕图.png"), PngIO.RenderCreaseBlack(strip));
        WritePng(Path.Combine(outDir, "长条折痕图_彩色.png"), PngIO.RenderCreaseColor(strip));
        Console.WriteLine($"长条 {SvgIO.Describe(strip)}");

        // 方形
        var plan = SquareOps.PlanFromMaster(master);
        var sq = Model.SquareModel(plan);
        WriteText(Path.Combine(outDir, "方形折痕图.svg"), SvgIO.ModelToSvg(sq, unit));
        WriteText(Path.Combine(outDir, "方形折痕图.json"), SvgIO.ModelToJson(sq));
        WritePng(Path.Combine(outDir, "方形折痕图.png"), PngIO.RenderCreaseBlack(sq));
        WritePng(Path.Combine(outDir, "方形折痕图_彩色.png"), PngIO.RenderCreaseColor(sq));
        Console.WriteLine($"方形 {SvgIO.Describe(sq)}");
        FoldSquareOut(plan, outDir, "折叠成品_方形CP", foldUnit);

        // 折叠成品（长条顺次折）
        var (faces, creases, _) = MasterOps.ChainFromMaster(master);
        var mv = strip.Folds.Select(f => f.MvType).ToList();
        var (world0, _) = FoldOps.RollFold(faces, creases);
        var rank0 = FoldOps.LayerRanks(faces.Count, creases, mv);
        var bad = FoldOps.VerifyLayers(creases, rank0);
        var fm = new FoldedModel(world0, rank0, mv, faces, creases).OrientLandscape();

        WriteText(Path.Combine(outDir, "折叠成品.svg"),
                  SvgIO.FoldedToSvg(fm.World, fm.Rank, null, mv, unit: foldUnit, color: true, flipY: true));
        WritePng(Path.Combine(outDir, "折叠成品.png"),
                 PngIO.RenderFolded(fm.World, unit: foldUnit, rank: fm.Rank, mvSeq: mv));
        WritePng(Path.Combine(outDir, "原子片分解.png"), PngIO.RenderPieces(fm.World, foldUnit));

        var (bx0, by0, bx1, by1) = fm.Bbox();
        double areaDiff = Math.Abs(faces.Sum(Geom.PolyArea) - fm.World.Sum(Geom.PolyArea));
        Console.WriteLine($"折叠 {fm.World.Count}片 {fm.NLayers}层 " +
                          $"{bx1 - bx0:0}x{by1 - by0:0}格  " +
                          $"面积守恒差 {areaDiff:E2}  折痕违规 {bad.Count}/{creases.Count}");
        Console.WriteLine($"-> {outDir}/");
        return 0;
    }

    static void FoldSquareOut(SquarePlan plan, string outDir, string stem, double unit)
    {
        var fm = FoldOps.FoldSquare(plan);
        string path = Path.Combine(outDir, stem);
        var mvseq = Enumerable.Range(0, fm.World.Count)
                              .Select(i => fm.MvMap.TryGetValue(i, out var v) ? v : Mv.Mountain)
                              .ToList();
        WriteText(path + ".svg", SvgIO.FoldedToSvg(fm.World, fm.Rank, null, mvseq, unit: unit, color: true, flipY: false));
        WritePng(path + ".png",
                 PngIO.RenderFolded(fm.World, unit: unit, rank: fm.Rank, mvSeq: mvseq, flipY: false));

        var (x0, y0, x1, y1) = fm.Bbox();
        Console.WriteLine($"方形折叠 {fm.World.Count}片  {fm.NLayers}层  " +
                          $"成品 {x1 - x0:0}x{y1 - y0:0} 格  " +
                          $"面积守恒差 {Math.Abs(fm.AreaFlat() - fm.AreaFolded()):E2}");
        Console.WriteLine($"  -> {path}.svg / .png");
    }

    // ==================================================================
    //  verify —— 往返一致性自检
    // ==================================================================

    /// <summary>普通文本 -&gt; 折痕母版串（折纸字库）。</summary>
    static int CmdText(Args a)
    {
        // 短/长选项在 Args 里是两个独立键，两个都得查
        string text = (a.Has("t") || a.Has("text")) ? a.Get("t") ?? a.Get("text")!
            : (a.Has("tf") || a.Has("text-file")) ? ReadText(a.Get("tf") ?? a.Get("text-file")!)
            : "";

        if (string.IsNullOrWhiteSpace(text))
            throw new UserError("需要 -t <文本> 或 -f <文本文件路径>");

        bool mirror = a.Flag("mirror");
        bool left = a.Flag("left");

        string master = FoldFont.TextToMaster(text, mirror, left);
        if (master.Length == 0)
            throw new UserError(
                $"「{text.Trim()}」里没有字库支持的字符。可用：{FoldFont.Chars}");

        var m = Master.ParseMaster(master);
        var plan = SquareOps.PlanFromMaster(m);
        var fm = FoldOps.FoldSquare(plan, orient: false);
        var (x0, y0, x1, y1) = Geom.PolyBbox(fm.World);

        Console.WriteLine($"文本: {text.Replace("\n", " / ")}");
        Console.WriteLine($"母版: {m.NCell} 格  竖折痕 {m.Pipes.Count} 条  ->  方形 {plan.N}×{plan.N}  折痕 {plan.Fold.Count} 条");
        Console.WriteLine($"成品: {x1 - x0:0.#}×{y1 - y0:0.#} 格");
        Console.WriteLine();
        Console.WriteLine(master);

        if (a.Has("o"))
        {
            string op = a.Get("o")!;
            WriteText(op, master + "\n");
            Console.WriteLine($"  -> {op}");
        }
        return 0;
    }

    /// <summary>导出单个视图。SVG 是矢量，「分辨率」对它无意义，只对 PNG 有效。</summary>
    static int CmdExport(Args a, double unit, double foldUnit)
    {
        var master = ReadMaster(a);
        bool png = a.Flag("png");
        bool plain = a.Flag("plain");
        double scale = a.Get("png-scale") is { } ps
            ? ParseUnit("--png-scale", ps)
            : 2.0;

        string view = a.Get("view", "方形");
        string outPath = a.Get("o") ?? Path.Combine("out", view + (png ? ".png" : ".svg"));

        var stripModel = Model.StripModel(master);
        var plan = SquareOps.PlanFromMaster(master);
        var squareModel = Model.SquareModel(plan);
        var mv = stripModel.Folds.Select(f => f.MvType).ToList();
        var stripFold = FoldOps.FoldStrip(master, mv, orient: true);
        var squareFold = FoldOps.FoldSquare(plan, orient: false);

        // 视图名 -> (SVG 文本, PNG 渲染器)
        (string Svg, Func<Raster> Png) pick = view switch
        {
            "长条" => (
                SvgIO.ModelToSvg(stripModel, unit, unit * 0.58, 1.2 * scale, color: !plain),
                () => (PngIO.RenderCreaseBlack(stripModel, unit * scale,
                                       unit * scale * 0.58, lw: 1.2 * scale))),

            "方形" => (
                SvgIO.ModelToSvg(squareModel, unit, unit, 1.6 * scale, color: !plain),
                () => (PngIO.RenderCreaseBlack(squareModel, unit * scale,
                                       unit * scale, lw: 1.6 * scale))),

            "长条成品" => (
                SvgIO.FoldedToSvg(stripFold.World, stripFold.Rank, plain ? null : stripFold.MvMap,
                                  mvSeq: null, unit: foldUnit, color: !plain, flipY: true),
                () => (PngIO.RenderFolded(stripFold.World, foldUnit * scale,
                                       foldUnit * scale * 0.65, rank: stripFold.Rank,
                                       mvMap: plain ? null : stripFold.MvMap,
                                       lw: plain ? 0 : 1.2 * scale,
                                       supersample: scale >= 3 ? 4 : 2, flipY: true,
                                       forceFill: plain ? Rgb.Black : null))),

            "方形成品" => (
                SvgIO.FoldedToSvg(squareFold.World, squareFold.Rank, plain ? null : squareFold.MvMap,
                                  mvSeq: null, unit: foldUnit, color: !plain, flipY: false),
                () => (PngIO.RenderFolded(squareFold.World, foldUnit * scale,
                                       foldUnit * scale * 0.65, rank: squareFold.Rank,
                                       mvMap: plain ? null : squareFold.MvMap,
                                       lw: plain ? 0 : 1.2 * scale,
                                       supersample: scale >= 3 ? 4 : 2, flipY: false,
                                       forceFill: plain ? Rgb.Black : null))),

            "长条原子片" => (
                SvgIO.PiecesToSvg(stripFold.World, foldUnit, foldUnit * 0.6, flipY: true),
                () => (PngIO.RenderPieces(stripFold.World, foldUnit * scale,
                                       foldUnit * scale * 0.6, flipY: true))),

            "方形原子片" => (
                SvgIO.PiecesToSvg(squareFold.World, foldUnit, foldUnit * 0.6, flipY: false),
                () => (PngIO.RenderPieces(squareFold.World, foldUnit * scale,
                                       foldUnit * scale * 0.6, flipY: false))),

            _ => throw new UserError($"未知视图「{view}」"),
        };

        if (png)
        {
            WritePng(outPath, pick.Png());
            Console.WriteLine($"导出 PNG {scale:0.#}×  {(plain ? "黑白" : "彩色")}  -> {outPath}  ({new FileInfo(outPath).Length / 1024} KB)");
        }
        else
        {
            WriteText(outPath, pick.Svg);
            Console.WriteLine($"导出 SVG{(plain ? "  黑白/剪影" : "  彩色")}  -> {outPath}  ({new FileInfo(outPath).Length / 1024} KB)");
        }
        return 0;
    }

    static int CmdVerify(Args a, double unit)
    {
        var master = ReadMaster(a);
        int bad = 0;
        void Check(string name, bool ok)
        {
            Console.WriteLine($"  [{(ok ? "OK" : "!!")}] {name}");
            if (!ok) bad++;
        }

        foreach (var (tag, model) in new (string, Model)[]
                 {
                     ("长条", Model.StripModel(master)),
                     ("方形", Model.SquareModel(SquareOps.PlanFromMaster(master))),
                 })
        {
            // 模型 -> svg -> 模型 -> svg：两次写出的文本必须完全一致
            string s1 = SvgIO.ModelToSvg(model, unit);
            var back = SvgIO.SvgToModel(s1);
            string s2 = SvgIO.ModelToSvg(back, unit);
            Check($"{tag} model->svg->model->svg 文本一致", s1 == s2);

            Check($"{tag} 折痕数一致", model.Folds.Count == back.Folds.Count);
            Check($"{tag} 剪切线数一致", model.Cut.Count == back.Cut.Count);
            Check($"{tag} 外框数一致", model.Border.Count == back.Border.Count);

            bool mvOk = model.Folds.Count == back.Folds.Count
                        && model.Folds.Zip(back.Folds)
                              .All(p => p.First.MvType == p.Second.MvType);
            Check($"{tag} 峰谷一致", mvOk);
        }

        // 坐标系自检：长条是数学坐标（y 向上），方形是屏幕坐标（y 向下）
        var strip = Model.StripModel(master);
        Check("长条 y 已翻转到屏幕坐标", strip.Folds.All(f =>
            f.Seg.A.Y >= 0 && f.Seg.B.Y >= 0));

        Console.WriteLine(bad == 0
            ? "verify: 全部一致 ✔"
            : $"verify: {bad} 项不一致");
        return bad == 0 ? 0 : 1;
    }

    // ==================================================================
    //  参数解析（无外部依赖）
    // ==================================================================

    sealed class Args
    {
        readonly Dictionary<string, string> _opt = new(StringComparer.Ordinal);
        readonly List<string> _pos = new();
        readonly HashSet<string> _flags = new(StringComparer.Ordinal);

        /// <summary>
        /// 不接受取值的开关。必须显式列出：否则 <c>--png out.png</c> 会被解析成
        /// 「png 的值是 out.png」，开关静默失效、路径也被吞掉，退出码还是 0。
        /// </summary>
        static readonly HashSet<string> Valueless = new(StringComparer.Ordinal)
        {
            "png", "plain", "lenient", "mirror", "left",
        };

        public static Args Parse(string[] argv)
        {
            var a = new Args();
            for (int i = 0; i < argv.Length; i++)
            {
                string t = argv[i];
                if (!t.StartsWith('-')) { a._pos.Add(t); continue; }
                string key = t.TrimStart('-');
                if (!Valueless.Contains(key) && i + 1 < argv.Length && !argv[i + 1].StartsWith('-'))
                    a._opt[key] = argv[++i];
                else
                    a._flags.Add(key);
            }
            return a;
        }

        public bool Has(string k) => _opt.ContainsKey(k) || _flags.Contains(k);
        public string? Get(string k) => _opt.TryGetValue(k, out var v) ? v : null;
        public string Get(string k, string d) => _opt.TryGetValue(k, out var v) ? v : d;
        public bool Flag(string k) => _flags.Contains(k) && !_opt.ContainsKey(k);
        public string? Positional(int i) => i < _pos.Count ? _pos[i] : null;
    }
}