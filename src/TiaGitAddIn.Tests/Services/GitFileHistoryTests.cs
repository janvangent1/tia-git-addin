using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.Tests.Services
{
    public sealed class GitFileHistoryTests
    {
        [Fact]
        public void FileHistoryKeepsTheNameTheFileHadInEachCommit()
        {
            string output =
                "aaaaaaa\u001fAda\u001f2026-10-28T12:00:00+01:00\u001fRename the block\u001fbbbbbbb\n" +
                "R100\told/Block.xml\tPLC/Block.xml\n" +
                "bbbbbbb\u001fAda\u001f2026-10-27T12:00:00+01:00\u001fAdd the block\u001f\n" +
                "A\told/Block.xml\n";

            List<FileRevision> history = GitOutputParser.ParseFileHistory(output);

            Assert.Equal(2, history.Count);
            Assert.Equal("PLC/Block.xml", Assert.Single(history[0].Paths));
            Assert.Equal("Rename the block", history[0].Commit.Subject);
            Assert.Equal("old/Block.xml", Assert.Single(history[1].Paths));
        }

        [Fact]
        public async Task OneFileFollowsRenamesAndSeveralFilesDoNot()
        {
            var runner = new RecordingRunner();
            runner.Results.Enqueue(new GitProcessResult { ExitCode = 0 });
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\repo");

            await service.GetFileHistoryAsync(new[] { "PLC/old.txt" }, 10);

            Assert.Contains("--follow", runner.Calls[0]);
            Assert.Equal("PLC/old.txt", runner.Calls[0][runner.Calls[0].Length - 1]);

            runner.Calls.Clear();
            runner.Results.Enqueue(new GitProcessResult { ExitCode = 0 });
            await service.GetFileHistoryAsync(new[] { "a.txt", "b.txt" }, 10);

            Assert.DoesNotContain("--follow", runner.Calls[0]);
            Assert.Contains("a.txt", runner.Calls[0]);
            Assert.Contains("b.txt", runner.Calls[0]);
        }

        [Fact]
        public async Task CommitDiffCanBeLimitedToTheRequestedPaths()
        {
            var runner = new RecordingRunner();
            runner.Results.Enqueue(new GitProcessResult
            {
                ExitCode = 0,
                StandardOutput = "diff --git a/PLC/Block.xml b/PLC/Block.xml\n"
            });
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\repo");

            DiffResult diff = await service.GetCommitDiffAsync("abc1234", new[] { "PLC/Block.xml" });

            string[] call = runner.Calls[0];
            Assert.Equal("diff-tree", call[0]);
            Assert.Contains("--", call);
            Assert.Equal("PLC/Block.xml", call[call.Length - 1]);
            Assert.Equal("PLC/Block.xml", diff.Entries[0].FilePath);
        }

        [Theory]
        [InlineData("")]
        [InlineData("../secret.txt")]
        [InlineData("PLC/../../secret.txt")]
        [InlineData("C:/outside.txt")]
        public async Task FileHistoryRejectsPathsThatLeaveTheRepository(string path)
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\repo");

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.GetFileHistoryAsync(path.Length == 0 ? Array.Empty<string>() : new[] { path }, 10));
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task FileHistoryFollowsARenameAndKeepsTheOlderName()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-history-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var runner = new LocalGitRunner();
                Run(runner, root, "init");
                File.WriteAllText(Path.Combine(root, "old.txt"), "one\n");
                Run(runner, root, "add", "--", "old.txt");
                Run(runner, root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-m", "first");
                Run(runner, root, "mv", "old.txt", "new.txt");
                File.AppendAllText(Path.Combine(root, "new.txt"), "two\n");
                Run(runner, root, "add", "-A");
                Run(runner, root, "-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false", "commit", "-m", "rename");

                var service = new GitService(runner, new OperationSerializer(), GitExecutable(), root);
                IReadOnlyList<FileRevision> history = await service.GetFileHistoryAsync(new[] { "new.txt" }, 20);

                Assert.True(history.Count >= 2, "Expected the rename commit and the commit under the old name.");
                Assert.Contains("new.txt", history[0].Paths);
                Assert.Contains(history, revision => revision.Paths.Contains("old.txt"));

                FileRevision older = history.First(revision => revision.Paths.Contains("old.txt"));
                DiffResult diff = await service.GetCommitDiffAsync(older.Commit.Hash, older.Paths);
                Assert.Contains(diff.Entries, entry => entry.FilePath == "old.txt");
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    foreach (string file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(root, true);
                }
            }
        }

        private static void Run(LocalGitRunner runner, string root, params string[] arguments)
        {
            GitProcessResult result = runner.RunAsync(GitExecutable(), root, arguments, CancellationToken.None).GetAwaiter().GetResult();
            Assert.True(result.IsSuccess, result.StandardError + result.StandardOutput);
        }

        private static string GitExecutable()
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string candidate = Path.Combine(programFiles, "Git", "cmd", "git.exe");
            return File.Exists(candidate) ? candidate : "git";
        }

        private sealed class RecordingRunner : IGitProcessRunner
        {
            public List<string[]> Calls { get; } = new List<string[]>();

            public Queue<GitProcessResult> Results { get; } = new Queue<GitProcessResult>();

            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                Calls.Add(arguments.ToArray());
                return Task.FromResult(Results.Count == 0
                    ? new GitProcessResult { ExitCode = 0 }
                    : Results.Dequeue());
            }
        }

        private sealed class LocalGitRunner : IGitProcessRunner
        {
            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                var start = new ProcessStartInfo
                {
                    FileName = gitExecutablePath,
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Arguments = string.Join(" ", arguments.Select(Quote))
                };
                using var process = Process.Start(start);
                if (process == null)
                {
                    return Task.FromResult(new GitProcessResult { ExitCode = -1, StandardError = "git did not start." });
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return Task.FromResult(new GitProcessResult
                {
                    ExitCode = process.ExitCode,
                    StandardOutput = output,
                    StandardError = error
                });
            }

            private static string Quote(string value)
            {
                if (value.Length == 0)
                {
                    return "\"\"";
                }

                if (value.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
                {
                    return value;
                }

                return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            }
        }
    }
}
