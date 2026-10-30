using System.Collections.Generic;

namespace TiaGitAddIn.Models
{
    /// <summary>
    /// One commit that changed the requested files, with the names those files had in that commit.
    /// </summary>
    public sealed class FileRevision
    {
        public CommitInfo Commit { get; set; } = new CommitInfo();

        public IReadOnlyList<string> Paths { get; set; } = new List<string>();
    }
}
