using System.Windows.Controls;

namespace TiaGitAddIn.UI.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
        }
    }
}
