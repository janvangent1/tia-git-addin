using System;
using System.IO;
using TiaGitAddIn.Services.Hmi;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class ProjectContentStoreTests
    {
        [Fact]
        public void GraphicIndexAndLibraryManifestRoundTrip()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-project-" + Guid.NewGuid().ToString("N"));
            try
            {
                string project = ProjectContentStore.ProjectFolder(root, "Line 1");
                string graphics = ProjectContentStore.GraphicsFolder(project);
                ProjectContentStore.WriteGraphicIndex(graphics, new[] { "Logo", "Pump" });
                Assert.Equal(new[] { "Logo", "Pump" }, ProjectContentStore.ReadGraphicIndex(graphics));

                string typeFolder = Path.Combine(ProjectContentStore.LibraryFolder(project), "Motors", "Pump");
                Guid guid = Guid.Parse("2f0c1a7e-6b4d-4e1a-9c3d-8a7b6c5d4e3f");
                ProjectContentStore.WriteManifest(typeFolder, "Pump", guid, "WinCCML", "Pump");

                Assert.True(ProjectContentStore.TryReadManifest(typeFolder, out string name, out Guid readGuid, out string format, out string fileBase));
                Assert.Equal("Pump", name);
                Assert.Equal(guid, readGuid);
                Assert.Equal("WinCCML", format);
                Assert.Equal("Pump", fileBase);
                Assert.Contains(Path.Combine("HmiExport", "_Project"), project);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void ChooseFormatPrefersWinCcMl()
        {
            Assert.Equal("WinCCML", ProjectContentStore.ChooseFormat(new[] { "SimaticML", "WinCCML" }));
            Assert.Equal("SimaticML", ProjectContentStore.ChooseFormat(new[] { "SimaticML" }));
            Assert.Equal(string.Empty, ProjectContentStore.ChooseFormat(null));
        }
    }
}
