using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    public partial class StatusView : UserControl
    {
        public StatusView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
        }

        private void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is StatusViewModel viewModel && e.NewValue is ChangeNode node && node.Item is FileStatusItemViewModel item)
            {
                viewModel.SelectedEntry = item;
            }
        }

        private void OnDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not StatusViewModel viewModel || viewModel.SelectedEntry == null)
            {
                return;
            }

            viewModel.OpenSelectedCommand.Execute(viewModel.SelectedEntry);
            e.Handled = true;
        }
    }
}
