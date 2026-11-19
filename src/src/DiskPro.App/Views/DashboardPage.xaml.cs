using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPro.App.ViewModels;
namespace DiskPro.App.Views;

public partial class DashboardPage : UserControl
{
    public DashboardPage() => InitializeComponent();

    private void Cat_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<CheckBox>(d) != null) return;
        if (sender is FrameworkElement { DataContext: DashCatVm c } && DataContext is DashboardViewModel vm)
        {
            if (c.Cleanable) c.IsSelected = !c.IsSelected; else vm.GoCommand.Execute(c);
        }
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d != null) { if (d is T t) return t; d = d is System.Windows.Media.Visual ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d); }
        return null;
    }
}
