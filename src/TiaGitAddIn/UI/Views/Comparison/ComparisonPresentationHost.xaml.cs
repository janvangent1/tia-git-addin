using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.Views.Comparison
{
    public partial class ComparisonPresentationHost : UserControl
    {
        private ComparisonRawTextViewModel? rawText;

        public ComparisonPresentationHost()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (rawText != null)
            {
                rawText.PropertyChanged -= OnRawTextPropertyChanged;
                rawText = null;
            }

            if (e.NewValue is ComparisonPresentationViewModel presentation)
            {
                rawText = presentation.Metadata.RawText;
            }

            if (rawText != null)
            {
                rawText.PropertyChanged += OnRawTextPropertyChanged;
            }
        }

        private void OnRawTextPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ComparisonRawTextViewModel.CurrentChangeLine) &&
                sender is ComparisonRawTextViewModel viewModel &&
                viewModel.CurrentChangeLine != null)
            {
                ScrollToChange(viewModel.CurrentChangeLine);
            }
        }

        private void ScrollToChange(RawTextDiffLineViewModel line)
        {
            RawTextLines.SelectedItem = line;
            RawTextLines.UpdateLayout();
            RawTextLines.ScrollIntoView(line);

            if (FindDescendant<ScrollViewer>(RawTextLines) is ScrollViewer viewer)
            {
                int index = RawTextLines.Items.IndexOf(line);
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
