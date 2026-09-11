using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasyXray.ViewModels;

namespace EasyXray;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new TopologyViewModel();
        SizeChanged += (_, _) => UpdateResponsiveLayout();
        Loaded += (_, _) => UpdateResponsiveLayout();
    }

    private TopologyViewModel ViewModel => (TopologyViewModel)DataContext;
    private void WorkflowStepChanged(object sender, SelectionChangedEventArgs e) => Dispatcher.BeginInvoke(UpdateResponsiveLayout);
    private void UpdateResponsiveLayout()
    {
        ViewModel.UpdateLayoutWidth(ActualWidth);
        var compact = ActualWidth < 1080;
        foreach (var grid in Descendants<Grid>(this))
        {
            if (grid.ColumnDefinitions.Count == 5 && grid.RowDefinitions.Count == 5)
                PositionPanels(grid, compact, [0, 2, 4]);
            else if (grid.ColumnDefinitions.Count == 3 && grid.RowDefinitions.Count == 3)
                PositionPanels(grid, compact, [0, 2]);
        }
    }

    private static void PositionPanels(Grid grid, bool compact, IReadOnlyList<int> wideColumns)
    {
        for (var index = 0; index < wideColumns.Count && index < grid.Children.Count; index++)
        {
            var child = grid.Children[index];
            Grid.SetColumn(child, compact ? 0 : wideColumns[index]);
            Grid.SetRow(child, compact ? index * 2 : 0);
            if (child is FrameworkElement element)
                element.Margin = compact && index > 0 ? new Thickness(0, 18, 0, 0) : new Thickness(0);
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private void SshPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.SetSshPassword(((PasswordBox)sender).SecurePassword);
    private void PrivateKeyPassphraseChanged(object sender, RoutedEventArgs e) => ViewModel.SetPrivateKeyPassphrase(((PasswordBox)sender).SecurePassword);
    private void SudoPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.SetSudoPassword(((PasswordBox)sender).SecurePassword);
    private void RootPasswordChanged(object sender, RoutedEventArgs e) => ViewModel.SetRootPassword(((PasswordBox)sender).SecurePassword);
}
