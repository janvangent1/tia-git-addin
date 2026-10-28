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
            IReadOnlyDictionary<string, IReadOnlyList<string>?>? filesByCommit)
        {
            var lines = new List<PdfLine>
            {
                new PdfLine("Version history", PdfLineStyle.Heading),
                new PdfLine(string.IsNullOrWhiteSpace(repositoryPath) ? "Repository" : repositoryPath),
                new PdfLine("Commits", PdfLineStyle.Heading)
            };

            var listed = new List<CommitInfo>();
            if (commits != null)
            {
                foreach (CommitInfo commit in commits)
                {
                    if (commit != null)
                    {
                        listed.Add(commit);
                    }
                }
            }

            if (listed.Count == 0)
            {
                lines.Add(new PdfLine("No commits are loaded.", PdfLineStyle.Note));
                return new PdfReport("Version history", lines);
            }

            foreach (CommitInfo commit in listed)
            {
                lines.Add(new PdfLine(CommitLine(commit)));
            }

            lines.Add(new PdfLine(string.Empty, PdfLineStyle.PageBreak));
            lines.Add(new PdfLine("Changed files", PdfLineStyle.Heading));
            foreach (CommitInfo commit in listed)
            {
                lines.Add(new PdfLine(CommitLine(commit), PdfLineStyle.Heading));
                IReadOnlyList<string>? files = null;
                if (filesByCommit != null && commit.Hash != null)
                {
                    filesByCommit.TryGetValue(commit.Hash, out files);
                }

                if (files == null)
                {
                    lines.Add(new PdfLine("The changed files could not be read.", PdfLineStyle.Note));
                }
                else if (files.Count == 0)
                {
                    lines.Add(new PdfLine("No changed files are listed for this commit.", PdfLineStyle.Note));
                }
                else
                {
                    AppendPaths(lines, files);
                }

                lines.Add(new PdfLine(string.Empty));
            }

            return new PdfReport("Version history", lines);
        }

        private static string CommitLine(CommitInfo commit)
        {
            string hash = ShortHash(commit.Hash);
            string date = commit.AuthorDate?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? string.Empty;
            string author = commit.AuthorName ?? string.Empty;
            string subject = commit.Subject ?? string.Empty;
            return hash + "  " + date + "  " + author + "  " + subject;
        }

        private static void AppendPaths(List<PdfLine> lines, IReadOnlyList<string> files)
        {
            var paths = new List<string[]>();
            foreach (string file in files)
            {
                string[] parts = (file ?? string.Empty)
                    .Replace('\\', '/')
                    .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    paths.Add(parts);
                }
            }

            paths.Sort((left, right) =>
            {
                int count = Math.Min(left.Length, right.Length);
                for (int index = 0; index < count; index++)
                {
                    int compared = StringComparer.OrdinalIgnoreCase.Compare(left[index], right[index]);
                    if (compared != 0)
                    {
                        return compared;
                    }
                }

                return left.Length.CompareTo(right.Length);
            });

            string[] previous = Array.Empty<string>();
            foreach (string[] parts in paths)
            {
                int folders = parts.Length - 1;
                int shared = 0;
                while (shared < folders
                    && shared < previous.Length - 1
                    && string.Equals(previous[shared], parts[shared], StringComparison.OrdinalIgnoreCase))
                {
                    shared++;
                }

                for (int index = shared; index < folders; index++)
                {
                    lines.Add(new PdfLine(new string(' ', index * 2) + parts[index]));
                }

                lines.Add(new PdfLine(new string(' ', folders * 2) + parts[parts.Length - 1]));
                previous = parts;
            }
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
