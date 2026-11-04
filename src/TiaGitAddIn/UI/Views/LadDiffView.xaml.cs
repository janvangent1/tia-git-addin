using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    public partial class LadDiffView : UserControl
    {
        public LadDiffView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
        }

        private void OnNetworkSelected(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ListBox list || list.SelectedItem == null)
            {
                return;
            }

            if (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) is FrameworkElement row)
            {
                row.BringIntoView();
            }

            int index = list.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            var networks = FindName("Diagram") as StackPanel;
            if (networks == null)
            {
                return;
            }

            foreach (object child in LogicalTree(networks))
            {
                if (child is FrameworkElement element && element.DataContext is LadNetworkPairViewModel pair &&
                    pair == list.SelectedItem)
                {
                    element.BringIntoView();
                    return;
                }
            }
        }

        private void OnFitWidth(object sender, RoutedEventArgs e)
        {
            if (DataContext is not LadDiffViewModel viewModel || Diagram == null || DiagramScroll == null)
            {
                return;
            }

            Diagram.UpdateLayout();
            double content = Diagram.ActualWidth / (viewModel.Zoom <= 0 ? 1 : viewModel.Zoom);
            double viewport = DiagramScroll.ViewportWidth;
            if (content < 1 || viewport < 1)
            {
                return;
            }

            double zoom = viewport / content;
            if (zoom < 0.25)
            {
                zoom = 0.25;
            }
            else if (zoom > 2.5)
            {
                zoom = 2.5;
            }

            viewModel.Zoom = zoom;
        }

        private static System.Collections.Generic.IEnumerable<DependencyObject> LogicalTree(DependencyObject root)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                if (child is DependencyObject node)
                {
                    yield return node;
                    foreach (DependencyObject nested in LogicalTree(node))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
