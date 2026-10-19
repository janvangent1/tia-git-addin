using System;
using System.IO;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;
using TiaGitAddIn.Models.Hmi;
using TiaGitAddIn.Services.Hmi;
using Xunit;

namespace TiaGitAddIn.IntegrationTests.Services
{
    public sealed class HmiPreviewAndImportWarningTests
    {
        [Fact]
        public void ScreenPreviewMarksAMovedButtonAndAnAddedIoField()
        {
            const string left = @"<Screen name=""Main"" width=""800"" height=""480"">
  <Item kind=""Button"" name=""Start"" left=""10"" top=""20"" width=""80"" height=""40""><Text>Start</Text></Item>
</Screen>";
            const string right = @"<Screen name=""Main"" width=""800"" height=""480"">
  <Item kind=""Button"" name=""Start"" left=""40"" top=""20"" width=""80"" height=""40""><Text>Start</Text></Item>
  <Item kind=""IOField"" name=""Speed"" left=""10"" top=""80"" width=""100"" height=""30"" processValue=""DB1.Speed""/>
</Screen>";

            Assert.True(HmiFilePreview.TryCreate(left, right, out HmiFilePreview? preview));
            Assert.NotNull(preview);
            Assert.NotNull(preview.LeftScreen);
            Assert.NotNull(preview.RightScreen);
            HmiScreenItemPreview previous = Assert.Single(preview.LeftScreen.Items);
            Assert.Equal("Changed", previous.Change);
            Assert.Equal("Changed", preview.RightScreen.Items[0].Change);
            Assert.Equal(40, preview.RightScreen.Items[0].Left);
            Assert.Equal("Added", preview.RightScreen.Items[1].Change);
            Assert.Equal("Speed", preview.RightScreen.Items[1].Name);
            Assert.Equal("DB1.Speed", preview.RightScreen.Items[1].Label);
            Assert.Null(HmiFilePreview.ApplyScreenChoice(preview, false));
            Assert.Same(preview, HmiFilePreview.ApplyScreenChoice(preview, true));
        }

        [Fact]
        public void TagPreviewShowsTheOldAndNewAddress()
        {
            const string left = "<TagTable><Tag><Name>Motor</Name><LogicalAddress>%MW10</LogicalAddress><DataType>Int</DataType><Comment>run</Comment></Tag></TagTable>";
            const string right = "<TagTable><Tag><Name>Motor</Name><LogicalAddress>%MW12</LogicalAddress><DataType>Int</DataType><Comment>run</Comment></Tag></TagTable>";

            Assert.True(HmiFilePreview.TryCreate(left, right, out HmiFilePreview? preview));
            Assert.NotNull(preview);
            HmiTagPreviewRow row = Assert.Single(preview.Tags);
            Assert.Equal("Motor", row.Name);
            Assert.Equal("Changed", row.Change);
            Assert.Equal("%MW10 → %MW12", row.Address);
            Assert.Equal("Int", row.DataType);
            Assert.Equal("run", row.Comment);
            Assert.Same(preview, HmiFilePreview.ApplyScreenChoice(preview, false));
        }

        [Fact]
        public void ImportDifferenceListsAChangedScreenAndATagOnlyInTheImport()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-preview-" + Guid.NewGuid().ToString("N"));
            string repo = Path.Combine(root, "repo");
            string snapshot = Path.Combine(root, "snapshot");
            try
            {
                Write(repo, "HmiExport/Panel/Screens/Main.xml", "<Screen name=\"Main\" width=\"1\" height=\"1\"/>");
                Write(snapshot, "HmiExport/Panel/Screens/Main.xml", "<Screen name=\"Main\" width=\"2\" height=\"1\"/>");
                Write(repo, "HmiExport/Panel/Tags/Tags.hmi.yml", "Name: Extra\n");

                var differences = HmiImportDifference.List(repo, snapshot);

                Assert.Contains("Panel/Screens/Main.xml differs", differences);
                Assert.Contains("Panel/Tags/Tags.hmi.yml is only in the files to import", differences);
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
        public void ScreenChoiceRoundTripsAndStaysOnWhenTheSettingIsMissing()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-screen-choice-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var service = new ConfigurationService();
                File.WriteAllText(Path.Combine(root, ConfigurationService.FileName), "{\"CompareXmlByElement\":true}");
                Assert.True(service.Load(root).ShowScreenPreview);

                service.Save(root, new GitConfiguration { RepositoryPath = root, ShowScreenPreview = false });
                Assert.False(service.Load(root).ShowScreenPreview);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static void Write(string root, string relative, string text)
        {
            string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
    }
}
