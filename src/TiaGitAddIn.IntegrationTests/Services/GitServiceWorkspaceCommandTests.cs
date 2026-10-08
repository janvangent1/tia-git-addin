using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.IntegrationTests.Services
{
    public sealed class GitServiceWorkspaceCommandTests
    {
        [Fact]
        public async Task DiscardRestoresTheSelectedFilesFromHead()
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.DiscardAsync(new[] { "Blocks/Program.xml" });

            Assert.True(result.Success);
            Assert.Equal(
                new[] { "restore", "--source=HEAD", "--worktree", "--staged", "--", "Blocks/Program.xml" },
                runner.Calls.Single());
        }

        [Fact]
        public async Task SetLocalIdentityWritesNameAndEmailIntoTheRepository()
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.SetLocalIdentityAsync("Ada Lovelace", "ada@example.com");

            Assert.True(result.Success);
            Assert.Equal(new[] { "config", "user.name", "Ada Lovelace" }, runner.Calls[0]);
            Assert.Equal(new[] { "config", "user.email", "ada@example.com" }, runner.Calls[1]);
        }

        [Fact]
        public async Task SetLocalIdentityRejectsADashPrefixedNameWithoutRunningGit()
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.SetLocalIdentityAsync("-n", "ada@example.com");

            Assert.False(result.Success);
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public void ExplainTurnsMissingGitAndMissingIdentityIntoNextSteps()
        {
            Assert.Equal(GitUserMessages.GitNotFound, GitUserMessages.Explain("The system cannot find the file specified"));
            Assert.Equal(
                GitUserMessages.IdentityRequired,
                GitUserMessages.Describe(OperationResult.Fail("Unable to create commit.", "Please tell me who you are.")));
        }

        private sealed class RecordingRunner : IGitProcessRunner
        {
            public List<string[]> Calls { get; } = new();

            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                Calls.Add(arguments.ToArray());
                return Task.FromResult(new GitProcessResult { ExitCode = 0 });
            }
        }
    }
}
