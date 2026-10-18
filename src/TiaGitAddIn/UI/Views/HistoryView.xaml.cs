using System.Windows;
using System.Windows.Controls;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    public partial class HistoryView : UserControl
    {
        public HistoryView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
        }

        private void OnCommitListSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (MessageColumn == null)
            {
                return;
            }

            double width = CommitList.ActualWidth - 75 - 140 - 150 - 28;
            MessageColumn.Width = width < 160 ? 160 : width;
        }

        private void OnFileSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not HistoryViewModel history || e.NewValue is not ChangeNode node || !node.IsFile)
            {
                return;
            }

            string path = node.Item as string ?? node.Title;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            _ = history.OpenFile(path);
        }
    }
}
