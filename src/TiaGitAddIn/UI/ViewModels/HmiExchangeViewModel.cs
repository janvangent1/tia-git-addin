using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.Services.Hmi;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class HmiExchangeViewModel : ViewModelBase
    {
        private readonly IHmiDeviceExchange exchange;
        private readonly Func<Task> refreshStatusAsync;
        private readonly string repositoryPath;
        private string lastOperationMessage = string.Empty;
        private bool hasChecklist;
        private string expandedDevice = string.Empty;
        private bool exportHmi = true;
        private bool exportHardware = true;

        public HmiExchangeViewModel(
            string repositoryPath,
            IHmiDeviceExchange exchange,
            Func<Task> refreshStatusAsync,
            IUiDispatcher? uiDispatcher = null)
            : base(uiDispatcher)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
            }

            this.repositoryPath = repositoryPath;
            this.exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            this.refreshStatusAsync = refreshStatusAsync ?? throw new ArgumentNullException(nameof(refreshStatusAsync));
            ExportCommand = new AsyncCommand(() => ExportAsync(), () => !IsBusy && (ExportHmi || ExportHardware));
            ImportCommand = new AsyncCommand(() => ImportAsync(), () => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
            DismissChecklistCommand = new RelayCommand(_ => DismissChecklist());
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value, updated => lastOperationMessage = updated);
        }

        public ObservableCollection<HmiChecklistItem> Checklist { get; } = new ObservableCollection<HmiChecklistItem>();

        public ObservableCollection<HmiChecklistGroup> ChecklistGroups { get; } = new ObservableCollection<HmiChecklistGroup>();

        public bool HasChecklist
        {
            get => hasChecklist;
            private set => SetProperty(ref hasChecklist, value);
        }

        public bool ExportHmi
        {
            get => exportHmi;
            set
            {
                if (SetProperty(ref exportHmi, value))
                {
                    ExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ExportHardware
        {
            get => exportHardware;
            set
            {
                if (SetProperty(ref exportHardware, value))
                {
                    ExportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncCommand ExportCommand { get; }

        public AsyncCommand ImportCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand DismissChecklistCommand { get; }

        public Task ExportAsync()
        {
            bool includeHmi = ExportHmi;
            bool includeHardware = ExportHardware;
            if (!includeHmi && !includeHardware)
            {
                return Task.CompletedTask;
            }

            InvokeOnUI(ClearChecklist);
            string busyMessage = includeHmi && includeHardware
                ? "Exporting…"
                : includeHardware
                    ? "Exporting hardware…"
                    : "Exporting HMI…";
            return RunBusyAsync(busyMessage, async ct =>
            {
                var progress = new Progress<string>(message => InvokeOnUI(() => ApplyExportProgress(message)));
                var messages = new System.Collections.Generic.List<string>();
                bool succeeded = false;
                if (includeHmi)
                {
                    OperationResult hmi = await exchange.ExportAsync(repositoryPath, progress, ct).ConfigureAwait(false);
                    messages.Add(GitUserMessages.Describe(hmi));
                    succeeded |= hmi.Success;
                    ct.ThrowIfCancellationRequested();
                }

                if (includeHardware)
                {
                    OperationResult hardware = await exchange.ExportHardwareAsync(repositoryPath, progress, ct).ConfigureAwait(false);
                    messages.Add(GitUserMessages.Describe(hardware));
                    succeeded |= hardware.Success;
                }

                InvokeOnUI(() => LastOperationMessage = string.Join(" ", messages));
                if (succeeded)
                {
                    await refreshStatusAsync().ConfigureAwait(false);
                }
            });
        }

        public Task ImportAsync() =>
            RunExchangeAsync("Importing HMI screens…", exchange.ImportAsync);

        private Task RunExchangeAsync(
            string busyMessage,
            Func<string, IProgress<string>, CancellationToken, Task<OperationResult>> operation)
        {
            return RunBusyAsync(busyMessage, async ct =>
            {
                var progress = new Progress<string>(message => InvokeOnUI(() => ApplyExportProgress(message)));
                OperationResult result = await operation(repositoryPath, progress, ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
                if (result.Success)
                {
                    await refreshStatusAsync().ConfigureAwait(false);
                }
            });
        }

        internal void ApplyExportProgress(string message)
        {
            if (!HmiExportProgress.TryParse(message, out HmiExportUpdate update))
            {
                BusyMessage = message ?? string.Empty;
                return;
            }

            HmiChecklistItem? item = null;
            foreach (HmiChecklistItem existing in Checklist)
            {
                if (existing.Key == update.Key)
                {
                    item = existing;
                    break;
                }
            }

            HmiChecklistGroup group = GroupFor(update.Device);
            if (item == null)
            {
                item = new HmiChecklistItem(update.Key, update.Device, update.Category);
                Checklist.Add(item);
                group.Add(item);
            }

            item.Apply(update);
            HasChecklist = Checklist.Count > 0;
            if (update.State == "running" && !string.Equals(expandedDevice, update.Device, StringComparison.Ordinal))
            {
                expandedDevice = update.Device ?? string.Empty;
                foreach (HmiChecklistGroup existing in ChecklistGroups)
                {
                    existing.IsExpanded = existing == group;
                }
            }
        }

        private void ClearChecklist()
        {
            Checklist.Clear();
            ChecklistGroups.Clear();
            expandedDevice = string.Empty;
            HasChecklist = false;
        }

        private void DismissChecklist()
        {
            InvokeOnUI(ClearChecklist);
        }

        private HmiChecklistGroup GroupFor(string device)
        {
            foreach (HmiChecklistGroup group in ChecklistGroups)
            {
                if (string.Equals(group.Device, device, StringComparison.Ordinal))
                {
                    return group;
                }
            }

            var created = new HmiChecklistGroup(device);
            ChecklistGroups.Add(created);
            return created;
        }

        protected override void ReportStatus(string message) => LastOperationMessage = message;

        protected override void OnBusyChanged()
        {
            InvokeOnUI(() =>
            {
                ExportCommand.RaiseCanExecuteChanged();
                ImportCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            });
        }
    }
}
