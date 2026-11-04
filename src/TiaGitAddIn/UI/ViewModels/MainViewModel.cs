using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.Services.Comparison;
using TiaGitAddIn.Services.Hmi;
using TiaGitAddIn.Services.Revision;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.Mapping;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly IGitService gitService;

        public MainViewModel(
            string repositoryPath,
            IGitService gitService,
            IPlcRevisionProvider revisionProvider,
            IPlcComparisonCoordinator comparisonCoordinator,
            IComparisonPresentationMapper mapper,
            IConfigurationService configurationService,
            IAddInLogger? logger = null,
            IUiDispatcher? uiDispatcher = null,
            IHmiDeviceExchange? hmiExchange = null)
            : base(uiDispatcher)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
                throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
            if (gitService == null)
                throw new ArgumentNullException(nameof(gitService));
            if (revisionProvider == null)
                throw new ArgumentNullException(nameof(revisionProvider));
            if (comparisonCoordinator == null)
                throw new ArgumentNullException(nameof(comparisonCoordinator));
            if (mapper == null)
                throw new ArgumentNullException(nameof(mapper));
            if (configurationService == null)
                throw new ArgumentNullException(nameof(configurationService));

            RepositoryPath = repositoryPath;
            this.gitService = gitService;
            Status = new StatusViewModel(gitService, UiDispatcher, ShowWorkingTreeDiffAsync, OpenWorkingTreeFileAsync);
            Commit = new CommitViewModel(gitService, RefreshAfterCommitAsync, UiDispatcher);
            Branch = new BranchViewModel(
                gitService,
                UiDispatcher,
                () => configurationService.Load(repositoryPath).DefaultRemote);
            History = new HistoryViewModel(gitService, UiDispatcher, RefreshStatusAndBranchesAsync, repositoryPath);
            Export = new ExportCatalogViewModel(gitService, UiDispatcher);
            Diff = new DiffViewModel(
                gitService, revisionProvider, comparisonCoordinator, mapper, logger, UiDispatcher, repositoryPath);
            Settings = new SettingsViewModel(configurationService, repositoryPath, gitService, UiDispatcher);
            About = new AboutViewModel();
            History.OpenFileAsync = OpenHistoryFileAsync;
            History.OpenFileHistoryAsync = OpenFileHistoryAsync;
            History.CompareRangeAsync = OpenRangeDiffAsync;
            Status.CompareConflictAsync = OpenConflictAsync;
            Hmi = new HmiExchangeViewModel(
                repositoryPath,
                hmiExchange ?? new UnavailableHmiDeviceExchange(),
                Status.RefreshAsync,
                UiDispatcher);
            Hmi.Categories = () => Settings.ExportCategories();

            CancelCommand = new RelayCommand(_ => CancelAll(), _ => IsBusy);

            Status.PropertyChanged += OnChildPropertyChanged;
            Commit.PropertyChanged += OnChildPropertyChanged;
            Branch.PropertyChanged += OnChildPropertyChanged;
            History.PropertyChanged += OnChildPropertyChanged;
            Diff.PropertyChanged += OnChildPropertyChanged;
            Hmi.PropertyChanged += OnChildPropertyChanged;
        }

        public const int DiffTabIndex = 4;

        private int selectedTabIndex;

        public string RepositoryPath { get; }

        public int SelectedTabIndex
        {
            get => selectedTabIndex;
            set => SetProperty(ref selectedTabIndex, value);
        }

        public StatusViewModel Status { get; }
        public CommitViewModel Commit { get; }
        public BranchViewModel Branch { get; }
        public HistoryViewModel History { get; }
        public ExportCatalogViewModel Export { get; }
        public DiffViewModel Diff { get; }
        public SettingsViewModel Settings { get; }
        public AboutViewModel About { get; }
        public HmiExchangeViewModel Hmi { get; }

        public RelayCommand CancelCommand { get; }

        public Task RefreshAsync() => Status.RefreshAsync();

        public Task RefreshOnOpenAsync() =>
            Task.WhenAll(Status.RefreshAsync(), History.RefreshAsync());

        private async Task RefreshAfterCommitAsync()
        {
            await Status.RefreshAsync().ConfigureAwait(false);
            await History.RefreshAsync().ConfigureAwait(false);
        }

        private Task ShowWorkingTreeDiffAsync()
        {
            SelectedTabIndex = DiffTabIndex;
            return Diff.LoadWorkingTreeDiffAsync();
        }

        private Task OpenWorkingTreeFileAsync(string path)
        {
            SelectedTabIndex = DiffTabIndex;
            return OpenDiffFileAsync(null, path);
        }

        private Task OpenRangeDiffAsync(string leftHash, string rightHash)
        {
            SelectedTabIndex = DiffTabIndex;
            return Diff.LoadRangeDiffAsync(leftHash, rightHash);
        }

        private async Task OpenConflictAsync(string path)
        {
            SelectedTabIndex = DiffTabIndex;
            (string? ours, string? theirs) = await gitService.GetConflictSidesAsync(path).ConfigureAwait(false);
            await Diff.ShowConflictAsync(path, ours, theirs).ConfigureAwait(false);
        }

        private Task OpenFileHistoryAsync(System.Collections.Generic.IReadOnlyList<string> paths)
        {
            SelectedTabIndex = DiffTabIndex;
            return Diff.StartVersionWalkAsync(paths);
        }

        private Task OpenHistoryFileAsync(string path)
        {
            SelectedTabIndex = DiffTabIndex;
            return OpenDiffFileAsync(History.SelectedCommit?.Hash, path);
        }

        private async Task OpenDiffFileAsync(string? commitHash, string path)
        {
            bool sameCommit = string.IsNullOrEmpty(commitHash)
                ? Diff.IsWorkingTreeDiff
                : string.Equals(Diff.LoadedCommitHash, commitHash, StringComparison.OrdinalIgnoreCase);
            if (!sameCommit)
            {
                if (string.IsNullOrEmpty(commitHash))
                {
                    await Diff.LoadWorkingTreeDiffAsync().ConfigureAwait(false);
                }
                else
                {
                    await Diff.LoadCommitDiffAsync(commitHash!).ConfigureAwait(false);
                }
            }

            await Diff.FocusFileAsync(path).ConfigureAwait(false);
        }

        private Task RefreshStatusAndBranchesAsync() =>
            Task.WhenAll(Status.RefreshAsync(), Branch.RefreshAsync());

        private void OnChildPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HistoryViewModel.SelectedCommit) && sender is HistoryViewModel)
            {
                CommitInfo? commit = History.SelectedCommit;
                _ = Export.ShowAsync(commit);
                if (commit != null)
                {
                    // Fire-and-forget is safe here: LoadCommitDiffAsync is built on RunBusyAsync, which
                    // catches every exception (including OperationCanceledException) internally and
                    // reports it via ReportStatus, so the returned Task never faults or needs observing.
                    _ = Diff.LoadCommitDiffAsync(commit.Hash);
                }

                return;
            }

            if (e.PropertyName == nameof(IsBusy) || e.PropertyName == nameof(BusyMessage))
            {
                RecomputeAggregate();
            }

            if (e.PropertyName == nameof(IsBusy)
                || e.PropertyName == nameof(BusyMessage)
                || e.PropertyName == nameof(HmiExchangeViewModel.HasChecklist)
                || e.PropertyName == nameof(HmiExchangeViewModel.LastOperationMessage))
            {
                NotifyOverlay();
            }
        }

        private void RecomputeAggregate()
        {
            bool busy = Status.IsBusy || Commit.IsBusy || Branch.IsBusy || History.IsBusy || Diff.IsBusy || Hmi.IsBusy;

            string msg = Hmi.IsBusy ? Hmi.BusyMessage : string.Empty;
            if (string.IsNullOrEmpty(msg)) msg = Status.BusyMessage;
            if (string.IsNullOrEmpty(msg)) msg = Commit.BusyMessage;
            if (string.IsNullOrEmpty(msg)) msg = Branch.BusyMessage;
            if (string.IsNullOrEmpty(msg)) msg = History.BusyMessage;
            if (string.IsNullOrEmpty(msg)) msg = Diff.BusyMessage;
            if (string.IsNullOrEmpty(msg)) msg = Hmi.BusyMessage;

            InvokeOnUI(() =>
            {
                IsBusy = busy;
                BusyMessage = msg;
            });
        }

        public bool ShowOverlay => IsBusy && !Hmi.HasChecklist;

        public bool ShowProgress => IsBusy;

        public double OverlayCardMinWidth => Hmi.HasChecklist ? 840 : 420;

        public string OverlayStatus
        {
            get
            {
                if (Hmi.HasChecklist && Hmi.IsBusy)
                {
                    return string.Empty;
                }

                if (Hmi.HasChecklist)
                {
                    return Hmi.LastOperationMessage;
                }

                return BusyMessage;
            }
        }

        public bool HasOverlayStatus => !string.IsNullOrEmpty(OverlayStatus);

        public string OverlayActionText => IsBusy ? "Cancel" : "Close";

        public ICommand OverlayActionCommand => IsBusy ? CancelCommand : Hmi.DismissChecklistCommand;

        protected override void OnBusyChanged()
        {
            InvokeOnUI(() =>
            {
                CancelCommand.RaiseCanExecuteChanged();
                NotifyOverlay();
            });
        }

        private void NotifyOverlay()
        {
            InvokeOnUI(() =>
            {
                OnPropertyChanged(nameof(ShowOverlay));
                OnPropertyChanged(nameof(ShowProgress));
                OnPropertyChanged(nameof(OverlayCardMinWidth));
                OnPropertyChanged(nameof(OverlayStatus));
                OnPropertyChanged(nameof(HasOverlayStatus));
                OnPropertyChanged(nameof(OverlayActionText));
                OnPropertyChanged(nameof(OverlayActionCommand));
            });
        }

        private void CancelAll()
        {
            if (Status.IsBusy) Status.CancelCommand.Execute(null);
            if (Commit.IsBusy) Commit.CancelCommand.Execute(null);
            if (Branch.IsBusy) Branch.CancelCommand.Execute(null);
            if (History.IsBusy) History.CancelCommand.Execute(null);
            if (Diff.IsBusy) Diff.CancelCommand.Execute(null);
            if (Hmi.IsBusy) Hmi.CancelCommand.Execute(null);
        }
    }
}
