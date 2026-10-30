using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.Views.Comparison
{
    public partial class TextDiffView : UserControl
    {
        public TextDiffView()
        {
            PanelTheme.Apply(this);
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is INotifyPropertyChanged previous)
            {
                previous.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is INotifyPropertyChanged next)
            {
                next.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not TextComparisonViewModel viewModel)
            {
                return;
            }

            if (e.PropertyName == nameof(TextComparisonViewModel.CurrentChangeLine) &&
                viewModel.CurrentChangeLine != null)
            {
                ScrollToChange(viewModel.CurrentChangeLine);
            }

            if (e.PropertyName == nameof(TextComparisonViewModel.CurrentTagRow) &&
                viewModel.CurrentTagRow != null)
            {
                ListView list = viewModel.IsPlcTable ? TagListView : HmiTagListView;
                list.SelectedItem = viewModel.CurrentTagRow;
                list.UpdateLayout();
                list.ScrollIntoView(viewModel.CurrentTagRow);
            }
        }

        private void OnScreenBoxEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element
                && element.DataContext is HmiScreenBoxViewModel box
                && DataContext is TextComparisonViewModel viewModel
                && box.Change != "Unchanged")
            {
                viewModel.ShowScreenHover(box.ToolTipText);
            }
        }

        private void ScrollToChange(TextDiffLineViewModel line)
        {
            LinesListView.SelectedItem = line;
            LinesListView.UpdateLayout();
            LinesListView.ScrollIntoView(line);

            if (FindDescendant<ScrollViewer>(LinesListView) is ScrollViewer viewer)
            {
                int index = LinesListView.Items.IndexOf(line);
                if (index >= 0)
                {
                    viewer.ScrollToVerticalOffset(index);
                }
            }
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int index = 0; index < count; index++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, index);
                if (child is T match)
                {
                    return match;
                }

                T? descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }
    }
}
