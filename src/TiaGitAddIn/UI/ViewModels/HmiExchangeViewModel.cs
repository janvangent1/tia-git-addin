using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
        private bool importHmi;
        private bool importHardware;
        private readonly Stopwatch elapsed = new Stopwatch();
        private Timer? elapsedTimer;
        private int elapsedGeneration;
        private string elapsedText = string.Empty;
        private bool lastOperationFailed;

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
            ImportCommand = new AsyncCommand(() => ImportAsync(), () => !IsBusy && (ImportHmi || ImportHardware));
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

        public string ElapsedText
        {
            get => elapsedText;
            private set
            {
                if (SetProperty(ref elapsedText, value))
                {
                    OnPropertyChanged(nameof(HasElapsed));
                }
            }
        }

        public bool HasElapsed => elapsedText.Length > 0;

        internal static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            if (elapsed.TotalHours >= 1)
            {
                return string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}:{1:00}:{2:00}",
                    (int)elapsed.TotalHours,
                    elapsed.Minutes,
                    elapsed.Seconds);
            }

            return string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "{0}:{1:00}",
                (int)elapsed.TotalMinutes,
                elapsed.Seconds);
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

        public bool ImportHmi
        {
            get => importHmi;
            set
            {
                if (SetProperty(ref importHmi, value))
                {
                    ImportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool ImportHardware
        {
            get => importHardware;
            set
            {
                if (SetProperty(ref importHardware, value))
                {
                    ImportCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncCommand ExportCommand { get; }

        public AsyncCommand ImportCommand { get; }

        public RelayCommand CancelCommand { get; }

        public RelayCommand DismissChecklistCommand { get; }

        public Func<string, bool>? Confirm { get; set; }

        public Action<string>? ShowError { get; set; }

        public bool LastOperationFailed
        {
            get => lastOperationFailed;
            private set => SetProperty(ref lastOperationFailed, value);
        }

        public Func<ExportCategorySet>? Categories { get; set; }

        public Task ExportAsync()
        {
            bool includeHmi = ExportHmi;
            bool includeHardware = ExportHardware;
            if (!includeHmi && !includeHardware)
            {
                return Task.CompletedTask;
            }

            ExportCategorySet.Pending = Categories?.Invoke() ?? ExportCategorySet.All;
            InvokeOnUI(() =>
            {
                ClearChecklist();
                StartElapsed();
            });
            string busyMessage = includeHmi && includeHardware
                ? "Transferring to Git…"
                : includeHardware
                    ? "Transferring hardware to Git…"
                    : "Transferring HMI to Git…";
            return RunBusyAsync(busyMessage, async ct =>
            {
                IProgress<string> progress = new ChecklistProgress(ReportChecklist);
                var messages = new System.Collections.Generic.List<string>();
                bool succeeded = false;
                ExportIssues.Begin();
                try
                {
                    if (includeHmi)
                    {
                        await RunExchangeStep("HMI", () => exchange.ExportAsync(repositoryPath, progress, ct), messages, ct, succeededFlag => succeeded = succeeded || succeededFlag).ConfigureAwait(false);
                    }

                    if (includeHardware && !ExportIssues.SessionClosed)
                    {
                        await RunExchangeStep("Hardware", () => exchange.ExportHardwareAsync(repositoryPath, progress, ct), messages, ct, succeededFlag => succeeded = succeeded || succeededFlag).ConfigureAwait(false);
                    }

                    if (succeeded)
                    {
                        await refreshStatusAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    ExportIssues finished = ExportIssues.End();
                    bool cancelled = ct.IsCancellationRequested;
                    InvokeOnUI(() => PublishOutcome(messages, finished, cancelled));
                    ExportCategorySet.Pending = null;
                }
            });
        }

        private static string ImportPrompt(bool hmi, bool hardware, IReadOnlyList<string> differences)
        {
            string target = hmi && hardware ? "HMI and hardware files" : hardware ? "hardware files" : "HMI files";
            string prompt = "Import writes the checked " + target + " onto the open project. A checkout only puts files on disk. Continue?";
            if (differences == null || differences.Count == 0)
            {
                return prompt;
            }

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("Import replaces objects in the open project. It does not merge two edits.");
            lines.AppendLine("These screens and tag tables differ:");
            int shown = Math.Min(differences.Count, 30);
            for (int index = 0; index < shown; index++)
            {
                lines.AppendLine("- " + differences[index]);
            }

            if (differences.Count > shown)
            {
                lines.AppendLine("and " + (differences.Count - shown).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more.");
            }

            lines.Append("Continue?");
            return lines.ToString();
        }

        public async Task ImportAsync()
        {
            bool includeHmi = ImportHmi;
            bool includeHardware = ImportHardware;
            if (!includeHmi && !includeHardware)
            {
                return;
            }

            IReadOnlyList<string> differences = null;
            if (includeHmi)
            {
                InvokeOnUI(() => LastOperationMessage = "Comparing screens and tag tables with the open project…");
                try
                {
                    differences = await exchange.ListImportDifferencesAsync(repositoryPath).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    differences = null;
                }
            }

            if (Confirm != null && !Confirm(ImportPrompt(includeHmi, includeHardware, differences)))
            {
                return;
            }

            InvokeOnUI(() =>
            {
                ClearChecklist();
                StartElapsed();
            });
            string busyMessage = includeHmi && includeHardware
                ? "Transferring to the project…"
                : includeHardware
                    ? "Transferring hardware to the project…"
                    : "Transferring HMI to the project…";
            await RunBusyAsync(busyMessage, async ct =>
            {
                IProgress<string> progress = new ChecklistProgress(ReportChecklist);
                var messages = new System.Collections.Generic.List<string>();
                bool succeeded = false;
                ExportIssues.Begin();
                try
                {
                    if (includeHmi)
                    {
                        await RunExchangeStep("HMI", () => exchange.ImportAsync(repositoryPath, progress, ct), messages, ct, succeededFlag => succeeded = succeeded || succeededFlag).ConfigureAwait(false);
                    }

                    if (includeHardware && !ExportIssues.SessionClosed)
                    {
                        await RunExchangeStep("Hardware", () => exchange.ImportHardwareAsync(repositoryPath, progress, ct), messages, ct, succeededFlag => succeeded = succeeded || succeededFlag).ConfigureAwait(false);
                    }

                    if (succeeded)
                    {
                        await refreshStatusAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    ExportIssues finished = ExportIssues.End();
                    bool cancelled = ct.IsCancellationRequested;
                    InvokeOnUI(() => PublishOutcome(messages, finished, cancelled));
                }
            });
        }

        /// <summary>
        /// Applies a checklist line before returning. <see cref="Progress{T}"/> would only post it,
        /// so a hardware row would stay unfinished until the whole export returned.
        /// </summary>
        private void ReportChecklist(string message)
        {
            if (UiDispatcher.CheckAccess())
            {
                ApplyExportProgress(message);
                return;
            }

            UiDispatcher.Invoke(() => ApplyExportProgress(message));
        }

        private sealed class ChecklistProgress : IProgress<string>
        {
            private readonly Action<string> apply;

            public ChecklistProgress(Action<string> apply)
            {
                this.apply = apply;
            }

            public void Report(string value) => apply(value);
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

        private void StartElapsed()
        {
            StopElapsedTimer();
            int generation = ++elapsedGeneration;
            elapsed.Restart();
            ElapsedText = "Elapsed " + FormatElapsed(TimeSpan.Zero);
            elapsedTimer = new Timer(_ => InvokeOnUI(() => RefreshElapsed(generation)), null, 1000, 1000);
        }

        private void RefreshElapsed(int generation)
        {
            if (generation != elapsedGeneration || !elapsed.IsRunning)
            {
                return;
            }

            ElapsedText = "Elapsed " + FormatElapsed(elapsed.Elapsed);
        }

        private void FinishElapsed(bool cancelled)
        {
            elapsed.Stop();
            elapsedGeneration++;
            StopElapsedTimer();
            ElapsedText = (cancelled ? "Stopped after " : "Finished in ") + FormatElapsed(elapsed.Elapsed);
        }

        private void StopElapsedTimer()
        {
            Timer? timer = elapsedTimer;
            elapsedTimer = null;
            timer?.Dispose();
        }

        private void ClearChecklist()
        {
            StopElapsedTimer();
            elapsed.Reset();
            elapsedGeneration++;
            ElapsedText = string.Empty;
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

        private async Task RunExchangeStep(
            string name,
            Func<Task<OperationResult>> operation,
            ICollection<string> messages,
            CancellationToken ct,
            Action<bool> markSucceeded)
        {
            int before = ExportIssues.Count;
            try
            {
                OperationResult result = await operation().ConfigureAwait(false);
                string described = GitUserMessages.Describe(result);
                messages.Add(described);
                if (result.Success)
                {
                    markSucceeded(true);
                    return;
                }

                if (ExportIssues.Count == before && !string.IsNullOrWhiteSpace(described))
                {
                    ExportIssues.Note(name, described);
                }

                ct.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExportIssues.Remember(name, ex);
                messages.Add(GitUserMessages.DescribeException(ex.GetBaseException().Message));
            }
        }

        private void PublishOutcome(IReadOnlyList<string> messages, ExportIssues issues, bool cancelled)
        {
            if (elapsed.IsRunning)
            {
                FinishElapsed(cancelled);
            }

            int issueCount = issues == null ? 0 : issues.IssueCount;
            if (cancelled)
            {
                LastOperationFailed = false;
                LastOperationMessage = "Cancelled. " + ElapsedText;
            }
            else
            {
                LastOperationFailed = issueCount > 0;
                string status = string.Join(" ", messages);
                if (issueCount > 0)
                {
                    status += " " + issueCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + (issueCount == 1 ? " issue." : " issues.");
                }

                LastOperationMessage = (status + " " + ElapsedText + ".").Trim();
            }

            if (issueCount == 0)
            {
                return;
            }

            string heading = "Transfer finished with " + issueCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (issueCount == 1 ? " issue." : " issues.");
            string report = issues.Format(heading);
            HmiExportTrace.Info(report);
            ShowError?.Invoke(report);
        }

        protected override void ReportStatus(string message)
        {
            bool cancelled = string.Equals(message, "Cancelled.", StringComparison.Ordinal);
            LastOperationFailed = !cancelled;
            LastOperationMessage = message ?? string.Empty;
            if (!cancelled && !string.IsNullOrWhiteSpace(message))
            {
                ShowError?.Invoke(message);
            }
        }

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
