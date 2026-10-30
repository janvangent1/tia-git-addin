using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services
{
    public static class GitOutputParser
    {
        private const char FieldSeparator = '\u001f';

        public static GitStatus ParseStatus(string output)
        {
            string[] lines = SplitLines(output);
            GitStatus status = new()

            {
                Entries = lines
                    .Where(line => !line.StartsWith("##", StringComparison.Ordinal))
                    .Where(line => line.Length >= 3)
                    .Select(ParseStatusEntry)
                    .ToList()
            };

            string? branchLine = lines.FirstOrDefault(line => line.StartsWith("##", StringComparison.Ordinal));
            if (branchLine != null)
            {
                ApplyBranchStatus(status, branchLine);
            }

            return status;
        }

        public static IEnumerable<CommitInfo> ParseCommitLog(string output)
        {
            foreach (string line in SplitLines(output))
            {
                if (TryParseCommitLine(line, out CommitInfo? commit) && commit != null)
                {
                    yield return commit;
                }
            }
        }

        /// <summary>
        /// Reads <c>git log --name-status</c> output. A rename keeps the name the file had in that commit.
        /// </summary>
        public static List<FileRevision> ParseFileHistory(string output)
        {
            var revisions = new List<FileRevision>();
            List<string>? paths = null;
            foreach (string line in SplitLines(output ?? string.Empty))
            {
                if (TryParseCommitLine(line, out CommitInfo? commit) && commit != null)
                {
                    paths = new List<string>();
                    revisions.Add(new FileRevision { Commit = commit, Paths = paths });
                    continue;
                }

                if (paths != null && TryParseNameStatusPath(line, out string path))
                {
                    paths.Add(path);
                }
            }

            return revisions;
        }

        private static bool TryParseCommitLine(string line, out CommitInfo? commit)
        {
            commit = null;
            string[] fields = line.Split(FieldSeparator);
            if (fields.Length < 4 || fields[0].IndexOf('\t') >= 0)
            {
                return false;
            }

            DateTimeOffset? authorDate = DateTimeOffset.TryParse(
                fields[2],
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset parsedDate)
                ? parsedDate
                : null;

            commit = new CommitInfo
            {
                Hash = fields[0],
                AuthorName = fields[1],
                AuthorDate = authorDate,
                Subject = fields[3],
                ParentHash = fields.Length > 4 ? fields[4] : null
            };
            return true;
        }

        private static bool TryParseNameStatusPath(string line, out string path)
        {
            path = string.Empty;
            int tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                return false;
            }

            string status = line.Substring(0, tab);
            if (status.Length == 0 || "ACDMRTUXB".IndexOf(status[0]) < 0)
            {
                return false;
            }

            for (int index = 1; index < status.Length; index++)
            {
                if (!char.IsDigit(status[index]))
                {
                    return false;
                }
            }

            string rest = line.Substring(tab + 1);
            int lastTab = rest.LastIndexOf('\t');
            string raw = lastTab >= 0 ? rest.Substring(lastTab + 1) : rest;
            path = UnquotePorcelainPath(raw.Trim());
            return path.Length > 0;
        }

        public static IEnumerable<BranchInfo> ParseBranches(string output)
        {
            foreach (string line in SplitLines(output))
            {
                string[] fields = line.Split('\t');
                if (fields.Length < 2)
                {
                    continue;
                }

                string name = fields[1];
                string? tracking = fields.Length > 2 && fields[2].Length > 0 ? fields[2] : null;
                int ahead = 0;
                int behind = 0;

                if (fields.Length > 3)
                {
                    string ab = fields[3];
                    Match aheadMatch = Regex.Match(ab, @"ahead (\d+)");
                    if (aheadMatch.Success) ahead = int.Parse(aheadMatch.Groups[1].Value);
                    Match behindMatch = Regex.Match(ab, @"behind (\d+)");
                    if (behindMatch.Success) behind = int.Parse(behindMatch.Groups[1].Value);
                }

                yield return new BranchInfo
                {
                    Name = name,
                    IsCurrent = fields[0] == "*",
                    TrackingBranch = tracking,
                    AheadBy = ahead,
                    BehindBy = behind
                };
            }
        }

        public static IEnumerable<RemoteInfo> ParseRemotes(string output)
        {
            foreach (string line in SplitLines(output))
            {
                int separatorIndex = line.IndexOf('\t');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string name = line.Substring(0, separatorIndex);
                string remoteWithPurpose = line.Substring(separatorIndex + 1);
                int purposeIndex = remoteWithPurpose.LastIndexOf(" (", StringComparison.Ordinal);
                if (purposeIndex <= 0 || !remoteWithPurpose.EndsWith(")", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return new RemoteInfo
                {
                    Name = name,
                    Url = remoteWithPurpose.Substring(0, purposeIndex),
                    Purpose = remoteWithPurpose.Substring(
                        purposeIndex + 2,
                        remoteWithPurpose.Length - purposeIndex - 3)
                };
            }
        }

        public static DiffResult ParseDiff(string output)
        {
            List<DiffEntry> entries = new();
            if (string.IsNullOrWhiteSpace(output)) return new DiffResult { Entries = entries };

            string[] lines = SplitLines(output);
            DiffEntry? currentEntry = null;
            DiffHunk? currentHunk = null;
            int oldLineNumber = 0;
            int newLineNumber = 0;

            foreach (string line in lines)
            {
                if (line.StartsWith("diff --git", StringComparison.Ordinal))
                {
                    currentEntry = new DiffEntry { ChangeType = "M" };
                    entries.Add(currentEntry);
                    currentHunk = null;

                    if (TryParseDiffGitPath(line, out string path))
                    {
                        currentEntry.FilePath = path;
                    }
                }
                else if (currentEntry != null && line.StartsWith("new file mode", StringComparison.Ordinal))
                {
                    currentEntry.ChangeType = "A";
                }
                else if (currentEntry != null && line.StartsWith("deleted file mode", StringComparison.Ordinal))
                {
                    currentEntry.ChangeType = "D";
                }
                else if (currentEntry != null && line.StartsWith("--- ", StringComparison.Ordinal))
                {
                    // Ignore old file path line
                }
                else if (currentEntry != null && line.StartsWith("+++ ", StringComparison.Ordinal))
                {
                    // Ignore new file path line
                }
                else if (currentEntry != null && line.StartsWith("@@ ", StringComparison.Ordinal))
                {
                    currentHunk = ParseHunkHeader(line);
                    List<DiffHunk> hunks = (List<DiffHunk>)currentEntry.Hunks;
                    hunks.Add(currentHunk);
                    oldLineNumber = currentHunk.OldStart;
                    newLineNumber = currentHunk.NewStart;
                }
                else if (currentHunk != null)
                {
                    List<DiffLine> hunkLines = (List<DiffLine>)currentHunk.Lines;

                    if (line.StartsWith("+", StringComparison.Ordinal))
                    {
                        hunkLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Added,
                            Content = line.Substring(1),
                            NewLineNumber = newLineNumber++
                        });
                    }
                    else if (line.StartsWith("-", StringComparison.Ordinal))
                    {
                        hunkLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Deleted,
                            Content = line.Substring(1),
                            OldLineNumber = oldLineNumber++
                        });
                    }
                    else
                    {
                        hunkLines.Add(new DiffLine
                        {
                            Type = DiffLineType.Context,
                            Content = line.Length > 0 ? line.Substring(1) : line,
                            OldLineNumber = oldLineNumber++,
                            NewLineNumber = newLineNumber++
                        });
                    }
                }
            }

            return new DiffResult { Entries = entries };
        }

        private static DiffHunk ParseHunkHeader(string line)
        {
            // @@ -1,4 +1,4 @@
            Match match = Regex.Match(line, @"@@ -(\d+),?(\d*) \+(\d+),?(\d*) @@");
            if (match.Success)
            {
                return new DiffHunk
                {
                    Header = line,
                    OldStart = int.Parse(match.Groups[1].Value),
                    OldCount = string.IsNullOrEmpty(match.Groups[2].Value) ? 1 : int.Parse(match.Groups[2].Value),
                    NewStart = int.Parse(match.Groups[3].Value),
                    NewCount = string.IsNullOrEmpty(match.Groups[4].Value) ? 1 : int.Parse(match.Groups[4].Value),
                    Lines = new List<DiffLine>()
                };
            }
            return new DiffHunk { Header = line, Lines = new List<DiffLine>() };
        }

        public static List<string> ParseDiffTree(string output)
        {
            return SplitLines(output)
                .Select(line => UnquotePorcelainPath(line.Split('\t').Last()))
                .ToList();
        }

        /// <summary>
        /// Reads the new-side path from <c>diff --git</c>. Git quotes a path that contains a space,
        /// a quote, or a non-ASCII byte, and the quotes are not part of the workspace path.
        /// </summary>
        private static bool TryParseDiffGitPath(string line, out string path)
        {
            const string prefix = "diff --git ";
            path = string.Empty;
            if (!line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string rest = line.Substring(prefix.Length);
            string newSide;
            if (rest.StartsWith("\"", StringComparison.Ordinal))
            {
                if (!TryReadQuotedToken(rest, out string first, out int consumed))
                {
                    return false;
                }

                string afterFirst = rest.Substring(consumed);
                if (afterFirst.Length == 0 || afterFirst[0] != ' ')
                {
                    return false;
                }

                if (!TryReadQuotedToken(afterFirst.Substring(1), out string second, out _))
                {
                    return false;
                }

                newSide = UnquotePorcelainPath(second);
            }
            else
            {
                int split = rest.LastIndexOf(" b/", StringComparison.Ordinal);
                if (split < 2 || !rest.StartsWith("a/", StringComparison.Ordinal))
                {
                    return false;
                }

                newSide = rest.Substring(split + 1);
            }

            if (newSide.StartsWith("b/", StringComparison.Ordinal))
            {
                newSide = newSide.Substring(2);
            }

            if (newSide.Length == 0 || newSide.Equals("/dev/null", StringComparison.Ordinal))
            {
                return false;
            }

            path = newSide;
            return true;
        }

        private static bool TryReadQuotedToken(string value, out string token, out int consumed)
        {
            token = string.Empty;
            consumed = 0;
            if (value.Length == 0 || value[0] != '"')
            {
                return false;
            }

            for (int index = 1; index < value.Length; index++)
            {
                if (value[index] == '\\')
                {
                    index++;
                    continue;
                }

                if (value[index] == '"')
                {
                    consumed = index + 1;
                    token = value.Substring(0, consumed);
                    return true;
                }
            }

            return false;
        }

        private static FileStatusEntry ParseStatusEntry(string line)
        {
            char index = line[0];
            char workTree = line[1];
            SplitPorcelainPath(line.Substring(3), out string path, out string? originalPath);

            return new FileStatusEntry
            {
                FilePath = path,
                OriginalPath = originalPath,
                IndexStatus = MapStatus(index, workTree),
                WorkTreeStatus = MapStatus(workTree, index)
            };
        }

        private static FileStatus MapStatus(char status, char pairedStatus)
        {
            if (status == 'U' || IsConflictPair(status, pairedStatus))
            {
                return FileStatus.Conflicted;
            }

            switch (status)
            {
                case ' ':
                    return FileStatus.Unmodified;
                case 'M':
                    return FileStatus.Modified;
                case 'A':
                    return FileStatus.Added;
                case 'D':
                    return FileStatus.Deleted;
                case 'R':
                    return FileStatus.Renamed;
                case 'C':
                    return FileStatus.Copied;
                case '?':
                    return FileStatus.Untracked;
                case '!':
                    return FileStatus.Ignored;
                default:
                    return FileStatus.Unmodified;
            }
        }

        private static bool IsConflictPair(char status, char pairedStatus) =>
            (status == 'A' && pairedStatus == 'A') ||
            (status == 'D' && pairedStatus == 'D');

        private static void ApplyBranchStatus(GitStatus status, string line)
        {
            string value = line.Substring(3);
            int trackingIndex = value.IndexOf("...", StringComparison.Ordinal);
            if (trackingIndex < 0)
            {
                status.CurrentBranch = ParseUntrackedBranchName(value);
                return;
            }

            status.CurrentBranch = value.Substring(0, trackingIndex);
            string tracking = value.Substring(trackingIndex + 3);
            int metadataIndex = tracking.IndexOf(" [", StringComparison.Ordinal);
            if (metadataIndex >= 0)
            {
                string metadata = tracking.Substring(metadataIndex + 2).TrimEnd(']');
                tracking = tracking.Substring(0, metadataIndex);
                ApplyAheadBehind(status, metadata);
            }

            status.TrackingBranch = tracking;
        }

        private static string ParseUntrackedBranchName(string value)
        {
            const string noCommitsPrefix = "No commits yet on ";
            return value.StartsWith(noCommitsPrefix, StringComparison.Ordinal)
                ? value.Substring(noCommitsPrefix.Length)
                : value;
        }

        private static void ApplyAheadBehind(GitStatus status, string metadata)
        {
            foreach (string part in metadata.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("ahead ", StringComparison.Ordinal) &&
                    int.TryParse(trimmed.Substring(6), out int ahead))
                {
                    status.AheadBy = ahead;
                }
                else if (trimmed.StartsWith("behind ", StringComparison.Ordinal) &&
                    int.TryParse(trimmed.Substring(7), out int behind))
                {
                    status.BehindBy = behind;
                }
            }
        }

        private static string[] SplitLines(string output) =>
            output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

        // git status --porcelain quotes a path that contains a space or a non-ASCII byte.
        // The quotes and C-style escapes are syntax, not part of the workspace path.
        private static void SplitPorcelainPath(string rawPath, out string path, out string? originalPath)
        {
            int separator = IndexOfUnquoted(rawPath, " -> ");
            if (separator < 0)
            {
                originalPath = null;
                path = UnquotePorcelainPath(rawPath);
                return;
            }

            originalPath = UnquotePorcelainPath(rawPath.Substring(0, separator));
            path = UnquotePorcelainPath(rawPath.Substring(separator + 4));
        }

        private static int IndexOfUnquoted(string value, string token)
        {
            bool quoted = false;
            for (int index = 0; index <= value.Length - token.Length; index++)
            {
                char current = value[index];
                if (current == '\\' && quoted)
                {
                    index++;
                    continue;
                }

                if (current == '"')
                {
                    quoted = !quoted;
                    continue;
                }

                if (!quoted && string.CompareOrdinal(value, index, token, 0, token.Length) == 0)
                {
                    return index;
                }
            }

            return -1;
        }

        private static string UnquotePorcelainPath(string path)
        {
            if (path.Length < 2 || path[0] != '"' || path[path.Length - 1] != '"')
            {
                return path;
            }

            List<byte> bytes = new();
            for (int index = 1; index < path.Length - 1; index++)
            {
                char current = path[index];
                if (current != '\\')
                {
                    bytes.Add((byte)current);
                    continue;
                }

                if (index + 1 >= path.Length - 1)
                {
                    break;
                }

                char escaped = path[++index];
                switch (escaped)
                {
                    case 'a':
                        bytes.Add((byte)'\a');
                        break;
                    case 'b':
                        bytes.Add((byte)'\b');
                        break;
                    case 't':
                        bytes.Add((byte)'\t');
                        break;
                    case 'n':
                        bytes.Add((byte)'\n');
                        break;
                    case 'v':
                        bytes.Add((byte)'\v');
                        break;
                    case 'f':
                        bytes.Add((byte)'\f');
                        break;
                    case 'r':
                        bytes.Add((byte)'\r');
                        break;
                    case '\\':
                        bytes.Add((byte)'\\');
                        break;
                    case '"':
                        bytes.Add((byte)'"');
                        break;
                    default:
                        if (escaped is >= '0' and <= '7')
                        {
                            int value = escaped - '0';
                            for (int extra = 0; extra < 2 && index + 1 < path.Length - 1; extra++)
                            {
                                char digit = path[index + 1];
                                if (digit is < '0' or > '7')
                                {
                                    break;
                                }

                                value = (value << 3) + (digit - '0');
                                index++;
                            }

                            bytes.Add((byte)value);
                        }
                        else
                        {
                            bytes.Add((byte)escaped);
                        }

                        break;
                }
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
