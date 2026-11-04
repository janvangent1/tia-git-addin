using System.IO;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;
using Xunit;

namespace TiaGitAddIn.Tests.Configuration
{
    public sealed class ConfigurationServiceTests
    {
        [Fact]
        public void SaveThenLoadRoundTripsConfiguration()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                ConfigurationService service = new ConfigurationService();
                GitConfiguration config = new GitConfiguration
                {
                    GitExecutablePath = @"C:\Program Files\Git\cmd\git.exe",
                    RepositoryPath = root,
                    DefaultRemote = "upstream",
                    CommitAuthorName = "Engineer"
                };

                service.Save(root, config);
                GitConfiguration loaded = service.Load(root);

                Assert.Equal(config.GitExecutablePath, loaded.GitExecutablePath);
                Assert.Equal(config.RepositoryPath, loaded.RepositoryPath);
                Assert.Equal(config.DefaultRemote, loaded.DefaultRemote);
                Assert.Equal(config.CommitAuthorName, loaded.CommitAuthorName);
                Assert.True(loaded.CompareXmlByElement);
                Assert.True(loaded.ShowScreenPreview);
                Assert.True(loaded.ShowSclStructure);
                Assert.True(loaded.IgnoreAllSpace);
                Assert.False(loaded.IgnoreBlankLines);
                Assert.False(loaded.IgnoreComments);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void WhitespaceDiffOptionsRoundTripAndDefaultToIgnoreAllSpace()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                ConfigurationService service = new ConfigurationService();
                Assert.True(service.Load(root).IgnoreAllSpace);

                service.Save(root, new GitConfiguration
                {
                    RepositoryPath = root,
                    IgnoreTabExpansion = true,
                    IgnoreTrailingSpace = true,
                    IgnoreSpaceChange = true,
                    IgnoreAllSpace = false,
                    IgnoreBlankLines = true
                });
                GitConfiguration loaded = service.Load(root);
                Assert.True(loaded.IgnoreTabExpansion);
                Assert.True(loaded.IgnoreTrailingSpace);
                Assert.True(loaded.IgnoreSpaceChange);
                Assert.False(loaded.IgnoreAllSpace);
                Assert.True(loaded.IgnoreBlankLines);

                File.WriteAllText(Path.Combine(root, ConfigurationService.FileName), "{}");
                GitConfiguration defaults = service.Load(root);
                Assert.True(defaults.IgnoreAllSpace);
                Assert.False(defaults.IgnoreComments);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void PlainTextXmlDiffChoiceRoundTrips()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                ConfigurationService service = new ConfigurationService();
                service.Save(root, new GitConfiguration
                {
                    RepositoryPath = root,
                    CompareXmlByElement = false
                });

                Assert.False(service.Load(root).CompareXmlByElement);
                service.Save(root, new GitConfiguration
                {
                    RepositoryPath = root,
                    ShowScreenPreview = false
                });
                Assert.False(service.Load(root).ShowScreenPreview);
                service.Save(root, new GitConfiguration
                {
                    RepositoryPath = root,
                    DisabledHmiCategories = new System.Collections.Generic.List<string> { "Screens", "Tags" },
                    DisabledHardwareCategories = new System.Collections.Generic.List<string> { "Drives" }
                });
                GitConfiguration categories = service.Load(root);
                Assert.Equal(new[] { "Screens", "Tags" }, categories.DisabledHmiCategories);
                Assert.Equal(new[] { "Drives" }, categories.DisabledHardwareCategories);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void EverySettingRoundTripsIncludingPathsTheFormDoesNotEdit()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                ConfigurationService service = new ConfigurationService();
                GitConfiguration config = new GitConfiguration
                {
                    GitExecutablePath = @"C:\Git\cmd\git.exe",
                    RepositoryPath = root,
                    DefaultRemote = "upstream",
                    RemoteUrl = "https://github.com/org/repo.git",
                    MaxLogEntries = 40,
                    Version = 3,
                    CommitAuthorName = "Engineer",
                    CommitAuthorEmail = "engineer@example.com",
                    CompareXmlByElement = false,
                    IgnoreTabExpansion = true,
                    IgnoreTrailingSpace = true,
                    IgnoreSpaceChange = true,
                    IgnoreAllSpace = false,
                    IgnoreBlankLines = true,
                    IgnoreComments = true,
                    ShowScreenPreview = false,
                    ShowSclStructure = false,
                    DisabledHmiCategories = new System.Collections.Generic.List<string> { "Screens" },
                    DisabledHardwareCategories = new System.Collections.Generic.List<string> { "Drives" },
                    SiemensCompareToolPath = @"C:\Tools\compare.exe",
                    NodeExecutablePath = @"C:\Tools\node.exe"
                };

                service.Save(root, config);
                GitConfiguration loaded = service.Load(root);

                Assert.Equal(config.GitExecutablePath, loaded.GitExecutablePath);
                Assert.Equal(config.RepositoryPath, loaded.RepositoryPath);
                Assert.Equal(config.DefaultRemote, loaded.DefaultRemote);
                Assert.Equal(config.RemoteUrl, loaded.RemoteUrl);
                Assert.Equal(40, loaded.MaxLogEntries);
                Assert.Equal(3, loaded.Version);
                Assert.Equal(config.CommitAuthorName, loaded.CommitAuthorName);
                Assert.Equal(config.CommitAuthorEmail, loaded.CommitAuthorEmail);
                Assert.False(loaded.CompareXmlByElement);
                Assert.True(loaded.IgnoreTabExpansion);
                Assert.True(loaded.IgnoreTrailingSpace);
                Assert.True(loaded.IgnoreSpaceChange);
                Assert.False(loaded.IgnoreAllSpace);
                Assert.True(loaded.IgnoreBlankLines);
                Assert.True(loaded.IgnoreComments);
                Assert.False(loaded.ShowScreenPreview);
                Assert.False(loaded.ShowSclStructure);
                Assert.Equal(new[] { "Screens" }, loaded.DisabledHmiCategories);
                Assert.Equal(new[] { "Drives" }, loaded.DisabledHardwareCategories);
                Assert.Equal(config.SiemensCompareToolPath, loaded.SiemensCompareToolPath);
                Assert.Equal(config.NodeExecutablePath, loaded.NodeExecutablePath);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void LoadReturnsDefaultConfigurationWhenFileIsMalformed()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(Path.Combine(root, ConfigurationService.FileName), "{ invalid json");
                ConfigurationService service = new ConfigurationService();

                GitConfiguration loaded = service.Load(root);

                Assert.Equal(root, loaded.RepositoryPath);
                Assert.Equal("git", loaded.GitExecutablePath);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void LoadNormalizesMissingRepositoryPath()
        {
            string root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                File.WriteAllText(
                    Path.Combine(root, ConfigurationService.FileName),
                    "{\"GitExecutablePath\":\"git.exe\"}");
                ConfigurationService service = new ConfigurationService();

                GitConfiguration loaded = service.Load(root);

                Assert.Equal(root, loaded.RepositoryPath);
                Assert.Equal("git.exe", loaded.GitExecutablePath);
                Assert.True(loaded.CompareXmlByElement);
                Assert.True(loaded.ShowScreenPreview);
                Assert.True(loaded.ShowSclStructure);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
