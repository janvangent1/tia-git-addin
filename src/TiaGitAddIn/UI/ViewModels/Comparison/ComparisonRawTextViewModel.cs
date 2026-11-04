using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Services.Comparison;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.ViewModels.Comparison
{
    /// <summary>
    /// Read-only presentation wrapper for <see cref="ComparisonRawText"/>. The two sides stay available
    /// as plain text, and <see cref="Lines"/> is the same line alignment used by the comparison, with a
    /// color on each side so an added or removed line is visible.
    /// </summary>
    public sealed class ComparisonRawTextViewModel : INotifyPropertyChanged
    {
        private readonly int[] changeStarts;
        private int currentChangeIndex = -1;
        private RawTextDiffLineViewModel? currentChangeLine;

        public ComparisonRawTextViewModel(ComparisonRawText rawText)
        {
            if (rawText == null) throw new ArgumentNullException(nameof(rawText));

            LeftText = rawText.LeftText ?? string.Empty;
            RightText = rawText.RightText ?? string.Empty;
            IsLeftMissing = rawText.IsLeftMissing;
            IsRightMissing = rawText.IsRightMissing;
            TextPresentation diff = new LineTextComparer(TextComparisonLimits.Default).Compare(rawText);
            IsTruncated = diff.IsTruncated;
            Lines = diff.Lines.Select(line => new RawTextDiffLineViewModel(line)).ToArray();
            changeStarts = FindChangeStarts(Lines);
            PreviousChangeCommand = new RelayCommand(_ => GoToPreviousChange(), _ => CanGoToPreviousChange);
            NextChangeCommand = new RelayCommand(_ => GoToNextChange(), _ => CanGoToNextChange);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string LeftText { get; }
        public string RightText { get; }
        public bool IsLeftMissing { get; }
        public bool IsRightMissing { get; }
        public bool IsTruncated { get; }
        public IReadOnlyList<RawTextDiffLineViewModel> Lines { get; }

        /// <summary>The first line of the changed block last jumped to, or null before any jump.</summary>
        public RawTextDiffLineViewModel? CurrentChangeLine => currentChangeLine;

        public string ChangePositionText
        {
            get
            {
                if (changeStarts.Length == 0)
                {
                    return "No changes";
                }

                return currentChangeIndex < 0
                    ? changeStarts.Length + " changes"
                    : "Change " + (currentChangeIndex + 1) + " of " + changeStarts.Length;
            }
        }

        public RelayCommand PreviousChangeCommand { get; }
        public RelayCommand NextChangeCommand { get; }

        private bool CanGoToPreviousChange => currentChangeIndex > 0;

        private bool CanGoToNextChange => changeStarts.Length > 0 && currentChangeIndex < changeStarts.Length - 1;

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
            currentChangeLine = Lines[changeStarts[currentChangeIndex]];
            OnPropertyChanged(nameof(CurrentChangeLine));
            OnPropertyChanged(nameof(ChangePositionText));
            PreviousChangeCommand.RaiseCanExecuteChanged();
            NextChangeCommand.RaiseCanExecuteChanged();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // A change is a contiguous run of added or removed lines. Matching and omitted lines separate runs,
        // so one replacement (removed lines followed immediately by added lines) is a single jump target.
        private static int[] FindChangeStarts(IReadOnlyList<RawTextDiffLineViewModel> lines)
        {
            List<int> starts = new();
            bool insideChange = false;
            for (int index = 0; index < lines.Count; index++)
            {
                if (lines[index].IsChange && !insideChange)
                {
                    starts.Add(index);
                }

                insideChange = lines[index].IsChange;
            }

            return starts.ToArray();
        }
    }

    public sealed class RawTextDiffLineViewModel
    {
        private static readonly Brush RemovedFill = Freeze(Color.FromRgb(245, 183, 177));
        private static readonly Brush AddedFill = Freeze(Color.FromRgb(169, 223, 191));
        private static readonly Brush OmittedFill = Freeze(Color.FromRgb(236, 236, 236));
        private static readonly Brush Clear = Freeze(Colors.Transparent);

        public RawTextDiffLineViewModel(TextDiffLine line)
        {
            if (line == null) throw new ArgumentNullException(nameof(line));

            LeftNumber = line.LeftLineNumber?.ToString() ?? string.Empty;
            RightNumber = line.RightLineNumber?.ToString() ?? string.Empty;
            IsChange = line.Kind is TextDiffLineKind.Added or TextDiffLineKind.Removed;
            if (line.Kind == TextDiffLineKind.Removed)
            {
                LeftText = line.Text;
                LeftFill = RemovedFill;
                RightFill = Clear;
            }
            else if (line.Kind == TextDiffLineKind.Added)
            {
                RightText = line.Text;
                LeftFill = Clear;
                RightFill = AddedFill;
            }
            else if (line.Kind == TextDiffLineKind.Omitted)
            {
                LeftText = line.Text;
                RightText = line.Text;
                LeftFill = OmittedFill;
                RightFill = OmittedFill;
            }
            else
            {
                LeftText = line.Text;
                RightText = line.Text;
                LeftFill = Clear;
                RightFill = Clear;
            }
        }

        public string LeftNumber { get; }
        public string RightNumber { get; }
        public bool IsChange { get; }
        public string LeftText { get; } = string.Empty;
        public string RightText { get; } = string.Empty;
        public Brush LeftFill { get; }
        public Brush RightFill { get; }

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
