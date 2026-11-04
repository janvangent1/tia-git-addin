using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.ViewModels.Comparison
{
    /// <summary>
    /// SCL block structure with the declarations and statements that changed. Unchanged rows stay
    /// hidden until the user asks for them. Next and previous move through the changed rows.
    /// </summary>
    public sealed class SclComparisonViewModel : ComparisonPresentationViewModel, INotifyPropertyChanged
    {
        private readonly SclPresentation presentation;
        private bool showUnchanged;
        private SclDiffRow? selectedRow;

        public SclComparisonViewModel(
            SclPresentation presentation,
            ComparisonViewModelMetadata metadata,
            bool showStructure = true)
            : base(ComparisonPresentationKind.Scl, metadata)
        {
            this.presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
            OpenRawText = !showStructure && HasRawText;
            Rows = new ObservableCollection<SclDiffRow>();
            PreviousChangeCommand = new RelayCommand(_ => Move(-1));
            NextChangeCommand = new RelayCommand(_ => Move(1));
            Rebuild();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<SclDiffRow> Rows { get; }

        public ICommand PreviousChangeCommand { get; }

        public ICommand NextChangeCommand { get; }

        public bool ShowUnchanged
        {
            get => showUnchanged;
            set
            {
                if (showUnchanged == value)
                {
                    return;
                }

                showUnchanged = value;
                Rebuild();
                OnPropertyChanged();
            }
        }

        public SclDiffRow? SelectedRow
        {
            get => selectedRow;
            set
            {
                selectedRow = value;
                OnPropertyChanged();
            }
        }

        public string ChangeCountText { get; private set; } = string.Empty;

        private void Rebuild()
        {
            Rows.Clear();
            int changes = 0;
            foreach (SclChange change in presentation.Changes)
            {
                changes += Add(change, 0);
            }

            if (Rows.Count == 0)
            {
                Rows.Add(new SclDiffRow("No differences in this SCL block.", string.Empty, string.Empty, 0, false));
            }

            ChangeCountText = changes == 1 ? "1 change" : changes + " changes";
            OnPropertyChanged(nameof(ChangeCountText));
            SelectedRow = FirstChange();
        }

        private int Add(SclChange change, int depth)
        {
            bool changed = change.Status != SclChangeStatus.Unchanged;
            bool childChanged = HasChange(change);
            if (!showUnchanged && !changed && !childChanged)
            {
                return 0;
            }

            bool isChange = changed && !childChanged;
            Rows.Add(new SclDiffRow(change.Title, Label(change.Status), change.Detail, depth, isChange));
            int count = isChange ? 1 : 0;
            foreach (SclChange child in change.Children)
            {
                count += Add(child, depth + 1);
            }

            return count;
        }

        private static bool HasChange(SclChange change)
        {
            foreach (SclChange child in change.Children)
            {
                if (child.Status != SclChangeStatus.Unchanged || HasChange(child))
                {
                    return true;
                }
            }

            return false;
        }

        private void Move(int direction)
        {
            int index = SelectedRow == null ? -1 : Rows.IndexOf(SelectedRow);
            int step = direction < 0 ? -1 : 1;
            for (int i = index + step; i >= 0 && i < Rows.Count; i += step)
            {
                if (Rows[i].IsChange)
                {
                    SelectedRow = Rows[i];
                    return;
                }
            }
        }

        private SclDiffRow? FirstChange()
        {
            foreach (SclDiffRow row in Rows)
            {
                if (row.IsChange)
                {
                    return row;
                }
            }

            return Rows.Count > 0 ? Rows[0] : null;
        }

        private static string Label(SclChangeStatus status)
        {
            return status == SclChangeStatus.Unchanged ? string.Empty : status.ToString();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public sealed class SclDiffRow
    {
        public SclDiffRow(string title, string statusLabel, string detail, int depth, bool isChange)
        {
            Title = title ?? string.Empty;
            StatusLabel = statusLabel ?? string.Empty;
            Detail = detail ?? string.Empty;
            Indent = new Thickness(12 + (depth * 16), 2, 8, 2);
            IsChange = isChange;
            HasDetail = Detail.Length > 0;
            HasStatus = StatusLabel.Length > 0;
        }

        public string Title { get; }
        public string StatusLabel { get; }
        public string Detail { get; }
        public Thickness Indent { get; }
        public bool IsChange { get; }
        public bool HasDetail { get; }
        public bool HasStatus { get; }
    }
}
