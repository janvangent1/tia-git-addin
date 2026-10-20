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

        /// <summary>Git -E. Ignore a change that only expands a tab into spaces.</summary>
        public bool IgnoreTabExpansion { get; set; }

        /// <summary>Git -Z. Ignore white space at the end of a line.</summary>
        public bool IgnoreTrailingSpace { get; set; }

        /// <summary>Git -b. Ignore a change in the amount of white space.</summary>
        public bool IgnoreSpaceChange { get; set; }

        /// <summary>Git -w. Ignore every white space character. On until the user turns it off.</summary>
        public bool IgnoreAllSpace { get; set; } = true;

        /// <summary>Git -B. Leave a change out when its lines are all blank.</summary>
        public bool IgnoreBlankLines { get; set; }

        /// <summary>
        /// When true, a // line comment or a (* *) / *( *) block comment is not a change.
        /// Off until the user turns it on.
        /// </summary>
        public bool IgnoreComments { get; set; }

        /// <summary>
        /// When true, a screen file is drawn as buttons and I/O fields.
        /// When false, that file stays as XML text.
        /// </summary>
        public bool ShowScreenPreview { get; set; } = true;

        public List<string> DisabledHmiCategories { get; set; } = new List<string>();

        public List<string> DisabledHardwareCategories { get; set; } = new List<string>();

        public string? SiemensCompareToolPath { get; set; }

        public string? NodeExecutablePath { get; set; }
    }
}
