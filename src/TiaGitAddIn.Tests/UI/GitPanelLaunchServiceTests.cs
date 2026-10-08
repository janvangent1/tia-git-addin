using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class GitPanelLaunchServiceTests
    {
        [Fact]
        public void CreateViewModelBuildsPanelForDiscoveredRepository()
        {
            var gitService = new FakeGitService();
            var configService = new FakeConfigurationService();
            var locator = new FakeWorkspaceLocator();
            var discovery = new FakeRepositoryDiscovery();
            var logger = new FakeLogger();
            
            var service = new GitPanelLaunchService(
                locator,
                discovery,
                configService,
                (p1, p2) => gitService,
                logger,
                new UnusedInitializer(),
                new UnusedPrompt(),
                UnusedGitExecutable);

            GitPanelLaunchResult result = service.CreateViewModel(new object());

            Assert.True(result.Success);
            Assert.NotNull(result.CreateViewModel);
            MainViewModel viewModel = result.CreateViewModel();
            Assert.Equal("C:\\repo", viewModel.RepositoryPath);
        }

        [Fact]
        public void CreateViewModel_WhenWorkspaceHasNoRepository_CreatesOneAfterConfirmation()
        {
            var gitService = new FakeGitService();
            var configService = new FakeConfigurationService();
            var locator = new FakeWorkspaceLocator();
            var discovery = new FakeRepositoryDiscovery { Root = null };
            var logger = new FakeLogger();
            var initializer = new RecordingInitializer();
            var prompt = new RecordingPrompt { Response = true };

            var service = new GitPanelLaunchService(
                locator,
                discovery,
                configService,
                (p1, p2) => gitService,
                logger,
                initializer,
                prompt,
                () => @"C:\Program Files\Git\cmd\git.exe");

            GitPanelLaunchResult result = service.CreateViewModel(new object());

            Assert.True(result.Success);
            Assert.Equal(1, prompt.Calls);
            Assert.Equal(1, initializer.Calls);
            Assert.Equal("C:\\repo\\vci", initializer.WorkspacePath);
            Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", initializer.GitExecutablePath);
            MainViewModel viewModel = result.CreateViewModel!();
            Assert.Equal("C:\\repo\\vci", viewModel.RepositoryPath);
            Assert.Equal("C:\\repo\\vci", configService.SavedRepositoryPath);
            Assert.Equal(@"C:\Program Files\Git\cmd\git.exe", configService.SavedGitExecutablePath);
        }

        [Fact]
        public void CreateViewModel_WhenInitializationIsDeclined_DoesNotCreateARepository()
        {
            var discovery = new FakeRepositoryDiscovery { Root = null };
            var initializer = new RecordingInitializer();
            var prompt = new RecordingPrompt { Response = false };
            var service = new GitPanelLaunchService(
                new FakeWorkspaceLocator(),
                discovery,
                new FakeConfigurationService(),
                (p1, p2) => new FakeGitService(),
                new FakeLogger(),
                initializer,
                prompt,
                UnusedGitExecutable);

            GitPanelLaunchResult result = service.CreateViewModel(new object());

            Assert.False(result.Success);
            Assert.Equal(GitUserMessages.RepositoryInitDeclined, result.Message);
            Assert.Equal(0, initializer.Calls);
        }

        [Fact]
        public void CreateViewModel_WhenInitializationFails_ReturnsTheInitializerMessage()
        {
            var initializer = new RecordingInitializer
            {
                Result = OperationResult.Fail(GitUserMessages.GitNotFoundBeforeRepository)
            };
            var service = new GitPanelLaunchService(
                new FakeWorkspaceLocator(),
                new FakeRepositoryDiscovery { Root = null },
                new FakeConfigurationService(),
                (p1, p2) => new FakeGitService(),
                new FakeLogger(),
                initializer,
                new RecordingPrompt { Response = true },
                () => "git");

            GitPanelLaunchResult result = service.CreateViewModel(new object());

            Assert.False(result.Success);
            Assert.Equal(GitUserMessages.GitNotFoundBeforeRepository, result.Message);
        }

        [Fact]
        public void CreateViewModel_LogsTheSiemensGitProcessRunnerAdapterAndNoTestOrSystemRunner()
        {
            var gitService = new FakeGitService();
            var configService = new FakeConfigurationService();
            var locator = new FakeWorkspaceLocator();
            var discovery = new FakeRepositoryDiscovery();
            var logger = new FakeLogger();

            var service = new GitPanelLaunchService(
                locator,
                discovery,
                configService,
                (p1, p2) => gitService,
                logger,
                new UnusedInitializer(),
                new UnusedPrompt(),
                UnusedGitExecutable);

            GitPanelLaunchResult result = service.CreateViewModel(new object());

            Assert.True(result.Success);
            Assert.Contains(logger.InfoMessages, m => m.Contains("GitProcessRunner") && m.Contains("SiemensAddIn"));
            Assert.DoesNotContain(logger.InfoMessages, m => m.Contains("Test") || m.Contains("System.Diagnostics.Process"));
        }

        private sealed class FakeWorkspaceLocator : IVciWorkspaceLocator
        {
            public string? TryGetWorkspacePath(object context) => "C:\\repo\\vci";
            public bool IsVciWorkspaceValid(string path) => true;
        }

        private sealed class FakeRepositoryDiscovery : IRepositoryDiscovery
        {
            public string? Root { get; set; } = "C:\\repo";

            public string? FindRepositoryRoot(string startPath) => Root;
        }

        private sealed class FakeConfigurationService : IConfigurationService
        {
            public string? SavedRepositoryPath { get; private set; }

            public string? SavedGitExecutablePath { get; private set; }

            public GitConfiguration Load(string workspacePath) => new GitConfiguration { RepositoryPath = workspacePath };

            public void Save(string workspacePath, GitConfiguration config)
            {
                SavedRepositoryPath = workspacePath;
                SavedGitExecutablePath = config.GitExecutablePath;
            }

            public string GetConfigFilePath(string workspacePath) => string.Empty;
        }

        private sealed class RecordingPrompt : IRepositoryInitPrompt
        {
            public bool Response { get; set; }

            public int Calls { get; private set; }

            public bool Confirm(string workspacePath)
            {
                Calls++;
                return Response;
            }
        }

        private sealed class UnusedPrompt : IRepositoryInitPrompt
        {
            public bool Confirm(string workspacePath) =>
                throw new InvalidOperationException("A repository prompt is not expected when a repository already exists.");
        }

        private sealed class RecordingInitializer : IGitRepositoryInitializer
        {
            public int Calls { get; private set; }

            public string? WorkspacePath { get; private set; }

            public string? GitExecutablePath { get; private set; }

            public OperationResult Result { get; set; } = OperationResult.Ok("Git repository created.");

            public Task<OperationResult> InitializeAsync(
                string workspacePath,
                string gitExecutablePath,
                CancellationToken ct = default)
            {
                Calls++;
                WorkspacePath = workspacePath;
                GitExecutablePath = gitExecutablePath;
                return Task.FromResult(Result);
            }
        }

        private static string UnusedGitExecutable() =>
            throw new InvalidOperationException("Git does not need to be located when a repository already exists.");

        private sealed class UnusedInitializer : IGitRepositoryInitializer
        {
            public Task<OperationResult> InitializeAsync(
                string workspacePath,
                string gitExecutablePath,
                CancellationToken ct = default) =>
                throw new InvalidOperationException("Repository initialization is not expected when a repository already exists.");
        }

        private sealed class FakeLogger : IAddInLogger
        {
            public List<string> InfoMessages { get; } = new();

            public void Info(string message) => InfoMessages.Add(message);
            public void Warn(string message) { }
            public void Error(string message, Exception? ex = null) { }
            public void Debug(string message) { }
        }

        private sealed class FakeGitService : IGitService
        {
            public Task<GitStatus> GetStatusAsync(CancellationToken ct = default) =>
                Task.FromResult(new GitStatus());

            public Task<OperationResult> StageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("File staged."));

            public Task<OperationResult> UnstageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("File unstaged."));

            public Task<OperationResult> StageAllAsync(CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("All changes staged."));

            public Task<OperationResult> CommitAsync(string message, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Commit created."));

            public Task<OperationResult> FetchAsync(string? remote = null, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Fetch completed."));

            public Task<OperationResult> PullAsync(string? remote = null, string? branch = null, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Pull completed."));

            public Task<OperationResult> PushAsync(string? remote = null, string? branch = null, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Push completed."));

            public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<BranchInfo>>(new List<BranchInfo>());

            public Task<OperationResult> CreateBranchAsync(string name, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Branch created."));

            public Task<OperationResult> SwitchBranchAsync(string name, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Branch switched."));

            public Task<OperationResult> CheckoutBranchAsync(string branchName, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Branch checked out."));

            public Task<OperationResult> RestoreCommitAsync(string commitHash, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Commit restored."));

            public Task<OperationResult> DiscardAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Discarded changes."));

            public Task<OperationResult> SetLocalIdentityAsync(string name, string email, CancellationToken ct = default) =>
                Task.FromResult(OperationResult.Ok("Name and email saved for this repository."));

            public Task<IReadOnlyList<CommitInfo>> GetCommitLogAsync(int maxCount, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<CommitInfo>>(new List<CommitInfo>());

            public Task<DiffResult> GetWorkingTreeDiffAsync(CancellationToken ct = default) =>
                Task.FromResult(new DiffResult());

            public Task<DiffResult> GetCommitDiffAsync(string commitHash, CancellationToken ct = default) =>
                Task.FromResult(new DiffResult());

            public Task<IReadOnlyList<string>> GetCommitFilesAsync(string commitHash, CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<string>>(new List<string>());

            public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<RemoteInfo>>(new List<RemoteInfo>());
        }
    }
}
