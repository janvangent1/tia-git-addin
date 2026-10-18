using System.Collections.Generic;

namespace TiaGitAddIn.Models
{
    public sealed class GitConfiguration
    {
        public string? GitExecutablePath { get; set; } = "git";

        public string RepositoryPath { get; set; } = string.Empty;

        public string DefaultRemote { get; set; } = "origin";

        public string RemoteUrl { get; set; } = string.Empty;

        public int MaxLogEntries { get; set; } = 200;

        public int Version { get; set; } = 1;

        public string CommitAuthorName { get; set; } = string.Empty;

        public string CommitAuthorEmail { get; set; } = string.Empty;

        public bool CompareXmlByElement { get; set; } = true;

        public List<string> DisabledHmiCategories { get; set; } = new List<string>();

        public List<string> DisabledHardwareCategories { get; set; } = new List<string>();

        public string? SiemensCompareToolPath { get; set; }

        public string? NodeExecutablePath { get; set; }
    }
}
