using System.Windows.Controls;

namespace TiaGitAddIn.UI.Views.Comparison
{
    public partial class SclDiffView : UserControl
    {
        public SclDiffView()
        {
            InitializeComponent();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ChangeList.SelectedItem != null)
            {
                ChangeList.ScrollIntoView(ChangeList.SelectedItem);
            }
        }
    }
}
