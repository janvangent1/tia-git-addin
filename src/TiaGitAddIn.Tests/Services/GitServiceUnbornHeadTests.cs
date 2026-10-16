using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.Tests.Services
{
    public sealed class GitServiceUnbornHeadTests
    {
        [Fact]
        public async Task WorkingTreeDiffUsesTheEmptyTreeWhenHeadDoesNotExist()
        {
            var runner = new RecordingRunner();
            runner.Results.Enqueue(new GitProcessResult { ExitCode = 1 });
            runner.Results.Enqueue(new GitProcessResult
            {
                ExitCode = 0,
                StandardOutput = "diff --git a/block.xml b/block.xml\n"
            });
            var service = CreateService(runner);

            await service.GetWorkingTreeDiffAsync();

            Assert.Equal(new[] { "rev-parse", "--verify", "--quiet", "HEAD" }, runner.Calls[0]);
            Assert.Equal("4b825dc642cb6eb9a060e54bf8d69288fbee4904", runner.Calls[1][1]);
        }

        [Fact]
        public async Task WorkingTreeDiffUsesHeadAfterTheFirstCommit()
        {
            var runner = new RecordingRunner();
            runner.Results.Enqueue(new GitProcessResult { ExitCode = 0, StandardOutput = "abc" });
            runner.Results.Enqueue(new GitProcessResult { ExitCode = 0 });
            var service = CreateService(runner);

            await service.GetWorkingTreeDiffAsync();

            Assert.Equal("HEAD", runner.Calls[1][1]);
        }

        [Fact]
        public async Task CommitLogIsEmptyWhenTheRepositoryHasNoCommits()
        {
            var runner = new RecordingRunner();
            runner.Results.Enqueue(new GitProcessResult
            {
                ExitCode = 128,
                StandardError = "fatal: your current branch 'master' does not have any commits yet"
            });
            var service = CreateService(runner);

            IReadOnlyList<CommitInfo> log = await service.GetCommitLogAsync(20);

            Assert.Empty(log);
        }

        private static GitService CreateService(RecordingRunner runner)
        {
            return new GitService(runner, new OperationSerializer(), "git", @"C:\repo");
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
                return Task.FromResult(Results.Dequeue());
            }
        }
    }
}
