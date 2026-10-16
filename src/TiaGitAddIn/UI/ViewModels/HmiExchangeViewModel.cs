using System;
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
            ExportCommand = new AsyncCommand(() => ExportAsync(), () => !IsBusy);
            ImportCommand = new AsyncCommand(() => ImportAsync(), () => !IsBusy);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value, updated => lastOperationMessage = updated);
        }

        public AsyncCommand ExportCommand { get; }

        public AsyncCommand ImportCommand { get; }

        public RelayCommand CancelCommand { get; }

        public Task ExportAsync() =>
            RunExchangeAsync("Exporting HMI screens…", exchange.ExportAsync);

        public Task ImportAsync() =>
            RunExchangeAsync("Importing HMI screens…", exchange.ImportAsync);

        private Task RunExchangeAsync(
            string busyMessage,
            Func<string, IProgress<string>, CancellationToken, Task<OperationResult>> operation)
        {
            return RunBusyAsync(busyMessage, async ct =>
            {
                var progress = new Progress<string>(message => InvokeOnUI(() => BusyMessage = message));
                OperationResult result = await operation(repositoryPath, progress, ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = GitUserMessages.Describe(result));
                if (result.Success)
                {
                    await refreshStatusAsync().ConfigureAwait(false);
                }
            });
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
