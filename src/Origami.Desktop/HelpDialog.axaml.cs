using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Origami.Desktop;

/// <summary>操作说明对话框。从画布左下角的「?」打开。</summary>
public partial class HelpDialog : Window
{
    public HelpDialog()
    {
        // 不要自己写 InitializeComponent() —— XAML 编译器生成的同名方法还负责
        // 把 x:Name 声明的控件字段赋值上，手写会覆盖掉它
        InitializeComponent();
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}