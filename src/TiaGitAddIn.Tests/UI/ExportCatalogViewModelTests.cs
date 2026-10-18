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
    public sealed class ExportCatalogViewModelTests
    {
        [Fact]
        public async Task SelectedCommitListsTheExportStoredInThatCommit()
        {
            var git = new FakeGitService();
            var viewModel = new ExportCatalogViewModel(git);
            var commit = new CommitInfo { Hash = "abcdef1234567890", Subject = "Export the line" };

            await viewModel.ShowAsync(commit);

            Assert.Equal("abcdef1234567890", git.RequestedHash);
            Assert.Equal("abcdef1  Export the line", viewModel.Heading);
            Assert.Equal("2 HMI files and 1 hardware file.", viewModel.Summary);
            Assert.Equal(2, viewModel.Groups.Count);
            Assert.Contains(viewModel.Groups, group => group.Title.StartsWith("HMI"));
            Assert.Contains(viewModel.Groups, group => group.Title.StartsWith("Hardware"));
            Assert.Contains("Start.xml", Titles(viewModel.Groups));
        }

        [Fact]
        public async Task CommitWithoutAnExportSaysSo()
        {
            var viewModel = new ExportCatalogViewModel(new FakeGitService { Files = new string[0] });

            await viewModel.ShowAsync(new CommitInfo { Hash = "abc", Subject = "PLC only" });

            Assert.Equal("This commit has no HMI or hardware export.", viewModel.Summary);
            Assert.Empty(viewModel.Groups);
        }

        [Fact]
        public async Task ClearingTheCommitResetsThePage()
        {
            var viewModel = new ExportCatalogViewModel(new FakeGitService());
            await viewModel.ShowAsync(new CommitInfo { Hash = "abc", Subject = "Export" });

            await viewModel.ShowAsync(null);

            Assert.Equal("Select a commit on the History page.", viewModel.Heading);
            Assert.Empty(viewModel.Groups);
        }

        private static IEnumerable<string> Titles(IEnumerable<ChangeNode> nodes)
        {
            foreach (ChangeNode node in nodes)
            {
                yield return node.Title;
                if (node.Children != null)
                {
                    foreach (string title in Titles(node.Children))
                    {
                        yield return title;
                    }
                }
            }
        }

        private sealed class FakeGitService : IGitService
        {
            public string RequestedHash { get; private set; } = string.Empty;

            public IReadOnlyList<string> Files { get; set; } = new[]
            {
                "HmiExport/Panel/Screens/Start.xml",
                "HmiExport/Panel/Tags/Speed/Speed.xml",
                "HardwareExport/PLC/Hardware.xml"
            };

            public Task<IReadOnlyList<string>> ListExportFilesAsync(string commitHash, CancellationToken ct = default)
            {
                RequestedHash = commitHash;
                return Task.FromResult(Files);
            }

            public Task<GitStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new GitStatus());
            public Task<OperationResult> StageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> UnstageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> StageAllAsync(CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> CommitAsync(string message, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> FetchAsync(string? remote = null, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> PullAsync(string? remote = null, string? branch = null, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> PushAsync(string? remote = null, string? branch = null, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<BranchInfo>>(new List<BranchInfo>());
            public Task<OperationResult> CreateBranchAsync(string name, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> SwitchBranchAsync(string name, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> CheckoutBranchAsync(string branchName, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> RestoreCommitAsync(string commitHash, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> DiscardAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<OperationResult> SetLocalIdentityAsync(string name, string email, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<(string Name, string Email)> GetLocalIdentityAsync(CancellationToken ct = default) => Task.FromResult((string.Empty, string.Empty));
            public Task<OperationResult> SetRemoteUrlAsync(string remoteName, string url, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok(string.Empty));
            public Task<IReadOnlyList<CommitInfo>> GetCommitLogAsync(int maxCount, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CommitInfo>>(new List<CommitInfo>());
            public Task<DiffResult> GetWorkingTreeDiffAsync(CancellationToken ct = default) => Task.FromResult(new DiffResult());
            public Task<DiffResult> GetCommitDiffAsync(string commitHash, CancellationToken ct = default) => Task.FromResult(new DiffResult());
            public Task<IReadOnlyList<string>> GetCommitFilesAsync(string commitHash, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
            public Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemoteInfo>>(new List<RemoteInfo>());
        }
    }
}
