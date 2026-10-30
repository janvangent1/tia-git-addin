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
        private string? rangeLeftHash;
        private ComparisonPresentationViewModel? currentPresentation;
        private string fileFilter = string.Empty;
        private readonly string repositoryPath;
        private bool isReviewingCommit;
        private ReviewBlock? selectedReviewBlock;
        private FileHistoryLine? selectedHistoryLine;
        private FileHistoryLine? loadedHistoryLine;
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
            ToggleReviewCommand = new RelayCommand(_ => ToggleReview(), _ => !IsBusy && ReviewBlocks.Count > 0);
            NextBlockCommand = new RelayCommand(_ => MoveBlock(1), _ => !IsBusy && IsReviewingCommit && ReviewBlocks.Count > 1);
            PreviousBlockCommand = new RelayCommand(_ => MoveBlock(-1), _ => !IsBusy && IsReviewingCommit && ReviewBlocks.Count > 1);
            CancelCommand = new RelayCommand(_ => RequestCancel(), _ => IsBusy);
        }

        public sealed class FileHistoryLine
        {
            public FileHistoryLine(string title, string regions, string? commitHash, string focusPath)
            {
                Title = title;
                Regions = regions;
                CommitHash = commitHash;
                FocusPath = focusPath;
            }

            public string Title { get; }
            public string Regions { get; }
            public string? CommitHash { get; }
            public string FocusPath { get; }
        }

        public sealed class ReviewBlock
        {
            public ReviewBlock(string caption, DiffEntryViewModel entry)
            {
                Caption = caption;
                Entry = entry;
            }

            public string Caption { get; }
            public DiffEntryViewModel Entry { get; }
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

        public ObservableCollection<FileHistoryLine> FileHistoryLines { get; } = new ObservableCollection<FileHistoryLine>();

        public bool HasFileHistory => FileHistoryLines.Count > 0;

        public FileHistoryLine? SelectedHistoryLine
        {
            get => selectedHistoryLine;
            set => SetProperty(selectedHistoryLine, value, updated => selectedHistoryLine = updated);
        }

        public ObservableCollection<ReviewBlock> ReviewBlocks { get; } = new ObservableCollection<ReviewBlock>();

        public bool HasReviewBlocks => ReviewBlocks.Count > 0;

        public bool IsReviewingCommit
        {
            get => isReviewingCommit;
            private set
            {
                if (SetProperty(isReviewingCommit, value, updated => isReviewingCommit = updated))
                {
                    OnPropertyChanged(nameof(ReviewButtonText));
                }
            }
        }

        public string ReviewButtonText => IsReviewingCommit ? "All files" : "Review this commit";

        public ReviewBlock? SelectedReviewBlock
        {
            get => selectedReviewBlock;
            set => SetProperty(selectedReviewBlock, value, updated => selectedReviewBlock = updated);
        }

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
        public RelayCommand ToggleReviewCommand { get; }
        public RelayCommand NextBlockCommand { get; }
        public RelayCommand PreviousBlockCommand { get; }
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

                var lines = new List<FileHistoryLine>();
                for (int index = 0; index < steps.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    VersionStep step = steps[index];
                    string mark = string.IsNullOrEmpty(step.CommitHash) ? "Uncommitted" : ShortHash(step.CommitHash!);
                    InvokeOnUI(() => BusyMessage = $"Reading version {index + 1} of {steps.Count}…");

                    DiffResult diff = string.IsNullOrEmpty(step.CommitHash)
                        ? await gitService.GetWorkingTreeDiffAsync(step.Paths, ct).ConfigureAwait(false)
                        : await gitService.GetCommitDiffAsync(step.CommitHash!, step.Paths, ct).ConfigureAwait(false);
                    int regions = CountRegions(diff);
                    string regionText = regions == 1 ? "1 region" : regions + " regions";
                    string subject = step.Subject.Length > 0 ? step.Subject : "No message";
                    lines.Add(new FileHistoryLine($"{mark} · {subject}", regionText, step.CommitHash, step.FocusPath));
                }

                FileHistoryLine first = lines[0];
                InvokeOnUI(() =>
                {
                    FileHistoryLines.Clear();
                    foreach (FileHistoryLine line in lines)
                    {
                        FileHistoryLines.Add(line);
                    }

                    SelectedHistoryLine = first;
                    OnPropertyChanged(nameof(HasFileHistory));
                    CloseHistoryCommand.RaiseCanExecuteChanged();
                    LastOperationMessage = $"Showing {lines.Count} version(s), newest first. Open one to compare it.";
                });

                await LoadHistoryPresentationAsync(first, ct).ConfigureAwait(false);
            });
        }

        public Task OpenHistoryLineAsync(FileHistoryLine? line)
        {
            if (line == null || ReferenceEquals(line, loadedHistoryLine))
            {
                return Task.CompletedTask;
            }

            return RunBusyAsync("Loading version…", ct => LoadHistoryPresentationAsync(line, ct));
        }

        public Task LoadRangeDiffAsync(string leftCommitHash, string rightCommitHash)
        {
            if (string.IsNullOrWhiteSpace(leftCommitHash) || string.IsNullOrWhiteSpace(rightCommitHash))
            {
                return Task.CompletedTask;
            }

            return RunBusyAsync("Comparing commits…", async ct =>
            {
                ClearFileHistory();
                DiffResult result = await gitService.GetRangeDiffAsync(leftCommitHash, rightCommitHash, ct).ConfigureAwait(false);
                currentDiffCommitHash = rightCommitHash;
                rangeLeftHash = leftCommitHash;
                ApplyDiffResult(result, "Commits " + ShortHash(leftCommitHash) + " .. " + ShortHash(rightCommitHash));
            });
        }

        public Task ShowConflictAsync(string path, string? ours, string? theirs)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return Task.CompletedTask;
            }

            return RunBusyAsync("Comparing both sides…", async ct =>
            {
                ClearFileHistory();
                rangeLeftHash = null;
                currentDiffCommitHash = null;
                await selectionCoordinator.CompareTextsAsync(path, ours, theirs, ct).ConfigureAwait(false);
                InvokeOnUI(() => LastOperationMessage = "Showing both sides of " + path + ". Fix the file in TIA, then mark it resolved.");
            });
        }

        public Task LoadCommitDiffAsync(string commitHash)
        {
            if (string.IsNullOrWhiteSpace(commitHash)) return Task.CompletedTask;

            return RunBusyAsync("Loading diff…", async ct =>
            {
                ClearFileHistory();
                rangeLeftHash = null;
                var result = await gitService.GetCommitDiffAsync(commitHash, ct).ConfigureAwait(false);
                currentDiffCommitHash = commitHash;
                ApplyDiffResult(result, $"Commit {ShortHash(commitHash)}");
            });
        }

        public Task LoadWorkingTreeDiffAsync() =>
            RunBusyAsync("Loading working tree diff…", async ct =>
            {
                ClearFileHistory();
                rangeLeftHash = null;
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
            var selection = new ComparisonSelection(entry.FilePath, currentDiffCommitHash, ToChangeKind(entry.ChangeType), rangeLeftHash);
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

        private async Task LoadHistoryPresentationAsync(FileHistoryLine line, CancellationToken cancellationToken)
        {
            var selection = new ComparisonSelection(line.FocusPath, line.CommitHash, PlcPairChangeKind.Modified);
            ComparisonPresentationViewModel presentation = await selectionCoordinator
                .BuildAsync(selection, cancellationToken)
                .ConfigureAwait(false);
            InvokeOnUI(() =>
            {
                loadedHistoryLine = line;
                SelectedHistoryLine = line;
                CurrentPresentation = presentation;
            });
        }

        private static int CountRegions(DiffResult diff)
        {
            int regions = 0;
            foreach (DiffEntry entry in diff.Entries)
            {
                int hunks = entry.Hunks?.Count ?? 0;
                regions += hunks > 0 ? hunks : 1;
            }

            return regions > 0 ? regions : 1;
        }

        private void ToggleReview()
        {
            IsReviewingCommit = !IsReviewingCommit;
            NextBlockCommand.RaiseCanExecuteChanged();
            PreviousBlockCommand.RaiseCanExecuteChanged();
            if (!IsReviewingCommit || ReviewBlocks.Count == 0)
            {
                return;
            }

            ReviewBlock first = ReviewBlocks[0];
            SelectedReviewBlock = first;
            SelectedEntry = first.Entry;
            _ = SelectEntryAsync(first.Entry, CancellationToken.None);
        }

        private void MoveBlock(int direction)
        {
            if (ReviewBlocks.Count == 0)
            {
                return;
            }

            int index = SelectedReviewBlock == null ? -1 : ReviewBlocks.IndexOf(SelectedReviewBlock);
            int next = index + direction;
            if (next < 0 || next >= ReviewBlocks.Count)
            {
                return;
            }

            ReviewBlock block = ReviewBlocks[next];
            SelectedReviewBlock = block;
            SelectedEntry = block.Entry;
            SelectNode(block.Entry);
            _ = SelectEntryAsync(block.Entry, CancellationToken.None);
        }

        private static string ReviewCaption(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            string[] parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Equals("Software units", StringComparison.OrdinalIgnoreCase))
                {
                    return parts[i + 1] + "   " + name;
                }
            }

            string folder = parts.Length >= 2 ? parts[parts.Length - 2] : string.Empty;
            return folder.Length > 0 ? folder + "   " + name : name;
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
            if (FileHistoryLines.Count == 0 && loadedHistoryLine == null)
            {
                return;
            }

            InvokeOnUI(() =>
            {
                FileHistoryLines.Clear();
                SelectedHistoryLine = null;
                loadedHistoryLine = null;
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
                ReviewBlocks.Clear();
                foreach (DiffEntryViewModel entry in Entries)
                {
                    if (entry.FilePath.EndsWith(".scl", StringComparison.OrdinalIgnoreCase))
                    {
                        ReviewBlocks.Add(new ReviewBlock(ReviewCaption(entry.FilePath), entry));
                    }
                }

                if (ReviewBlocks.Count == 0)
                {
                    IsReviewingCommit = false;
                }

                OnPropertyChanged(nameof(HasReviewBlocks));
                ToggleReviewCommand.RaiseCanExecuteChanged();
                NextBlockCommand.RaiseCanExecuteChanged();
                PreviousBlockCommand.RaiseCanExecuteChanged();
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
                ToggleReviewCommand.RaiseCanExecuteChanged();
                NextBlockCommand.RaiseCanExecuteChanged();
                PreviousBlockCommand.RaiseCanExecuteChanged();
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
