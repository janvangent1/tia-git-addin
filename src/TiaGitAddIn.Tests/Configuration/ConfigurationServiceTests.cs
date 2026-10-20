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
                Assert.True(loaded.IgnoreAllSpace);
                Assert.False(loaded.IgnoreBlankLines);
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
                Assert.True(service.Load(root).IgnoreAllSpace);
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
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
