using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Models.Hmi;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.ViewModels.Comparison
{
    /// <summary>
    /// One line of a <see cref="TextComparisonViewModel"/>. <see cref="StatusLabel"/> gives Added/Removed/
    /// Omitted lines a visible marker so the change is never conveyed by background color alone.
    /// </summary>
    public sealed class TextDiffLineViewModel
    {
        public TextDiffLineViewModel(TextDiffLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            Kind = line.Kind;
            LeftLineNumber = line.LeftLineNumber;
            RightLineNumber = line.RightLineNumber;
            Text = line.Text;
            StatusLabel = ToStatusLabel(line.Kind);
        }

        public TextDiffLineKind Kind { get; }
        public int? LeftLineNumber { get; }
        public int? RightLineNumber { get; }
        public string Text { get; }
        public string StatusLabel { get; }

        private static string ToStatusLabel(TextDiffLineKind kind) => kind switch
        {
            TextDiffLineKind.Added => "+",
            TextDiffLineKind.Removed => "−",
            TextDiffLineKind.Omitted => "…",
            _ => string.Empty,
        };
    }

    /// <summary>Focused view model for <see cref="TextPresentation"/>: the bounded line-by-line diff.</summary>
    public sealed class TextComparisonViewModel : ComparisonPresentationViewModel, INotifyPropertyChanged
    {
        private readonly int[] changeStarts;
        private readonly int[] tagChangeIndexes;
        private int currentChangeIndex = -1;
        private TextDiffLineViewModel? currentChangeLine;
        private HmiTagPreviewRow? currentTagRow;
        private double screenZoom = 1;
        private string screenHoverText = "Hover a highlighted item to see what changed.";

        public TextComparisonViewModel(
            TextPresentation presentation,
            ComparisonViewModelMetadata metadata,
            HmiFilePreview? preview = null)
            : base(ComparisonPresentationKind.Text, metadata)
        {
            if (presentation == null) throw new ArgumentNullException(nameof(presentation));

            Lines = presentation.Lines.Select(l => new TextDiffLineViewModel(l)).ToArray();
            IsTruncated = presentation.IsTruncated;
            UsedLinearFallback = presentation.UsedLinearFallback;
            ScreenPanes = HmiPreviewBuilder.Screens(preview);
            TagRows = preview != null && preview.IsTagTable ? preview.Tags : Array.Empty<HmiTagPreviewRow>();
            IsPlcTable = preview != null && preview.IsPlcTable;
            changeStarts = FindChangeStarts(Lines);
            tagChangeIndexes = FindTagChanges(TagRows);
            PreviousChangeCommand = new RelayCommand(_ => GoToPreviousChange(), _ => CanGoToPreviousChange);
            NextChangeCommand = new RelayCommand(_ => GoToNextChange(), _ => CanGoToNextChange);
            ScreenZoomOutCommand = new RelayCommand(_ => ScreenZoom = Math.Max(0.5, ScreenZoom - 0.25));
            ScreenZoomInCommand = new RelayCommand(_ => ScreenZoom = Math.Min(3, ScreenZoom + 0.25));
            ScreenZoomResetCommand = new RelayCommand(_ => ScreenZoom = 1);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<TextDiffLineViewModel> Lines { get; }
        public bool IsTruncated { get; }
        public bool UsedLinearFallback { get; }
        public IReadOnlyList<HmiScreenPaneViewModel> ScreenPanes { get; }
        public IReadOnlyList<HmiTagPreviewRow> TagRows { get; }
        public bool HasScreenPreview => ScreenPanes.Count > 0;
        public bool HasTagPreview => TagRows.Count > 0;
        public bool IsPlcTable { get; }
        public bool HasHmiTagPreview => HasTagPreview && !IsPlcTable;
        public bool ShowTextLines => !HasScreenPreview && !HasTagPreview;
        public bool ShowChangeNavigation => ShowTextLines || HasTagPreview;
        public bool ShowTruncationBanner => IsTruncated && ShowTextLines;

        /// <summary>The first line of the changed block last jumped to, or null before any jump.</summary>
        public TextDiffLineViewModel? CurrentChangeLine => currentChangeLine;

        public HmiTagPreviewRow? CurrentTagRow => currentTagRow;

        public string ChangePositionText
        {
            get
            {
                int count = JumpCount;
                if (count == 0)
                {
                    return "No changes";
                }

                return currentChangeIndex < 0
                    ? count + " changes"
                    : "Change " + (currentChangeIndex + 1) + " of " + count;
            }
        }

        private int JumpCount => HasTagPreview ? tagChangeIndexes.Length : changeStarts.Length;

        public RelayCommand PreviousChangeCommand { get; }
        public RelayCommand NextChangeCommand { get; }
        public RelayCommand ScreenZoomOutCommand { get; }
        public RelayCommand ScreenZoomInCommand { get; }
        public RelayCommand ScreenZoomResetCommand { get; }

        public double ScreenZoom
        {
            get => screenZoom;
            private set
            {
                if (Math.Abs(screenZoom - value) < 0.001)
                {
                    return;
                }

                screenZoom = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ScreenZoomText));
            }
        }

        public string ScreenZoomText =>
            Math.Round(ScreenZoom * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        public string ScreenHoverText => screenHoverText;

        public void ShowScreenHover(string text)
        {
            string next = string.IsNullOrWhiteSpace(text) ? string.Empty : text;
            if (screenHoverText == next)
            {
                return;
            }

            screenHoverText = next;
            OnPropertyChanged(nameof(ScreenHoverText));
        }

        private bool CanGoToPreviousChange => currentChangeIndex > 0;

        private bool CanGoToNextChange => JumpCount > 0 && currentChangeIndex < JumpCount - 1;

        private void GoToNextChange()
        {
            if (!CanGoToNextChange)
            {
                return;
            }

            currentChangeIndex++;
            PublishCurrentChange();
        }

        private void GoToPreviousChange()
        {
            if (!CanGoToPreviousChange)
            {
                return;
            }

            currentChangeIndex--;
            PublishCurrentChange();
        }

        private void PublishCurrentChange()
        {
            if (HasTagPreview)
            {
                currentTagRow = TagRows[tagChangeIndexes[currentChangeIndex]];
                OnPropertyChanged(nameof(CurrentTagRow));
            }
            else
            {
                currentChangeLine = Lines[changeStarts[currentChangeIndex]];
                OnPropertyChanged(nameof(CurrentChangeLine));
            }

            OnPropertyChanged(nameof(ChangePositionText));
            PreviousChangeCommand.RaiseCanExecuteChanged();
            NextChangeCommand.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // A change is a contiguous run of added or removed lines. Context and omitted lines separate runs,
        // so one replacement (removed lines followed immediately by added lines) is a single jump target.
        private static int[] FindChangeStarts(IReadOnlyList<TextDiffLineViewModel> lines)
        {
            List<int> starts = new();
            bool insideChange = false;
            for (int index = 0; index < lines.Count; index++)
            {
                bool changed = lines[index].Kind is TextDiffLineKind.Added or TextDiffLineKind.Removed;
                if (changed && !insideChange)
                {
                    starts.Add(index);
                }

                insideChange = changed;
            }

            return starts.ToArray();
        }

        private static int[] FindTagChanges(IReadOnlyList<HmiTagPreviewRow> rows)
        {
            var starts = new List<int>();
            for (int index = 0; index < rows.Count; index++)
            {
                string change = rows[index].Change;
                if (change == "Added" || change == "Removed" || change == "Changed")
                {
                    starts.Add(index);
                }
            }

            return starts.ToArray();
        }
    }
}
