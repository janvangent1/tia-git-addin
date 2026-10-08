using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class HistoryViewModelTests
    {
        [Fact]
        public async Task SelectingCommitIgnoresStaleChangedFiles()
        {
            var firstFiles = new TaskCompletionSource<IReadOnlyList<string>>();
            var gitService = new FakeGitService(firstFiles);
            var viewModel = new HistoryViewModel(gitService);
            var first = new CommitInfo { Hash = "first", Subject = "First" };
            var second = new CommitInfo { Hash = "second", Subject = "Second" };

            viewModel.SelectedCommit = first;
            viewModel.SelectedCommit = second;

            await WaitUntilAsync(() => viewModel.ChangedFiles.SequenceEqual(new[] { "second.xml" }));
            firstFiles.SetResult(new[] { "first.xml" });
            await Task.Delay(50);

            Assert.Equal(new[] { "second.xml" }, viewModel.ChangedFiles);
        }

        [Fact]
        public async Task RestoreSelectedCommitRestoresHashAndRefreshesStatus()
        {
            var gitService = new FakeGitService(new TaskCompletionSource<IReadOnlyList<string>>());
            int refreshCount = 0;
            var viewModel = new HistoryViewModel(gitService, refreshStatusAsync: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
            viewModel.SelectedCommit = new CommitInfo { Hash = "abcdef1234567890", Subject = "Older" };

            await viewModel.RestoreSelectedCommitAsync();

            Assert.Equal("abcdef1234567890", gitService.RestoredHash);
            Assert.Equal(1, refreshCount);
            Assert.Contains("restore-abcdef1", viewModel.LastOperationMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task RestoreWithoutSelectionDoesNothing()
        {
            var gitService = new FakeGitService(new TaskCompletionSource<IReadOnlyList<string>>());
            var viewModel = new HistoryViewModel(gitService);

            await viewModel.RestoreSelectedCommitAsync();

            Assert.Null(gitService.RestoredHash);
        }

        [Fact]
        public async Task FailedRestoreDoesNotRefreshStatus()
        {
            var gitService = new FakeGitService(new TaskCompletionSource<IReadOnlyList<string>>())
            {
                RestoreResult = OperationResult.Fail("Commit or discard the current workspace changes before restoring an older commit.")
            };
            int refreshCount = 0;
            var viewModel = new HistoryViewModel(gitService, refreshStatusAsync: () =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });
            viewModel.SelectedCommit = new CommitInfo { Hash = "abcdef1234567890" };

            await viewModel.RestoreSelectedCommitAsync();

            Assert.Equal(0, refreshCount);
            Assert.Contains("Commit or discard", viewModel.LastOperationMessage, StringComparison.Ordinal);
        }

        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            for (int i = 0; i < 20; i++)
            {
                if (condition()) return;
                await Task.Delay(25);
            }

            Assert.True(condition());
        }

        private sealed class FakeGitService : IGitService
        {
            private readonly TaskCompletionSource<IReadOnlyList<string>> firstFiles;

            public FakeGitService(TaskCompletionSource<IReadOnlyList<string>> firstFiles)
            {
                this.firstFiles = firstFiles;
            }

            public async Task<IReadOnlyList<string>> GetCommitFilesAsync(string commitHash, CancellationToken ct = default)
            {
                if (commitHash == "first")
                    return await firstFiles.Task.ConfigureAwait(false);

                return new[] { "second.xml" };
            }

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

            public string? RestoredHash { get; private set; }

            public OperationResult RestoreResult { get; set; } =
                OperationResult.Ok("Restored abcdef1 on branch restore-abcdef1.");

            public Task<OperationResult> RestoreCommitAsync(string commitHash, CancellationToken ct = default)
            {
                RestoredHash = commitHash;
                return Task.FromResult(RestoreResult);
            }

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

            public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct = default) =>
                Task.FromResult<IReadOnlyList<RemoteInfo>>(new List<RemoteInfo>());
        }
    }
}
