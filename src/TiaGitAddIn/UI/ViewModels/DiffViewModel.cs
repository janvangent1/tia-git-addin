using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Models;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Reporting;
using TiaGitAddIn.Services;
using TiaGitAddIn.Services.Comparison;
using TiaGitAddIn.Services.Revision;
using TiaGitAddIn.UI.Mapping;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class DiffViewModel : ViewModelBase
    {
        private readonly IGitService gitService;
        private readonly ComparisonSelectionCoordinator selectionCoordinator;
        private ObservableCollection<DiffEntryViewModel> entries = new();
        private ObservableCollection<ChangeNode> fileTree = new();
        private DiffEntryViewModel? selectedEntry;
        private string lastOperationMessage = string.Empty;
        private string reportTitle = "Diff";
        private string? currentDiffCommitHash;
        private ComparisonPresentationViewModel? currentPresentation;
        private string fileFilter = string.Empty;
        private readonly string repositoryPath;
        private const int MaxVersions = 100;

        public DiffViewModel(
            IGitService gitService,
            IPlcRevisionProvider revisionProvider,
            IPlcComparisonCoordinator comparisonCoordinator,
            IComparisonPresentationMapper mapper,
            IAddInLogger? logger = null,
            IUiDispatcher? uiDispatcher = null,
            string? repositoryPath = null)
            : base(uiDispatcher)
        {
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            this.repositoryPath = repositoryPath ?? string.Empty;
            string workspace = this.repositoryPath;
            if (revisionProvider == null) throw new ArgumentNullException(nameof(revisionProvider));
            if (comparisonCoordinator == null) throw new ArgumentNullException(nameof(comparisonCoordinator));
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));

            IAddInLogger effectiveLogger = logger ?? new NullLogger();
            selectionCoordinator = new ComparisonSelectionCoordinator(
                revisionProvider,
                comparisonCoordinator,
                mapper,
                UiDispatcher,
                viewModel => CurrentPresentation = viewModel,
                effectiveLogger);

            ExportPdfCommand = new RelayCommand(_ => ExportPdf(), _ => !IsBusy && Entries.Count > 0);
            NextFileCommand = new RelayCommand(_ => MoveFile(1), _ => !IsBusy && Entries.Count > 1);
            PreviousFileCommand = new RelayCommand(_ => MoveFile(-1), _ => !IsBusy && Entries.Count > 1);
            ShowHistoryCommand = new AsyncCommand(ShowHistoryOfSelectedFileAsync, () => !IsBusy && SelectedEntry != null);
            ChooseFilesCommand = new AsyncCommand(ChooseFilesAsync, () => !IsBusy && workspace.Length > 0);
            CloseHistoryCommand = new RelayCommand(_ => ClearFileHistory(), _ => !IsBusy && HasFileHistory);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
        }

        public sealed class FileHistorySection
        {
            public FileHistorySection(string title, ComparisonPresentationViewModel presentation)
            {
                Title = title;
                Presentation = presentation;
            }

            public string Title { get; }
            public ComparisonPresentationViewModel Presentation { get; }
        }

        private sealed class VersionStep
        {
            public VersionStep(string? commitHash, string subject, IReadOnlyList<string> paths, string focusPath)
            {
                CommitHash = commitHash;
                Subject = subject ?? string.Empty;
                Paths = paths;
                FocusPath = focusPath;
            }

            public string? CommitHash { get; }
            public string Subject { get; }
            public IReadOnlyList<string> Paths { get; }
            public string FocusPath { get; }
        }

        private sealed class NullLogger : IAddInLogger
        {
            public void Error(string message, Exception exception) { }
            public void Info(string message) { }
        }

        public ObservableCollection<DiffEntryViewModel> Entries
        {
            get => entries;
            private set => SetProperty(entries, value, updated => entries = updated);
        }

        public ObservableCollection<ChangeNode> FileTree
        {
            get => fileTree;
            private set => SetProperty(fileTree, value, updated => fileTree = updated);
        }

        public DiffEntryViewModel? SelectedEntry
        {
            get => selectedEntry;
            set
            {
                if (SetProperty(selectedEntry, value, updated => selectedEntry = updated))
                {
                    ShowHistoryCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        public string LastOperationMessage
        {
            get => lastOperationMessage;
            private set => SetProperty(lastOperationMessage, value ?? string.Empty, updated => lastOperationMessage = updated);
        }

        /// <summary>The mapped presentation for the current selection; <c>null</c> until one is applied.</summary>
        public ComparisonPresentationViewModel? CurrentPresentation
        {
            get => currentPresentation;
            private set
            {
                if (SetProperty(currentPresentation, value, updated => currentPresentation = updated))
                {
                    OnPropertyChanged(nameof(ComparisonKind));
                }
            }
        }

        public string LoadedCommitHash => currentDiffCommitHash ?? string.Empty;

        public bool IsWorkingTreeDiff => currentDiffCommitHash == null && Entries.Count > 0;

        public string FileFilter
        {
            get => fileFilter;
            set
            {
                if (SetProperty(fileFilter, value ?? string.Empty, updated => fileFilter = updated))
                {
                    FileTree = ChangeTree.Group(FilteredEntries(), entry => entry.FilePath);
                }
            }
        }

        public ObservableCollection<FileHistorySection> FileHistorySections { get; } = new ObservableCollection<FileHistorySection>();

        public bool HasFileHistory => FileHistorySections.Count > 0;

        public string ComparisonKind
        {
            get
            {
                if (CurrentPresentation == null)
                {
                    return string.Empty;
                }

                switch (CurrentPresentation.Kind)
                {
                    case ComparisonPresentationKind.LogicNetwork:
                        return "LAD";
                    case ComparisonPresentationKind.Interface:
                        return "Interface";
                    case ComparisonPresentationKind.Scl:
                        return "SCL";
                    case ComparisonPresentationKind.Unsupported:
                        return "Unsupported";
                    case ComparisonPresentationKind.Error:
                        return "Unavailable";
                    default:
                        if (CurrentPresentation.HasLimitation)
                        {
                            return "Plain text";
                        }

                        string path = SelectedEntry?.FilePath ?? string.Empty;
                        return path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? "XML" : "Text";
                }
            }
        }

        public RelayCommand ExportPdfCommand { get; }
        public RelayCommand NextFileCommand { get; }
        public RelayCommand PreviousFileCommand { get; }
        public AsyncCommand ShowHistoryCommand { get; }
        public AsyncCommand ChooseFilesCommand { get; }
        public RelayCommand CloseHistoryCommand { get; }
        public RelayCommand CancelCommand { get; }

        public Task StartVersionWalkAsync(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
            {
                LastOperationMessage = "Select a file first.";
                return Task.CompletedTask;
            }

            return RunBusyAsync("Loading file history…", async ct =>
            {
                IReadOnlyList<FileRevision> history = await gitService
                    .GetFileHistoryAsync(paths, MaxVersions, ct)
                    .ConfigureAwait(false);
                DiffResult workingTree = await gitService
                    .GetWorkingTreeDiffAsync(paths, ct)
                    .ConfigureAwait(false);

                var steps = new List<VersionStep>();
                if (workingTree.Entries.Count > 0)
                {
                    string focus = FirstPath(workingTree, paths);
                    steps.Add(new VersionStep(null, "Uncommitted changes", paths, focus));
                }

                foreach (FileRevision revision in history)
                {
                    string hash = revision.Commit?.Hash ?? string.Empty;
                    if (hash.Length == 0)
                    {
                        continue;
                    }

                    IReadOnlyList<string> stepPaths = revision.Paths != null && revision.Paths.Count > 0
                        ? revision.Paths
                        : paths;
                    steps.Add(new VersionStep(hash, revision.Commit?.Subject ?? string.Empty, stepPaths, stepPaths[0]));
                }

                if (steps.Count == 0)
                {
                    ClearFileHistory();
                    InvokeOnUI(() => LastOperationMessage = "No committed or uncommitted changes were found for the selected files.");
                    return;
                }

                var sections = new List<FileHistorySection>();
                for (int index = 0; index < steps.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    VersionStep step = steps[index];
                    int number = steps.Count - index;
                    string mark = string.IsNullOrEmpty(step.CommitHash) ? "uncommitted" : ShortHash(step.CommitHash!);
                    string progress = $"Loading version {number} of {steps.Count}…";
                    InvokeOnUI(() => BusyMessage = progress);

                    DiffResult diff = string.IsNullOrEmpty(step.CommitHash)
                        ? await gitService.GetWorkingTreeDiffAsync(step.Paths, ct).ConfigureAwait(false)
                        : await gitService.GetCommitDiffAsync(step.CommitHash!, step.Paths, ct).ConfigureAwait(false);

                    if (diff.Entries.Count == 0)
                    {
                        diff = new DiffResult
                        {
                            Entries = new List<DiffEntry>
                            {
                                new DiffEntry { FilePath = step.FocusPath, ChangeType = "M", Hunks = new List<DiffHunk>() }
                            }
                        };
                    }

                    foreach (DiffEntry entry in diff.Entries)
                    {
                        string path = string.IsNullOrWhiteSpace(entry.FilePath) ? step.FocusPath : entry.FilePath;
                        var selection = new ComparisonSelection(path, step.CommitHash, ToChangeKind(entry.ChangeType));
                        ComparisonPresentationViewModel presentation = await selectionCoordinator
                            .BuildAsync(selection, ct)
                            .ConfigureAwait(false);
                        string file = diff.Entries.Count > 1 ? " · " + path : string.Empty;
                        sections.Add(new FileHistorySection(
                            $"Version {number} of {steps.Count} · {mark} · {step.Subject}{file}",
                            presentation));
                    }
                }

                InvokeOnUI(() =>
                {
                    FileHistorySections.Clear();
                    foreach (FileHistorySection section in sections)
                    {
                        FileHistorySections.Add(section);
                    }

                    OnPropertyChanged(nameof(HasFileHistory));
                    CloseHistoryCommand.RaiseCanExecuteChanged();
                    LastOperationMessage = $"Showing {sections.Count} version(s), newest first.";
                });
            });
        }

        public Task LoadCommitDiffAsync(string commitHash)
        {
            if (string.IsNullOrWhiteSpace(commitHash)) return Task.CompletedTask;

            return RunBusyAsync("Loading diff…", async ct =>
            {
                ClearFileHistory();
                var result = await gitService.GetCommitDiffAsync(commitHash, ct).ConfigureAwait(false);
                currentDiffCommitHash = commitHash;
                ApplyDiffResult(result, $"Commit {ShortHash(commitHash)}");
            });
        }

        public Task LoadWorkingTreeDiffAsync() =>
            RunBusyAsync("Loading working tree diff…", async ct =>
            {
                ClearFileHistory();
                var result = await gitService.GetWorkingTreeDiffAsync(ct).ConfigureAwait(false);
                currentDiffCommitHash = null;
                ApplyDiffResult(result, "Working tree");
            });

        /// <summary>
        /// The genuine async boundary for a file-list selection change. Routes the entry through the
        /// cancellation-safe selection coordinator (latest-selection-wins) and updates no property other
        /// than <see cref="CurrentPresentation"/>, from inside the coordinator's own dispatcher callback.
        /// </summary>
        public Task SelectEntryAsync(DiffEntryViewModel? entry, CancellationToken cancellationToken)
        {
            if (entry == null)
            {
                return Task.CompletedTask;
            }

            ClearFileHistory();
            var selection = new ComparisonSelection(entry.FilePath, currentDiffCommitHash, ToChangeKind(entry.ChangeType));
            return selectionCoordinator.SelectAsync(selection, cancellationToken);
        }

        public Task FocusFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.CompletedTask;
            }

            DiffEntryViewModel? match = null;
            foreach (DiffEntryViewModel entry in Entries)
            {
                if (string.Equals(entry.FilePath, path, StringComparison.OrdinalIgnoreCase))
                {
                    match = entry;
                    break;
                }
            }

            if (match == null)
            {
                return Task.CompletedTask;
            }

            SelectNode(match);
            return SelectEntryAsync(match, CancellationToken.None);
        }

        private Task ShowHistoryOfSelectedFileAsync()
        {
            string? path = SelectedEntry?.FilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                LastOperationMessage = "Select a file first.";
                return Task.CompletedTask;
            }

            return StartVersionWalkAsync(new[] { path });
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

            if (!TryMakeRelative(dialog.FileNames, out IReadOnlyList<string> relative))
            {
                LastOperationMessage = "Choose files inside the repository.";
                return Task.CompletedTask;
            }

            return StartVersionWalkAsync(relative);
        }

        private bool TryMakeRelative(IEnumerable<string> fullPaths, out IReadOnlyList<string> relative)
        {
            var paths = new List<string>();
            foreach (string fullPath in fullPaths)
            {
                if (!RepositoryPaths.TryMakeRelative(repositoryPath, fullPath, out string path))
                {
                    relative = Array.Empty<string>();
                    return false;
                }

                paths.Add(path);
            }

            relative = paths;
            return paths.Count > 0;
        }

        private static string FirstPath(DiffResult diff, IReadOnlyList<string> fallback)
        {
            string? fromDiff = diff.Entries.FirstOrDefault()?.FilePath;
            if (!string.IsNullOrWhiteSpace(fromDiff))
            {
                return fromDiff!;
            }

            return fallback[0];
        }

        private void ClearFileHistory()
        {
            if (FileHistorySections.Count == 0)
            {
                return;
            }

            InvokeOnUI(() =>
            {
                FileHistorySections.Clear();
                OnPropertyChanged(nameof(HasFileHistory));
                CloseHistoryCommand.RaiseCanExecuteChanged();
            });
        }

        private static string ShortHash(string hash) =>
            hash.Substring(0, Math.Min(7, hash.Length));

        private static PlcPairChangeKind ToChangeKind(string? changeType) => changeType?.Trim().ToUpperInvariant() switch
        {
            "A" => PlcPairChangeKind.Added,
            "D" => PlcPairChangeKind.Removed,
            _ => PlcPairChangeKind.Modified,
        };

        private void ApplyDiffResult(DiffResult result, string title)
        {
            reportTitle = title;
            InvokeOnUI(() =>
            {
                Entries = new ObservableCollection<DiffEntryViewModel>(
                    result.Entries.Select(e => new DiffEntryViewModel(e)));
                FileTree = ChangeTree.Group(FilteredEntries(), entry => entry.FilePath);
                NextFileCommand.RaiseCanExecuteChanged();
                PreviousFileCommand.RaiseCanExecuteChanged();
                ChangeNode? onlyFile = ChangeTree.OnlyFile(FileTree);
                if (onlyFile != null)
                {
                    onlyFile.IsSelected = true;
                }
                else
                {
                    CurrentPresentation = null;
                }

                SelectedEntry = Entries.FirstOrDefault();
                LastOperationMessage = $"{title} · {result.Entries.Count} file(s) changed.";
                ExportPdfCommand.RaiseCanExecuteChanged();
            });
        }

        private void ExportPdf()
        {
            if (Entries.Count == 0)
            {
                LastOperationMessage = "There is no diff to export. Select a commit or use Show changes first.";
                return;
            }

            SaveFileDialog dialog = new()
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                FileName = "git-diff.pdf",
                Title = "Export diff",
                AddExtension = true,
                DefaultExt = ".pdf"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FileName))
            {
                return;
            }

            try
            {
                PdfReport report = BuildPdfReport();
                PdfReportWriter.Write(report, dialog.FileName);
                LastOperationMessage = "Exported diff to " + dialog.FileName;
            }
            catch (Exception ex)
            {
                LastOperationMessage = "Unable to export the PDF. " + ex.Message;
            }
        }

        private PdfReport BuildPdfReport()
        {
            IReadOnlyList<string> files = Entries
                .Select(entry => entry.ChangeType + "  " + entry.FilePath)
                .ToList();
            string? selectedFile = SelectedEntry?.FilePath;

            if (CurrentPresentation is LadComparisonViewModel ladder)
            {
                var lines = new List<DiffPdfTextLine>(DiffPdfContent.Ladder(ladder.Content.Networks));
                lines.Add(new DiffPdfTextLine("#", "Interface"));
                lines.AddRange(FlattenInterface(ladder.Content.InterfaceComparison));
                return DiffPdfReport.Create(
                    reportTitle,
                    files,
                    selectedFile,
                    false,
                    lines,
                    false,
                    "Each network is written as instructions. The ladder drawing itself is not pasted in.");
            }

            if (CurrentPresentation is TextComparisonViewModel text)
            {
                if (text.HasScreenPreview)
                {
                    return DiffPdfReport.Create(
                        reportTitle,
                        files,
                        selectedFile,
                        false,
                        DiffPdfContent.Screen(text),
                        false,
                        "Highlighted buttons and I/O fields. Unchanged items are left out.");
                }

                IReadOnlyList<DiffPdfTextLine> lines = text.Lines
                    .Select(line => new DiffPdfTextLine(line.StatusLabel, line.Text))
                    .ToList();
                return DiffPdfReport.Create(reportTitle, files, selectedFile, false, lines, text.IsTruncated);
            }

            if (CurrentPresentation is InterfaceComparisonViewModel interfaceComparison)
            {
                return DiffPdfReport.Create(
                    reportTitle,
                    files,
                    selectedFile,
                    false,
                    FlattenInterface(interfaceComparison),
                    false);
            }

            return DiffPdfReport.Create(
                reportTitle,
                files,
                selectedFile,
                false,
                null,
                false,
                CurrentPresentation == null
                    ? "The file comparison is not loaded yet, so only the file list is included."
                    : null);
        }

        private static IReadOnlyList<DiffPdfTextLine> FlattenInterface(InterfaceComparisonViewModel comparison)
        {
            var lines = new List<DiffPdfTextLine>();
            foreach (InterfaceSectionRowViewModel section in comparison.Sections)
            {
                lines.Add(new DiffPdfTextLine(" ", section.Name));
                foreach (InterfaceMemberRowViewModel member in section.Members)
                {
                    AppendInterfaceMember(lines, member, 1);
                }
            }

            return lines;
        }

        private static void AppendInterfaceMember(
            List<DiffPdfTextLine> lines,
            InterfaceMemberRowViewModel member,
            int depth)
        {
            string marker = member.StatusLabel == "Added"
                ? "+"
                : member.StatusLabel == "Removed"
                    ? "-"
                    : " ";
            string indent = new string(' ', depth * 2);
            lines.Add(new DiffPdfTextLine(
                marker,
                indent + member.Name + "  " + member.LeftDatatype + " -> " + member.RightDatatype));
            foreach (InterfaceMemberRowViewModel child in member.Children)
            {
                AppendInterfaceMember(lines, child, depth + 1);
            }
        }

        private IEnumerable<DiffEntryViewModel> FilteredEntries()
        {
            string filter = fileFilter.Trim();
            if (filter.Length == 0)
            {
                return Entries;
            }

            return Entries.Where(entry =>
                entry.FilePath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void MoveFile(int direction)
        {
            List<DiffEntryViewModel> files = FilteredEntries().ToList();
            if (files.Count == 0)
            {
                return;
            }

            int index = SelectedEntry == null ? -1 : files.FindIndex(entry =>
                string.Equals(entry.FilePath, SelectedEntry.FilePath, StringComparison.OrdinalIgnoreCase));
            int next = index < 0 ? 0 : index + direction;
            if (next < 0)
            {
                next = files.Count - 1;
            }
            else if (next >= files.Count)
            {
                next = 0;
            }

            SelectNode(files[next]);
            _ = SelectEntryAsync(files[next], CancellationToken.None);
        }

        private void SelectNode(DiffEntryViewModel entry)
        {
            SelectedEntry = entry;
            foreach (ChangeNode node in Flatten(FileTree))
            {
                node.IsSelected = node.Item is DiffEntryViewModel item &&
                    string.Equals(item.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static IEnumerable<ChangeNode> Flatten(IEnumerable<ChangeNode> nodes)
        {
            foreach (ChangeNode node in nodes)
            {
                yield return node;
                if (node.Children != null)
                {
                    foreach (ChangeNode child in Flatten(node.Children))
                    {
                        yield return child;
                    }
                }
            }
        }

        protected override void ReportStatus(string message) => LastOperationMessage = message;

        protected override void OnBusyChanged()
        {
            InvokeOnUI(() =>
            {
                ExportPdfCommand.RaiseCanExecuteChanged();
                NextFileCommand.RaiseCanExecuteChanged();
                PreviousFileCommand.RaiseCanExecuteChanged();
                ShowHistoryCommand.RaiseCanExecuteChanged();
                ChooseFilesCommand.RaiseCanExecuteChanged();
                CloseHistoryCommand.RaiseCanExecuteChanged();
                CancelCommand.RaiseCanExecuteChanged();
            });
        }
    }

    public sealed class DiffEntryViewModel
    {
        public DiffEntryViewModel(DiffEntry entry)
        {
            Entry = entry;
            FilePath = entry.FilePath;
            FileName = Path.GetFileName(entry.FilePath);
            ChangeType = entry.ChangeType ?? string.Empty;
        }

        public DiffEntry Entry { get; }
        public string FilePath { get; }
        public string FileName { get; }
        public string ChangeType { get; }
    }
}
