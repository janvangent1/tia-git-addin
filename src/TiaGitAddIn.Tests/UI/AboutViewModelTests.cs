using System.IO;
using System.Linq;
using System.Xml.Linq;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class AboutViewModelTests
    {
        [Fact]
        public void AboutPageShowsTheVersionAndShadesUncoveredRows()
        {
            var about = new AboutViewModel();
            string packaged = PackagedVersion();

            Assert.Equal(AddInVersion.Current, packaged);
            Assert.Equal(AddInVersion.Current, about.Version);
            Assert.Contains(about.Sections, section => section.Title == "WinCC Unified");
            Assert.Contains(
                about.Sections.SelectMany(section => section.Rows),
                row => row.Item.Contains("Recipes") && row.IsGap && row.Export.StartsWith("None"));
            Assert.Contains(
                about.Sections.SelectMany(section => section.Rows),
                row => row.Item == "System cycles" && !row.IsGap);
            Assert.Contains(
                about.Sections.SelectMany(section => section.Rows),
                row => row.Item == "Project pictures" && !row.IsGap);
            Assert.Contains(about.Sections, section => section.Title == "Project");
            Assert.Contains(
                about.Sections.SelectMany(section => section.Rows),
                row => row.Item == "PLC blocks" && row.IsGap);
            Assert.DoesNotContain(
                about.Sections.SelectMany(section => section.Rows),
                row => row.Item == "Text lists" && row.IsGap);
        }

        private static string PackagedVersion()
        {
            string root = Directory.GetCurrentDirectory();
            for (int i = 0; i < 8; i++)
            {
                string path = Path.Combine(root, "src", "TiaGitAddIn", "AddInPublisherConfiguration.xml");
                if (File.Exists(path))
                {
                    XNamespace ns = "http://www.siemens.com/automation/Openness/AddIn/Publisher/V21";
                    XElement version = XDocument.Load(path).Root?.Element(ns + "AddInVersion");
                    return version == null ? string.Empty : version.Value;
                }

                DirectoryInfo parentDirectory = Directory.GetParent(root);
                if (parentDirectory == null)
                {
                    break;
                }

                root = parentDirectory.FullName;
            }

            throw new FileNotFoundException("AddInPublisherConfiguration.xml");
        }
    }
}
