using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class HistoryViewModel : ViewModelBase
    {
        private const int DefaultMaxCount = 100;

        private readonly IGitService gitService;
        private readonly Func<Task>? refreshStatusAsync;
        private ObservableCollection<CommitInfo> commits = new();
        private CommitInfo? selectedCommit;
        private ObservableCollection<string> changedFiles = new();
        private string lastOperationMessage = string.Empty;
        private CancellationTokenSource? changedFilesCts;

        public HistoryViewModel(
            IGitService gitService,
            IUiDispatcher? uiDispatcher = null,
            Func<Task>? refreshStatusAsync = null)
            : base(uiDispatcher)
        {
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            this.refreshStatusAsync = refreshStatusAsync;
            RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
            RestoreCommand = new AsyncCommand(() => RestoreSelectedCommitAsync(), () => !IsBusy && SelectedCommit != null);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
        }

        public ObservableCollection<CommitInfo> Commits
        {
            get => commits;
            private set => SetProperty(commits, value, updated => commits = updated);
        }

        public CommitInfo? SelectedCommit
        {
            get => selectedCommit;
            set
            {
                if (SetProperty(selectedCommit, value, updated => selectedCommit = updated))
                {
                    RestoreCommand.RaiseCanExecuteChanged();
                    LoadChangedFilesAsync(value);
                }
            }
        }

        public ObservableCollection<string> ChangedFiles
        {
            get => changedFiles;
            private set => SetProperty(changedFiles, value, updated => changedFiles = updated);
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value ?? string.Empty, updated => lastOperationMessage = updated);
        }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand RestoreCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Task RefreshAsync() =>
            RunBusyAsync("Loading history…", async ct =>
            {
                var log = await gitService.GetCommitLogAsync(DefaultMaxCount, ct).ConfigureAwait(false);
                InvokeOnUI(() =>
                {
                    Commits = new ObservableCollection<CommitInfo>(log);
                    SelectedCommit = null;
                    ChangedFiles = new ObservableCollection<string>();
                    LastOperationMessage = $"Loaded {log.Count} commits.";
                });
            });

        public Task RestoreSelectedCommitAsync()
        {
            CommitInfo? commit = SelectedCommit;
            if (commit == null)
            {
                return Task.CompletedTask;
            }

            string shortHash = commit.Hash.Length <= 7 ? commit.Hash : commit.Hash.Substring(0, 7);
            return RunBusyAsync($"Restoring {shortHash}…", async ct =>
            {
                OperationResult result = await gitService.RestoreCommitAsync(commit.Hash, ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
                if (result.Success && refreshStatusAsync != null)
                {
                    await refreshStatusAsync().ConfigureAwait(false);
                }
            });
        }

        private async void LoadChangedFilesAsync(CommitInfo? commit)
        {
            changedFilesCts?.Cancel();

            if (commit == null)
            {
                InvokeOnUI(() => ChangedFiles = new ObservableCollection<string>());
                return;
            }

            changedFilesCts = new CancellationTokenSource();
            var ct = changedFilesCts.Token;

            try
            {
                var files = await gitService.GetCommitFilesAsync(commit.Hash, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                InvokeOnUI(() =>
                {
                    if (SelectedCommit?.Hash == commit.Hash)
                        ChangedFiles = new ObservableCollection<string>(files);
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                InvokeOnUI(() => LastOperationMessage = $"Error loading changed files: {ex.Message}");
            }
        }

        protected override void ReportStatus(string message) => LastOperationMessage = message;

        protected override void OnBusyChanged()
        {
            InvokeOnUI(() =>
            {
                RefreshCommand.RaiseCanExecuteChanged();
                RestoreCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            });
        }
    }
}
