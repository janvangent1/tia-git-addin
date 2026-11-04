using System;
using System.IO;

namespace TiaGitAddIn.UI
{
    internal static class RepositoryPaths
    {
        public static bool TryMakeRelative(string repositoryRoot, string fullPath, out string relative)
        {
            relative = string.Empty;
            if (string.IsNullOrWhiteSpace(repositoryRoot) || string.IsNullOrWhiteSpace(fullPath))
            {
                return false;
            }

            string root = Path.GetFullPath(repositoryRoot);
            string full = Path.GetFullPath(fullPath);
            string rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            relative = full.Substring(rootWithSeparator.Length).Replace('\\', '/');
            return relative.Length > 0 && relative.IndexOf("..", StringComparison.Ordinal) < 0;
        }
    }
}
