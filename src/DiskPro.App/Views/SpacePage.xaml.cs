using System.Windows;
using System.Windows.Controls;
using DiskPro.App.ViewModels;
namespace DiskPro.App.Views;
public partial class SpacePage : UserControl
{
    public SpacePage() => InitializeComponent();
    private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is SpaceViewModel vm && e.NewValue is FolderNodeVm n) vm.Current = n;
    }
    private void Treemap_NodeClicked(object? sender, FolderNodeVm n)
    {
        if (DataContext is SpaceViewModel vm) vm.DrillCommand.Execute(n);
    }
}
