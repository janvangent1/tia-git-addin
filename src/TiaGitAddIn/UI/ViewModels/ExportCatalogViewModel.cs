using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;

namespace TiaGitAddIn.UI.ViewModels
{
    /// <summary>
    /// Shows the HMI and hardware export stored in the commit selected on History.
    /// </summary>
    public sealed class ExportCatalogViewModel : ViewModelBase
    {
        private readonly IGitService gitService;
        private ObservableCollection<ChangeNode> groups = new();
        private string heading = "Select a commit on the History page.";
        private string summary = "The export in that commit will be listed here.";
        private CancellationTokenSource? loadCts;

        public ExportCatalogViewModel(IGitService gitService, IUiDispatcher? uiDispatcher = null)
            : base(uiDispatcher)
        {
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        }

        public ObservableCollection<ChangeNode> Groups
        {
            get => groups;
            private set => SetProperty(groups, value, updated => groups = updated);
        }

        public string Heading
        {
            get => heading;
            private set => SetProperty(heading, value ?? string.Empty, updated => heading = updated);
        }

        public string Summary
        {
            get => summary;
            private set => SetProperty(summary, value ?? string.Empty, updated => summary = updated);
        }

        public Task ShowAsync(CommitInfo? commit)
        {
            loadCts?.Cancel();
            loadCts = new CancellationTokenSource();
            return LoadAsync(commit, loadCts.Token);
        }

        private async Task LoadAsync(CommitInfo? commit, CancellationToken ct)
        {
            if (commit == null || string.IsNullOrWhiteSpace(commit.Hash))
            {
                InvokeOnUI(() =>
                {
                    Heading = "Select a commit on the History page.";
                    Summary = "The export in that commit will be listed here.";
                    Groups = new ObservableCollection<ChangeNode>();
                });
                return;
            }

            string hash = commit.Hash;
            string shortHash = hash.Length <= 7 ? hash : hash.Substring(0, 7);
            InvokeOnUI(() =>
            {
                Heading = shortHash + "  " + (commit.Subject ?? string.Empty);
                Summary = "Reading the export…";
            });

            try
            {
                IReadOnlyList<string> files = await gitService.ListExportFilesAsync(hash, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                InvokeOnUI(() =>
                {
                    Groups = ChangeTree.Group(files, path => path);
                    Summary = Describe(files);
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                InvokeOnUI(() =>
                {
                    Groups = new ObservableCollection<ChangeNode>();
                    Summary = "Unable to read the export in this commit. " + ex.Message;
                });
            }
        }

        private static string Describe(IReadOnlyList<string> files)
        {
            int hmi = 0;
            int hardware = 0;
            foreach (string path in files ?? Array.Empty<string>())
            {
                string normalized = (path ?? string.Empty).Replace('\\', '/');
                if (normalized.IndexOf("HmiExport/", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.StartsWith("HmiExport", StringComparison.OrdinalIgnoreCase))
                {
                    hmi++;
                }
                else if (normalized.IndexOf("HardwareExport/", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalized.StartsWith("HardwareExport", StringComparison.OrdinalIgnoreCase))
                {
                    hardware++;
                }
            }

            if (hmi == 0 && hardware == 0)
            {
                return "This commit has no HMI or hardware export.";
            }

            return hmi.ToString(CultureInfo.InvariantCulture) + " HMI file"
                + (hmi == 1 ? string.Empty : "s")
                + " and "
                + hardware.ToString(CultureInfo.InvariantCulture) + " hardware file"
                + (hardware == 1 ? string.Empty : "s")
                + ".";
        }
    }
}
