using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using TiaGitAddIn.UI.ViewModels;

namespace TiaGitAddIn.UI.Views
{
    public partial class DiffView : UserControl
    {
        public DiffView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
        }

        /// <summary>
        /// The one genuine WPF event boundary in this view: an <c>async void</c> is unavoidable here
        /// because <see cref="SelectionChangedEventHandler"/> is a fire-and-forget event delegate. It only
        /// ever awaits <see cref="DiffViewModel.SelectEntryAsync"/> (a Task-returning, cancellation-aware
        /// method) and swallows exactly one exception kind -- cancellation from a superseded selection --
        /// which intentionally applies no state.
        /// </summary>
        private async void OnSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is not DiffViewModel viewModel || e.NewValue is not ChangeNode node)
            {
                return;
            }

            if (node.Item is not DiffEntryViewModel entry)
            {
                return;
            }

            viewModel.SelectedEntry = entry;
            try { await viewModel.SelectEntryAsync(entry, CancellationToken.None); }
            catch (OperationCanceledException) { }
        }

        private async void OnReviewSelected(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not DiffViewModel viewModel || viewModel.IsBusy)
            {
                return;
            }

            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DiffViewModel.ReviewBlock block)
            {
                return;
            }

            viewModel.SelectedEntry = block.Entry;
            try { await viewModel.SelectEntryAsync(block.Entry, CancellationToken.None); }
            catch (OperationCanceledException) { }
        }

        private async void OnHistorySelected(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not DiffViewModel viewModel || viewModel.IsBusy)
            {
                return;
            }

            if (e.AddedItems.Count == 0 || e.AddedItems[0] is not DiffViewModel.FileHistoryLine line)
            {
                return;
            }

            try { await viewModel.OpenHistoryLineAsync(line); }
            catch (OperationCanceledException) { }
        }
    }
}
