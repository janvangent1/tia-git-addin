using System;
using System.Collections.Generic;

namespace TiaGitAddIn.Models
{
    /// <summary>
    /// What a pull would bring in. The pull itself has not run.
    /// </summary>
    public sealed class PullPreview
    {
        public PullPreview(IReadOnlyList<CommitInfo> commits, IReadOnlyList<string> plcFiles, string message)
        {
            Commits = commits ?? Array.Empty<CommitInfo>();
            PlcFiles = plcFiles ?? Array.Empty<string>();
            Message = message ?? string.Empty;
        }

        public IReadOnlyList<CommitInfo> Commits { get; }
        public IReadOnlyList<string> PlcFiles { get; }
        public string Message { get; }
        public bool HasIncoming => Commits.Count > 0 || PlcFiles.Count > 0;
    }
}
