using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class BranchViewModel : ViewModelBase
    {
        private readonly IGitService gitService;
        private readonly Func<string>? remoteNameProvider;
        private ObservableCollection<BranchInfo> branches = new();
        private BranchInfo? selectedBranch;
        private string newBranchName = string.Empty;
        private string lastOperationMessage = string.Empty;
        private string pullPreviewText = string.Empty;
        private bool hasPullPreview;

        public BranchViewModel(
            IGitService gitService,
            IUiDispatcher? uiDispatcher = null,
            Func<string>? remoteNameProvider = null)
            : base(uiDispatcher)
        {
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            this.remoteNameProvider = remoteNameProvider;

            RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
            CreateBranchCommand = new AsyncCommand(() => CreateBranchAsync(), () => !IsBusy && !string.IsNullOrWhiteSpace(NewBranchName));
            SwitchBranchCommand = new AsyncCommand(() => SwitchBranchAsync(), () => !IsBusy && SelectedBranch != null && !SelectedBranch.IsCurrent);
            FetchCommand = new AsyncCommand(() => FetchAsync(), () => !IsBusy);
            PullCommand = new AsyncCommand(() => PreviewPullAsync(), () => !IsBusy);
            ContinuePullCommand = new AsyncCommand(() => PullAsync(), () => !IsBusy && HasPullPreview);
            PushCommand = new AsyncCommand(() => PushAsync(), () => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
        }

        public ObservableCollection<BranchInfo> Branches
        {
            get => branches;
            private set => SetProperty(branches, value, updated => branches = updated);
        }

        public BranchInfo? SelectedBranch
        {
            get => selectedBranch;
            set
            {
                if (SetProperty(selectedBranch, value, updated => selectedBranch = updated))
                {
                    InvokeOnUI(() => SwitchBranchCommand.RaiseCanExecuteChanged());
                }
            }
        }

        public string NewBranchName
        {
            get => newBranchName;
            set
            {
                if (SetProperty(newBranchName, value ?? string.Empty, updated => newBranchName = updated))
                {
                    InvokeOnUI(() => CreateBranchCommand.RaiseCanExecuteChanged());
                }
            }
        }

        public string PullPreviewText
        {
            get => pullPreviewText;
            private set => SetProperty(pullPreviewText, value ?? string.Empty, updated => pullPreviewText = updated);
        }

        public bool HasPullPreview
        {
            get => hasPullPreview;
            private set
            {
                if (SetProperty(hasPullPreview, value, updated => hasPullPreview = updated))
                {
                    InvokeOnUI(() => ContinuePullCommand.RaiseCanExecuteChanged());
                }
            }
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value ?? string.Empty, updated => lastOperationMessage = updated);
        }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand CreateBranchCommand { get; }
        public AsyncCommand SwitchBranchCommand { get; }
        public AsyncCommand FetchCommand { get; }
        public AsyncCommand PullCommand { get; }
        public AsyncCommand ContinuePullCommand { get; }
        public AsyncCommand PushCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Task RefreshAsync() =>
            RunBusyAsync("Loading branches…", LoadBranchesCoreAsync);

        // Branch load without the busy/cancel shell, so a post-operation refresh can reuse the
        // caller's RunBusyAsync scope and token instead of nesting another RunBusyAsync.
        private async Task LoadBranchesCoreAsync(CancellationToken ct)
        {
            var branchList = await gitService.GetBranchesAsync(ct).ConfigureAwait(false);
            InvokeOnUI(() =>
            {
                Branches = new ObservableCollection<BranchInfo>(branchList);
                SelectedBranch = Branches.FirstOrDefault(b => b.IsCurrent) ?? Branches.FirstOrDefault();
            });
        }

        private Task CreateBranchAsync() =>
            RunBusyAsync("Creating branch…", async ct =>
            {
                var result = await gitService.CreateBranchAsync(NewBranchName, ct).ConfigureAwait(false);
                InvokeOnUI(() =>
                {
                    LastOperationMessage = GitUserMessages.Describe(result);
                    if (result.Success) NewBranchName = string.Empty;
                });
                if (result.Success) await LoadBranchesCoreAsync(ct).ConfigureAwait(false);
            });

        private Task SwitchBranchAsync()
        {
            BranchInfo? target = SelectedBranch;
            if (target == null) return Task.CompletedTask;

            return RunBusyAsync($"Switching to {target.Name}…", async ct =>
            {
                var result = await gitService.SwitchBranchAsync(target.Name, ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
                if (result.Success) await LoadBranchesCoreAsync(ct).ConfigureAwait(false);
            });
        }

        private Task FetchAsync() =>
            RunBusyAsync("Fetching…", async ct =>
            {
                var result = await gitService.FetchAsync(CurrentRemoteName(), ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
            });

        private Task PreviewPullAsync() =>
            RunBusyAsync("Checking what a pull would bring in…", async ct =>
            {
                PullPreview preview = await gitService.PreviewPullAsync(CurrentRemoteName(), ct).ConfigureAwait(false);
                InvokeOnUI(() =>
                {
                    HasPullPreview = preview.HasIncoming;
                    PullPreviewText = BuildPreviewText(preview);
                    LastOperationMessage = preview.HasIncoming
                        ? "Review the list, then continue the pull."
                        : preview.Message;
                });
            });

        private static string BuildPreviewText(PullPreview preview)
        {
            var lines = new List<string> { preview.Message };
            foreach (CommitInfo commit in preview.Commits)
            {
                string hash = commit.Hash ?? string.Empty;
                string mark = hash.Length > 7 ? hash.Substring(0, 7) : hash;
                lines.Add(mark + "  " + (commit.Subject ?? string.Empty));
            }

            foreach (string file in preview.PlcFiles)
            {
                lines.Add(file);
            }

            return string.Join(Environment.NewLine, lines);
        }

        private Task PullAsync() =>
            RunBusyAsync("Pulling…", async ct =>
            {
                var result = await gitService.PullAsync(CurrentRemoteName(), ct: ct).ConfigureAwait(false);
                InvokeOnUI(() =>
                {
                    LastOperationMessage = GitUserMessages.Describe(result);
                    if (result.Success)
                    {
                        HasPullPreview = false;
                        PullPreviewText = string.Empty;
                    }
                });
                if (result.Success) await LoadBranchesCoreAsync(ct).ConfigureAwait(false);
            });

        private Task PushAsync() =>
            RunBusyAsync("Pushing…", async ct =>
            {
                var result = await gitService.PushAsync(CurrentRemoteName(), ct: ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
            });

        private string CurrentRemoteName()
        {
            string? name = remoteNameProvider?.Invoke();
            return string.IsNullOrWhiteSpace(name) ? "origin" : name.Trim();
        }

        protected override void ReportStatus(string message) => LastOperationMessage = message;

        protected override void OnBusyChanged()
        {
            InvokeOnUI(() =>
            {
                RefreshCommand.RaiseCanExecuteChanged();
                CreateBranchCommand.RaiseCanExecuteChanged();
                SwitchBranchCommand.RaiseCanExecuteChanged();
                FetchCommand.RaiseCanExecuteChanged();
                PullCommand.RaiseCanExecuteChanged();
                ContinuePullCommand.RaiseCanExecuteChanged();
                PushCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            });
        }
    }
}
