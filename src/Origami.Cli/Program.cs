namespace Origami.Cli;

/// <summary>
/// <c>origami</c> 命令行入口。参数解析与全部文件 I/O 都在
/// <see cref="Core.Cli"/> 里；库（<c>Origami.Core</c>）只做纯计算。
/// </summary>
public static class Program
{
    public static int Main(string[] args) => Cli.Main(args);
}