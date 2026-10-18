using TiaGitAddIn.Models;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class FileStatusItemViewModel(FileStatusEntry entry)
    {

        public string FilePath { get; } = entry.FilePath;

        public string Area { get; } = GetArea(entry);

        public FileStatus Status { get; } = entry.IsStaged ? entry.IndexStatus : entry.WorkTreeStatus;

        public string StatusText { get; } = GetStatusText(entry);

        public string StatusBadge { get; } = Badge(entry);

        public bool CanStage { get; } = entry.IsUnstaged || entry.IndexStatus == FileStatus.Untracked;

        public bool CanUnstage { get; } = entry.IsStaged;

        public bool CanDiscard { get; } = entry.IsUnstaged
            && entry.WorkTreeStatus != FileStatus.Untracked
            && entry.IndexStatus != FileStatus.Untracked;

        private static string GetArea(FileStatusEntry entry)
        {
            if (entry.IsConflicted)
            {
                return "Conflicts";
            }

            return entry.IsStaged ? "Staged" : "Working tree";
        }

        private static string GetStatusText(FileStatusEntry entry)
        {
            if (entry.IsConflicted)
            {
                return "Conflicted";
            }

            FileStatus status = entry.IsStaged ? entry.IndexStatus : entry.WorkTreeStatus;
            return status.ToString();
        }

        private static string Badge(FileStatusEntry entry)
        {
            if (entry.IsConflicted)
            {
                return "C";
            }

            FileStatus status = entry.IsStaged ? entry.IndexStatus : entry.WorkTreeStatus;
            switch (status)
            {
                case FileStatus.Added: return "A";
                case FileStatus.Deleted: return "D";
                case FileStatus.Renamed: return "R";
                case FileStatus.Copied: return "C";
                case FileStatus.Untracked: return "?";
                case FileStatus.Modified: return "M";
                default: return "·";
            }
        }
    }
}
