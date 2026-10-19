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
        public async Task SetRemoteUrlAddsOriginWhenTheRemoteIsMissing()
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.SetRemoteUrlAsync(
                "origin",
                "https://dev.azure.com/lonza/plant/_git/plc");

            Assert.True(result.Success);
            Assert.Equal(new[] { "remote", "-v" }, runner.Calls[0]);
            Assert.Equal(
                new[] { "remote", "add", "origin", "https://dev.azure.com/lonza/plant/_git/plc" },
                runner.Calls[1]);
        }

        [Fact]
        public async Task SetRemoteUrlUpdatesAnExistingGitHubRemote()
        {
            var runner = new RecordingRunner
            {
                RemoteListOutput = "origin\thttps://github.com/old/repo.git (fetch)\n"
            };
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.SetRemoteUrlAsync(
                "origin",
                "git@github.com:janvangent1/tia-git-addin.git");

            Assert.True(result.Success);
            Assert.Equal(
                new[] { "remote", "set-url", "origin", "git@github.com:janvangent1/tia-git-addin.git" },
                runner.Calls[1]);
        }

        [Fact]
        public async Task SetRemoteUrlRejectsAPasswordAndDoesNotRunGit()
        {
            var runner = new RecordingRunner();
            var service = new GitService(runner, new OperationSerializer(), "git", @"C:\workspace");

            OperationResult result = await service.SetRemoteUrlAsync(
                "origin",
                "https://user:secret@dev.azure.com/lonza/plant/_git/plc");

            Assert.False(result.Success);
            Assert.Contains("password", result.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Calls);
        }

        [Theory]
        [InlineData("https://github.com/janvangent1/tia-git-addin.git")]
        [InlineData("https://lonza.visualstudio.com/plant/_git/plc")]
        [InlineData("https://tfs.example.com/collection/plant/_git/plc")]
        [InlineData("git@ssh.dev.azure.com:v3/lonza/plant/plc")]
        public void SupportedRepositoryUrlsAreAccepted(string url)
        {
            Assert.Null(GitService.DescribeRemoteUrlProblem(url));
        }

        [Theory]
        [InlineData("https://example.com/repo.git")]
        [InlineData("-o")]
        [InlineData("file:///C:/repo")]
        public void UnsupportedRepositoryUrlsAreRejected(string url)
        {
            Assert.NotNull(GitService.DescribeRemoteUrlProblem(url));
        }

        [Fact]
        public void ExplainTurnsMissingGitAndMissingIdentityIntoNextSteps()
        {
            Assert.Equal(GitUserMessages.GitNotFound, GitUserMessages.Explain("The system cannot find the file specified"));
            Assert.Equal(
                GitUserMessages.IdentityRequired,
                GitUserMessages.Describe(OperationResult.Fail("Unable to create commit.", "Please tell me who you are.")));
            Assert.Equal(
                GitUserMessages.RemoteSignInRequired,
                GitUserMessages.Explain("fatal: Authentication failed for 'https://dev.azure.com/org/project/_git/plc'"));
            Assert.Equal(
                GitUserMessages.RemoteSignInRequired,
                GitUserMessages.Explain("fatal: could not read Username for 'https://github.com': terminal prompts disabled"));
        }

        private sealed class RecordingRunner : IGitProcessRunner
        {
            public List<string[]> Calls { get; } = new();

            public string RemoteListOutput { get; set; } = string.Empty;

            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                Calls.Add(arguments.ToArray());
                string output = arguments.Count >= 2 && arguments[0] == "remote" && arguments[1] == "-v"
                    ? RemoteListOutput
                    : string.Empty;
                return Task.FromResult(new GitProcessResult { ExitCode = 0, StandardOutput = output });
            }
        }
    }
}
