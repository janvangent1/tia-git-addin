using System;
using System.Collections.Generic;
using System.Globalization;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Reporting
{
    public static class HistoryPdfReport
    {
        public static PdfReport Create(
            string repositoryPath,
            IReadOnlyList<CommitInfo> commits,
            CommitInfo? selectedCommit,
            IReadOnlyList<string>? selectedChangedFiles)
        {
            var lines = new List<PdfLine>
            {
                new PdfLine("Version history", PdfLineStyle.Heading),
                new PdfLine(string.IsNullOrWhiteSpace(repositoryPath) ? "Repository" : repositoryPath),
                new PdfLine(string.Empty)
            };

            if (commits == null || commits.Count == 0)
            {
                lines.Add(new PdfLine("No commits are loaded.", PdfLineStyle.Note));
                return new PdfReport("Version history", lines);
            }

            foreach (CommitInfo commit in commits)
            {
                if (commit == null)
                {
                    continue;
                }

                string hash = ShortHash(commit.Hash);
                string date = commit.AuthorDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
                lines.Add(new PdfLine(hash + "  " + date + "  " + commit.AuthorName, PdfLineStyle.Heading));
                lines.Add(new PdfLine(commit.Subject ?? string.Empty));
                lines.Add(new PdfLine(string.Empty));
            }

            if (selectedCommit != null)
            {
                lines.Add(new PdfLine("Changed files in " + ShortHash(selectedCommit.Hash), PdfLineStyle.Heading));
                if (selectedChangedFiles == null || selectedChangedFiles.Count == 0)
                {
                    lines.Add(new PdfLine("No changed files are listed for this commit.", PdfLineStyle.Note));
                }
                else
                {
                    foreach (string file in selectedChangedFiles)
                    {
                        lines.Add(new PdfLine(file ?? string.Empty));
                    }
                }
            }

            return new PdfReport("Version history", lines);
        }

        private static string ShortHash(string? hash)
        {
            if (hash == null || hash.Trim().Length == 0)
            {
                return string.Empty;
            }

            return hash.Length <= 7 ? hash : hash.Substring(0, 7);
        }
    }
}
