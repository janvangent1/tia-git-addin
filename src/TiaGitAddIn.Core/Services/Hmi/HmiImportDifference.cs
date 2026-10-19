using System;
using System.Collections.Generic;
using System.IO;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Compares screen and tag-table files in two HmiExport trees.
    /// The first tree is the folder about to be imported. The second is a snapshot of the open project.
    /// </summary>
    public static class HmiImportDifference
    {
        public static IReadOnlyList<string> List(string repositoryPath, string projectSnapshotPath)
        {
            var differences = new List<string>();
            Dictionary<string, string> imported = Index(repositoryPath);
            Dictionary<string, string> current = Index(projectSnapshotPath);
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in imported.Keys)
            {
                names.Add(name);
            }

            foreach (string name in current.Keys)
            {
                names.Add(name);
            }

            foreach (string name in names)
            {
                bool hasImported = imported.TryGetValue(name, out string importedText);
                bool hasCurrent = current.TryGetValue(name, out string currentText);
                if (!hasImported)
                {
                    differences.Add(name + " is only in the open project and will be removed");
                }
                else if (!hasCurrent)
                {
                    differences.Add(name + " is only in the files to import");
                }
                else if (!string.Equals(importedText, currentText, StringComparison.Ordinal))
                {
                    differences.Add(name + " differs");
                }
            }

            return differences;
        }

        private static Dictionary<string, string> Index(string repositoryPath)
        {
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                return files;
            }

            string root = Path.Combine(repositoryPath, HmiExportStore.RootFolderName);
            if (!Directory.Exists(root))
            {
                return files;
            }

            foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!IsScreenOrTagTable(root, path))
                {
                    continue;
                }

                string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                files[relative.Replace('\\', '/')] = Normalize(File.ReadAllText(path));
            }

            return files;
        }

        private static bool IsScreenOrTagTable(string root, string path)
        {
            string relative = path.Substring(root.Length).Replace('\\', '/');
            return relative.IndexOf("/Screens/", StringComparison.OrdinalIgnoreCase) >= 0
                || relative.IndexOf("/TagTables/", StringComparison.OrdinalIgnoreCase) >= 0
                || relative.IndexOf("/Tags/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Trim();
        }
    }
}
