using System.Collections.Generic;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class AboutViewModel : ViewModelBase
    {
        public AboutViewModel()
        {
            Version = AddInVersion.Current;
            Sections = AboutCoverage.Sections;
        }

        public string Version { get; }

        public IReadOnlyList<AboutSection> Sections { get; }
    }
}
