using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.IntegrationTests.Services
{
    public sealed class GitServiceRestoreTests
    {
        [Fact]
        public void ChooseRestoreBranchNameUsesShortHash()
        {
            string name = GitService.ChooseRestoreBranchName("ABCDEF1234567890", new[] { "main" });

            Assert.Equal("restore-abcdef1", name);
        }

        [Fact]
        public void ChooseRestoreBranchNameSkipsExistingLocalNames()
        {
            string name = GitService.ChooseRestoreBranchName(
                "abcdef1234567890",
                new[] { "restore-abcdef1", "restore-abcdef1-2" });

            Assert.Equal("restore-abcdef1-3", name);
        }

        [Fact]
        public void ChooseRestoreBranchNameIgnoresRemoteNameWithTheSameSuffix()
        {
            string name = GitService.ChooseRestoreBranchName(
                "abcdef1234567890",
                new[] { "origin/restore-abcdef1" });

            Assert.Equal("restore-abcdef1", name);
        }

        [Fact]
        public async Task RestoreCommitSwitchesToNewBranchWhenWorkspaceIsClean()
        {
            var runner = new RecordingRunner();
            var service = CreateService(runner);

            var result = await service.RestoreCommitAsync("abcdef1234567890abcdef1234567890abcdef12");

            Assert.True(result.Success);
            Assert.Contains("restore-abcdef1", result.Message);
            string[] switchArgs = runner.Calls.Last();
            Assert.Equal(
                new[] { "switch", "-c", "restore-abcdef1", "abcdef1234567890abcdef1234567890abcdef12" },
                switchArgs);
        }

        [Fact]
        public async Task RestoreCommitRefusesDirtyWorkspace()
        {
            var runner = new RecordingRunner
            {
                StatusOutput = "## main\n M Blocks/Program.xml\n"
            };
            var service = CreateService(runner);

            var result = await service.RestoreCommitAsync("abcdef1234567890abcdef1234567890abcdef12");

            Assert.False(result.Success);
            Assert.Contains("Commit or discard", result.Message);
            Assert.DoesNotContain(runner.Calls, call => call[0] == "switch");
        }

        [Fact]
        public async Task RestoreCommitRejectsInvalidHashWithoutRunningGit()
        {
            var runner = new RecordingRunner();
            var service = CreateService(runner);

            var result = await service.RestoreCommitAsync("HEAD");

            Assert.False(result.Success);
            Assert.Empty(runner.Calls);
        }

        private static GitService CreateService(RecordingRunner runner) =>
            new(runner, new OperationSerializer(), "git", @"C:\workspace");

        private sealed class RecordingRunner : IGitProcessRunner
        {
            public List<string[]> Calls { get; } = new();

            public string StatusOutput { get; set; } = "## main\n";

            public string BranchOutput { get; set; } = " \u001fmain\u001f\u001f\n";

            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                Calls.Add(arguments.ToArray());
                string output = arguments[0] switch
                {
                    "status" => StatusOutput,
                    "branch" => BranchOutput,
                    _ => string.Empty
                };

                return Task.FromResult(new GitProcessResult
                {
                    ExitCode = 0,
                    StandardOutput = output
                });
            }
        }
    }
}
