using System.Globalization;
using Avalonia.Data.Converters;

namespace Origami.Desktop;

/// <summary>
/// 把 <see cref="ShapeKind"/> 转换成 bool，供 RadioButton 的 <c>IsChecked</c> 单选绑定。
/// <para>
/// 界面上「长条 / 方形」是两个 RadioButton，而状态是一个枚举 —— 中间必须有转换。
/// 做成单向（枚举 -&gt; bool）是有意的：点击切换由 RadioButton 自己完成（它先改自己的
/// IsChecked），再由 <c>Check</c> 事件写回 VM，避免双向绑定在初始化阶段互相回写、
/// 把单选按钮的状态搅乱。
/// </para>
/// </summary>
public sealed class ShapeToBoolConverter : IValueConverter
{
    public static readonly ShapeToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ShapeKind s
           && Enum.TryParse(parameter?.ToString(), ignoreCase: true, out ShapeKind want)
           && s == want;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("单向转换：视图切换由 RadioButton 的 Check 事件驱动");
}

/// <summary>把 <see cref="ContentKind"/> 转换成 bool，用途同<see cref="ShapeToBoolConverter"/>。</summary>
public sealed class ContentToBoolConverter : IValueConverter
{
    public static readonly ContentToBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ContentKind c
           && Enum.TryParse(parameter?.ToString(), ignoreCase: true, out ContentKind want)
           && c == want;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("单向转换：视图切换由 RadioButton 的 Check 事件驱动");
}