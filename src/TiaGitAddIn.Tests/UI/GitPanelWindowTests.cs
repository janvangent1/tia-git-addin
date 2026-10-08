using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Services;
using TiaGitAddIn.Services.Comparison;
using TiaGitAddIn.Services.Revision;
using TiaGitAddIn.UI.Mapping;
using TiaGitAddIn.UI.ViewModels;
using TiaGitAddIn.UI.ViewModels.Comparison;
using TiaGitAddIn.UI.Views;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class GitPanelWindowTests
    {
        [Fact]
        public async Task SelectingTheHistoryTabLoadsTheCommitLog()
        {
            var gitService = new FakeGitService();
            gitService.Log.Add(new CommitInfo { Hash = "abc1234", Subject = "Initial" });
            var main = new MainViewModel(
                @"C:\repo",
                gitService,
                new UnusedRevisionProvider(),
                new UnusedComparisonCoordinator(),
                new UnusedPresentationMapper(),
                new FakeConfigurationService());

            await WpfTestHost.RunAsync(async _ =>
            {
                var window = new GitPanelWindow(main)
                {
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Width = 800,
                    Height = 500
                };
                window.Show();
                window.UpdateLayout();
                Assert.Equal(0, gitService.LogRequests);

                TabControl tabs = FindTabControl(window)
                    ?? throw new InvalidOperationException("Tab control was not found.");
                tabs.SelectedIndex = TabIndex(tabs, "History");
                for (int i = 0; i < 20 && main.History.Commits.Count == 0; i++)
                {
                    await Task.Delay(25);
                }

                window.Close();
            });

            Assert.Equal(1, gitService.LogRequests);
            Assert.Equal("abc1234", main.History.Commits.Single().Hash);
        }

        [Fact]
        public async Task SelectingTheBranchTabListsBranches()
        {
            var gitService = new FakeGitService();
            gitService.Branches.Add(new BranchInfo { Name = "restore-abc1234", IsCurrent = true });
            var main = new MainViewModel(
                @"C:\repo",
                gitService,
                new UnusedRevisionProvider(),
                new UnusedComparisonCoordinator(),
                new UnusedPresentationMapper(),
                new FakeConfigurationService());

            await WpfTestHost.RunAsync(async _ =>
            {
                var window = new GitPanelWindow(main)
                {
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Width = 800,
                    Height = 500
                };
                window.Show();
                window.UpdateLayout();
                Assert.Equal(0, gitService.BranchRequests);

                TabControl tabs = FindTabControl(window)
                    ?? throw new InvalidOperationException("Tab control was not found.");
                tabs.SelectedIndex = TabIndex(tabs, "Branch");
                for (int i = 0; i < 20 && main.Branch.Branches.Count == 0; i++)
                {
                    await Task.Delay(25);
                }

                window.UpdateLayout();
                Assert.True(ContainsText(window, "restore-abc1234"));
                window.Close();
            });

            Assert.Equal(1, gitService.BranchRequests);
            Assert.Equal("restore-abc1234", main.Branch.Branches.Single().Name);
        }

        private static int TabIndex(TabControl tabs, string header)
        {
            for (int i = 0; i < tabs.Items.Count; i++)
            {
                if (tabs.Items[i] is TabItem item && Equals(item.Header, header))
                {
                    return i;
                }
            }

            throw new InvalidOperationException(header + " tab was not found.");
        }

        private static bool ContainsText(DependencyObject parent, string text)
        {
            if (parent is TextBlock block && block.Text == text)
            {
                return true;
            }

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                if (ContainsText(VisualTreeHelper.GetChild(parent, i), text))
                {
                    return true;
                }
            }

            return false;
        }

        private static TabControl? FindTabControl(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                if (child is TabControl tabs)
                {
                    return tabs;
                }

                TabControl? nested = FindTabControl(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private sealed class FakeConfigurationService : IConfigurationService
        {
            public GitConfiguration Load(string repositoryRoot) =>
                new GitConfiguration { RepositoryPath = repositoryRoot };

            public void Save(string repositoryRoot, GitConfiguration configuration)
            {
            }
        }

        private sealed class UnusedRevisionProvider : IPlcRevisionProvider
        {
            public Task<PlcRevisionLease> LoadAsync(
                PlcRevisionSide side,
                PlcRevisionSource source,
                string repositoryRelativePath,
                CancellationToken cancellationToken) =>
                throw new NotImplementedException();

            public PlcRevisionLease Missing(
                PlcRevisionSide side,
                PlcRevisionSource source,
                string repositoryRelativePath,
                PlcRevisionMissingReason reason) =>
                throw new NotImplementedException();
        }

        private sealed class UnusedComparisonCoordinator : IPlcComparisonCoordinator
        {
            public Task<PlcComparisonResult> CompareAsync(
                PlcRevision left,
                PlcRevision right,
                CancellationToken cancellationToken) =>
                throw new NotImplementedException();

            public PlcComparisonResult CreateRevisionLoadError(
                PlcArtifactKind bestKnownKind,
                PlcComparisonMode requestedMode,
                Exception exception,
                PlcRevisionSide side) =>
                throw new NotImplementedException();
        }

        private sealed class UnusedPresentationMapper : IComparisonPresentationMapper
        {
            public ComparisonPresentationViewModel Map(PlcComparisonResult result) =>
                throw new NotImplementedException();
        }

        private sealed class FakeGitService : IGitService
        {
            public int LogRequests { get; private set; }

            public List<CommitInfo> Log { get; } = new();

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

            public int BranchRequests { get; private set; }

            public List<BranchInfo> Branches { get; } = new();

            public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
            {
                BranchRequests++;
                return Task.FromResult<IReadOnlyList<BranchInfo>>(Branches.ToList());
            }

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

            public Task<IReadOnlyList<CommitInfo>> GetCommitLogAsync(int maxCount, CancellationToken ct = default)
            {
                LogRequests++;
                return Task.FromResult<IReadOnlyList<CommitInfo>>(Log);
            }

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
