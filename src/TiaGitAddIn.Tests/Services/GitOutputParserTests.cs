using System.Linq;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.Tests.Services
{
    public sealed class GitOutputParserTests
    {
        [Fact]
        public void ParseStatusMapsPorcelainEntries()
        {
            GitStatus status = GitOutputParser.ParseStatus(
                "## main...origin/main [ahead 1]\n" +
                " M Blocks/Main.scl\n" +
                "A  Tags/Plant.xml\n" +
                "?? New/Device.xml\n" +
                "UU Conflicts/Block.scl\n");

            Assert.Equal("main", status.CurrentBranch);
            Assert.Equal("origin/main", status.TrackingBranch);
            Assert.Equal(1, status.AheadBy);
            Assert.Equal(4, status.Entries.Count);
            Assert.Contains(status.Entries, entry =>
                entry.FilePath == "Blocks/Main.scl" &&
                entry.IndexStatus == FileStatus.Unmodified &&
                entry.WorkTreeStatus == FileStatus.Modified);
            Assert.Contains(status.Entries, entry =>
                entry.FilePath == "Tags/Plant.xml" &&
                entry.IndexStatus == FileStatus.Added &&
                entry.WorkTreeStatus == FileStatus.Unmodified);
            Assert.Contains(status.Entries, entry =>
                entry.FilePath == "New/Device.xml" &&
                entry.IndexStatus == FileStatus.Untracked &&
                entry.WorkTreeStatus == FileStatus.Untracked);
            Assert.True(status.HasConflicts);
        }

        [Fact]
        public void ParseBranchesReadsTabSeparatedBranchFormat()
        {
            var branches = GitOutputParser.ParseBranches(
                "*\tmain\torigin/main\t[ahead 2, behind 1]\n" +
                " \trestore-abc1234\t\t\n" +
                " \torigin/main\t\t\n").ToList();

            Assert.Equal(3, branches.Count);
            Assert.Equal("main", branches[0].Name);
            Assert.True(branches[0].IsCurrent);
            Assert.Equal("origin/main", branches[0].TrackingBranch);
            Assert.Equal(2, branches[0].AheadBy);
            Assert.Equal(1, branches[0].BehindBy);
            Assert.Equal("restore-abc1234", branches[1].Name);
            Assert.False(branches[1].IsCurrent);
            Assert.Null(branches[1].TrackingBranch);
            Assert.Equal("origin/main", branches[2].Name);
            Assert.False(branches[2].IsCurrent);
        }

        [Fact]
        public void ParseCommitLogSkipsMalformedLines()
        {
            var commits = GitOutputParser.ParseCommitLog(
                "abc123\u001fJane Engineer\u001f2026-05-01T12:00:00+00:00\u001fInitial export\u001fdef456\n" +
                "malformed\n").ToList();

            Assert.Single(commits);
            Assert.Equal("abc123", commits[0].Hash);
            Assert.Equal("Jane Engineer", commits[0].AuthorName);
            Assert.Equal("Initial export", commits[0].Subject);
            Assert.Equal("def456", commits[0].ParentHash);
        }

        [Fact]
        public void ParseStatusUnquotesPathsThatContainSpaces()
        {
            GitStatus status = GitOutputParser.ParseStatus(
                "## main\n" +
                " M \"Program blocks/Main.xml\"\n" +
                "?? \"PLC data types/Recipe.udt\"\n" +
                "R  \"old name.xml\" -> \"Program blocks/New name.xml\"\n");

            Assert.Equal(
                new[]
                {
                    "Program blocks/Main.xml",
                    "PLC data types/Recipe.udt",
                    "Program blocks/New name.xml"
                },
                status.Entries.Select(entry => entry.FilePath).ToArray());
            Assert.Equal("old name.xml", status.Entries[2].OriginalPath);
            Assert.Equal(FileStatus.Modified, status.Entries[0].WorkTreeStatus);
            Assert.Equal(FileStatus.Untracked, status.Entries[1].WorkTreeStatus);
        }

        [Fact]
        public void ParseStatusDecodesQuotedUtf8Bytes()
        {
            GitStatus status = GitOutputParser.ParseStatus(" M \"caf\\303\\251.xml\"\n");

            Assert.Equal("café.xml", Assert.Single(status.Entries).FilePath);
        }

        [Fact]
        public void ParseStatusReadsFreshRepositoryBranch()
        {
            GitStatus status = GitOutputParser.ParseStatus("## No commits yet on main\n");

            Assert.Equal("main", status.CurrentBranch);
            Assert.Null(status.TrackingBranch);
        }

        [Fact]
        public void ParseRemotesPreservesUrlsWithSpaces()
        {
            var remotes = GitOutputParser.ParseRemotes(
                "origin\tC:/repos/remote with space (fetch)\n" +
                "origin\tC:/repos/remote with space (push)\n").ToList();

            Assert.Equal(2, remotes.Count);
            Assert.All(remotes, remote => Assert.Equal("origin", remote.Name));
            Assert.All(remotes, remote => Assert.Equal("C:/repos/remote with space", remote.Url));
            Assert.Equal("fetch", remotes[0].Purpose);
            Assert.Equal("push", remotes[1].Purpose);
        }

        [Fact]
        public void ParseDiffAssignsLineNumbersFromHunkHeader()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git a/file.txt b/file.txt\n" +
                "--- a/file.txt\n" +
                "+++ b/file.txt\n" +
                "@@ -1,3 +1,4 @@\n" +
                " context1\n" +
                "-removed\n" +
                "+added1\n" +
                "+added2\n" +
                " context2\n");

            DiffEntry entry = Assert.Single(result.Entries);
            Assert.Equal("file.txt", entry.FilePath);
            Assert.Equal("M", entry.ChangeType);
            DiffHunk hunk = Assert.Single(entry.Hunks);

            // context1 -> old 1 / new 1
            Assert.Equal(1, hunk.Lines[0].OldLineNumber);
            Assert.Equal(1, hunk.Lines[0].NewLineNumber);

            // removed -> old 2 / new (none)
            Assert.Equal(2, hunk.Lines[1].OldLineNumber);
            Assert.Null(hunk.Lines[1].NewLineNumber);

            // added1 -> old (none) / new 2
            Assert.Null(hunk.Lines[2].OldLineNumber);
            Assert.Equal(2, hunk.Lines[2].NewLineNumber);

            // added2 -> new 3
            Assert.Equal(3, hunk.Lines[3].NewLineNumber);

            // context2 -> old 3 / new 4
            Assert.Equal(3, hunk.Lines[4].OldLineNumber);
            Assert.Equal(4, hunk.Lines[4].NewLineNumber);
        }

        [Fact]
        public void ParseDiffMarksNewFileAsAdded()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git a/New.txt b/New.txt\n" +
                "new file mode 100644\n" +
                "index 0000000..abcd123\n" +
                "--- /dev/null\n" +
                "+++ b/New.txt\n" +
                "@@ -0,0 +1,2 @@\n" +
                "+line1\n" +
                "+line2\n");

            DiffEntry entry = Assert.Single(result.Entries);
            Assert.Equal("New.txt", entry.FilePath);
            Assert.Equal("A", entry.ChangeType);
        }

        [Fact]
        public void ParseDiffMarksRemovedFileAsDeleted()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git a/Old.txt b/Old.txt\n" +
                "deleted file mode 100644\n" +
                "index abcd123..0000000\n" +
                "--- a/Old.txt\n" +
                "+++ /dev/null\n" +
                "@@ -1,2 +0,0 @@\n" +
                "-line1\n" +
                "-line2\n");

            DiffEntry entry = Assert.Single(result.Entries);
            Assert.Equal("Old.txt", entry.FilePath);
            Assert.Equal("D", entry.ChangeType);
        }

        [Fact]
        public void ParseDiffReadsAQuotedPathWithSpaces()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git \"a/PLC/Software units/Unit_600_Motion/Program blocks/FB Jog.xml\" \"b/PLC/Software units/Unit_600_Motion/Program blocks/FB Jog.xml\"\n" +
                "--- \"a/PLC/Software units/Unit_600_Motion/Program blocks/FB Jog.xml\"\n" +
                "+++ \"b/PLC/Software units/Unit_600_Motion/Program blocks/FB Jog.xml\"\n");

            Assert.Equal(
                "PLC/Software units/Unit_600_Motion/Program blocks/FB Jog.xml",
                Assert.Single(result.Entries).FilePath);
        }

        [Fact]
        public void ParseDiffDecodesQuotedUtf8BytesInThePath()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git \"a/PLC/Software units/Unit_100_Library/Program blocks/caf\\303\\251.xml\" \"b/PLC/Software units/Unit_100_Library/Program blocks/caf\\303\\251.xml\"\n");

            Assert.Equal(
                "PLC/Software units/Unit_100_Library/Program blocks/café.xml",
                Assert.Single(result.Entries).FilePath);
        }

        [Fact]
        public void ParseDiffKeepsAnUnquotedPathThatContainsSpaces()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git a/PLC/Program blocks/Main.xml b/PLC/Program blocks/Main.xml\n");

            Assert.Equal("PLC/Program blocks/Main.xml", Assert.Single(result.Entries).FilePath);
        }

        [Fact]
        public void ParseDiffUsesTheNewNameAfterARename()
        {
            DiffResult result = GitOutputParser.ParseDiff(
                "diff --git \"a/old name.xml\" \"b/PLC/Program blocks/New name.xml\"\n" +
                "similarity index 90%\n" +
                "rename from old name.xml\n" +
                "rename to PLC/Program blocks/New name.xml\n");

            Assert.Equal("PLC/Program blocks/New name.xml", Assert.Single(result.Entries).FilePath);
        }
    }
}
