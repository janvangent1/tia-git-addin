using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using TiaGitAddIn.Models;
using TiaGitAddIn.Reporting;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class HistoryViewModel : ViewModelBase
    {
        private const int DefaultMaxCount = 100;

        private readonly IGitService gitService;
        private readonly Func<Task>? refreshStatusAsync;
        private readonly string repositoryPath;
        private ObservableCollection<CommitInfo> commits = new();
        private CommitInfo? selectedCommit;
        private ObservableCollection<string> changedFiles = new();
        private ObservableCollection<ChangeNode> changedFileGroups = new();
        private string lastOperationMessage = string.Empty;
        private string compareFromText = "No commit chosen as the other side.";
        private CommitInfo? compareFrom;
        private CancellationTokenSource? changedFilesCts;

        public HistoryViewModel(
            IGitService gitService,
            IUiDispatcher? uiDispatcher = null,
            Func<Task>? refreshStatusAsync = null,
            string? repositoryPath = null)
            : base(uiDispatcher)
        {
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            this.refreshStatusAsync = refreshStatusAsync;
            this.repositoryPath = repositoryPath ?? string.Empty;
            RefreshCommand = new AsyncCommand(() => RefreshAsync(), () => !IsBusy);
            RestoreCommand = new AsyncCommand(() => RestoreSelectedCommitAsync(), () => !IsBusy && SelectedCommit != null);
            ExportPdfCommand = new AsyncCommand(() => ExportPdfAsync(), () => !IsBusy && Commits.Count > 0);
            ChooseFilesCommand = new AsyncCommand(ChooseFilesAsync, () => !IsBusy && this.repositoryPath.Length > 0);
            MarkCompareFromCommand = new RelayCommand(_ => MarkCompareFrom(), _ => !IsBusy && SelectedCommit != null);
            CompareWithSelectedCommand = new AsyncCommand(CompareWithSelectedAsync, () => !IsBusy && SelectedCommit != null && compareFrom != null);
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
                    MarkCompareFromCommand?.RaiseCanExecuteChanged();
                    CompareWithSelectedCommand?.RaiseCanExecuteChanged();
                    LoadChangedFilesAsync(value);
                }
            }
        }

        public ObservableCollection<string> ChangedFiles
        {
            get => changedFiles;
            private set => SetProperty(changedFiles, value, updated => changedFiles = updated);
        }

        public ObservableCollection<ChangeNode> ChangedFileGroups
        {
            get => changedFileGroups;
            private set => SetProperty(changedFileGroups, value, updated => changedFileGroups = updated);
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value ?? string.Empty, updated => lastOperationMessage = updated);
        }

        public Func<string, Task>? OpenFileAsync { get; set; }

        public Func<IReadOnlyList<string>, Task>? OpenFileHistoryAsync { get; set; }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand RestoreCommand { get; }
        public AsyncCommand ExportPdfCommand { get; }
        public AsyncCommand ChooseFilesCommand { get; }
        public RelayCommand MarkCompareFromCommand { get; }
        public AsyncCommand CompareWithSelectedCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Func<string, string, Task>? CompareRangeAsync { get; set; }

        public string CompareFromText
        {
            get => compareFromText;
            private set => SetProperty(compareFromText, value ?? string.Empty, updated => compareFromText = updated);
        }

        private Task ChooseFilesAsync()
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                LastOperationMessage = "The workspace folder is not available.";
                return Task.CompletedTask;
            }

            OpenFileDialog dialog = new()
            {
                Title = "Choose files",
                InitialDirectory = repositoryPath,
                Multiselect = true,
                CheckFileExists = true,
                Filter = "All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0)
            {
                return Task.CompletedTask;
            }

            var relative = new List<string>();
            foreach (string fullPath in dialog.FileNames)
            {
                if (!RepositoryPaths.TryMakeRelative(repositoryPath, fullPath, out string path))
                {
                    LastOperationMessage = "Choose files inside the repository.";
                    return Task.CompletedTask;
                }

                relative.Add(path);
            }

            return OpenHistory(relative);
        }

        private Task OpenHistory(IReadOnlyList<string> paths)
        {
            if (OpenFileHistoryAsync == null)
            {
                LastOperationMessage = "File history is not available.";
                return Task.CompletedTask;
            }

            return OpenFileHistoryAsync(paths);
        }

        public Task OpenFile(string path)
        {
            if (OpenFileAsync == null || string.IsNullOrWhiteSpace(path))
            {
                return Task.CompletedTask;
            }

            return OpenFileAsync(path);
        }

        private void MarkCompareFrom()
        {
            if (SelectedCommit == null)
            {
                return;
            }

            compareFrom = SelectedCommit;
            string hash = compareFrom.Hash ?? string.Empty;
            string mark = hash.Length > 7 ? hash.Substring(0, 7) : hash;
            CompareFromText = "Other side: " + mark + "  " + (compareFrom.Subject ?? string.Empty);
            CompareWithSelectedCommand.RaiseCanExecuteChanged();
        }

        private Task CompareWithSelectedAsync()
        {
            if (compareFrom == null || SelectedCommit == null || CompareRangeAsync == null)
            {
                LastOperationMessage = "Choose the other side, then the commit to compare it with.";
                return Task.CompletedTask;
            }

            if (string.Equals(compareFrom.Hash, SelectedCommit.Hash, StringComparison.OrdinalIgnoreCase))
            {
                LastOperationMessage = "Choose two different commits.";
                return Task.CompletedTask;
            }

            return CompareRangeAsync(compareFrom.Hash, SelectedCommit.Hash);
        }

        public Task RefreshAsync() =>
            RunBusyAsync("Loading history…", async ct =>
            {
                var log = await gitService.GetCommitLogAsync(DefaultMaxCount, ct).ConfigureAwait(false);
                InvokeOnUI(() =>
                {
                    Commits = new ObservableCollection<CommitInfo>(log);
                    SelectedCommit = null;
                    ChangedFiles = new ObservableCollection<string>();
                    ChangedFileGroups = new ObservableCollection<ChangeNode>();
                    LastOperationMessage = $"Loaded {log.Count} commits.";
                    ExportPdfCommand.RaiseCanExecuteChanged();
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

        private Task ExportPdfAsync()
        {
            if (Commits.Count == 0)
            {
                LastOperationMessage = "There is no history to export. Refresh the history first.";
                return Task.CompletedTask;
            }

            SaveFileDialog dialog = new()
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = "git-history.pdf",
                Title = "Export version history",
                AddExtension = true,
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return Task.CompletedTask;
            }

            string path = dialog.FileName;
            List<CommitInfo> commits = Commits.ToList();
            return RunBusyAsync("Reading changed files for the PDF…", async ct =>
            {
                var filesByCommit = new Dictionary<string, IReadOnlyList<string>?>(StringComparer.OrdinalIgnoreCase);
                foreach (CommitInfo commit in commits)
                {
                    ct.ThrowIfCancellationRequested();
                    string hash = commit?.Hash ?? string.Empty;
                    if (hash.Length == 0 || filesByCommit.ContainsKey(hash))
                    {
                        continue;
                    }

                    try
                    {
                        filesByCommit[hash] = await gitService.GetCommitFilesAsync(hash, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        filesByCommit[hash] = null;
                    }
                }

                PdfReport report = HistoryPdfReport.Create(repositoryPath, commits, filesByCommit);
                PdfReportWriter.Write(report, path);
                InvokeOnUI(() => LastOperationMessage = "Exported version history to " + path);
            });
        }

        private async void LoadChangedFilesAsync(CommitInfo? commit)
        {
            changedFilesCts?.Cancel();

            if (commit == null)
            {
                InvokeOnUI(() =>
                {
                    ChangedFiles = new ObservableCollection<string>();
                    ChangedFileGroups = new ObservableCollection<ChangeNode>();
                });
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
                    {
                        ChangedFiles = new ObservableCollection<string>(files);
                        ChangedFileGroups = ChangeTree.Group(files, path => path);
                    }
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
                ExportPdfCommand.RaiseCanExecuteChanged();
                ChooseFilesCommand.RaiseCanExecuteChanged();
                MarkCompareFromCommand.RaiseCanExecuteChanged();
                CompareWithSelectedCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            });
        }
    }
}
