using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.IntegrationTests.Services
{
    public sealed class GitRepositoryInitializerTests
    {
        [Fact]
        public async Task InitializeRunsGitInitInTheWorkspace()
        {
            string workspace = CreateWorkspace();
            try
            {
                var runner = new RecordingRunner();
                var initializer = new GitRepositoryInitializer(runner);

                OperationResult result = await initializer.InitializeAsync(workspace, "git");

                Assert.True(result.Success);
                Assert.Equal(new[] { "init" }, runner.Calls.Single());
                Assert.Equal(workspace, runner.WorkingDirectory);
                Assert.Equal("git", runner.GitExecutablePath);
            }
            finally
            {
                Directory.Delete(workspace);
            }
        }

        [Fact]
        public async Task InitializeRejectsAMissingFolderWithoutRunningGit()
        {
            var runner = new RecordingRunner();
            var initializer = new GitRepositoryInitializer(runner);
            string missing = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            OperationResult result = await initializer.InitializeAsync(missing, "git");

            Assert.False(result.Success);
            Assert.Empty(runner.Calls);
        }

        [Fact]
        public async Task InitializeTurnsAMissingGitExecutableIntoInstallGuidance()
        {
            string workspace = CreateWorkspace();
            try
            {
                var runner = new RecordingRunner
                {
                    Result = new GitProcessResult
                    {
                        ExitCode = 1,
                        StandardError = "The system cannot find the file specified"
                    }
                };
                var initializer = new GitRepositoryInitializer(runner);

                OperationResult result = await initializer.InitializeAsync(workspace, "git");

                Assert.False(result.Success);
                Assert.Equal(
                    GitUserMessages.GitNotFoundBeforeRepository,
                    GitUserMessages.Describe(result));
            }
            finally
            {
                Directory.Delete(workspace);
            }
        }

        [Fact]
        public void ResolvePrefersGitOnThePath()
        {
            string directory = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
            string git = Path.Combine(directory, "git.exe");
            File.WriteAllText(git, string.Empty);
            try
            {
                string resolved = GitExecutableLocator.Resolve(directory, new[] { @"C:\missing\git.exe" });

                Assert.Equal(git, resolved);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void ResolveUsesAKnownInstallWhenGitIsNotOnThePath()
        {
            string directory = Directory.CreateDirectory(
                Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;
            string git = Path.Combine(directory, "git.exe");
            File.WriteAllText(git, string.Empty);
            try
            {
                string resolved = GitExecutableLocator.Resolve(@"C:\missing", new[] { git });

                Assert.Equal(git, resolved);
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private static string CreateWorkspace() =>
            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())).FullName;

        private sealed class RecordingRunner : IGitProcessRunner
        {
            public List<string[]> Calls { get; } = new();

            public string? WorkingDirectory { get; private set; }

            public string? GitExecutablePath { get; private set; }

            public GitProcessResult Result { get; set; } = new() { ExitCode = 0 };

            public Task<GitProcessResult> RunAsync(
                string gitExecutablePath,
                string workingDirectory,
                IReadOnlyList<string> arguments,
                CancellationToken cancellationToken)
            {
                GitExecutablePath = gitExecutablePath;
                WorkingDirectory = workingDirectory;
                Calls.Add(arguments.ToArray());
                return Task.FromResult(Result);
            }
        }
    }
}
