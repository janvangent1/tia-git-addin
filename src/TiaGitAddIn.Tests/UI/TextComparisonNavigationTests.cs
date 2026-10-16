using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.UI.ViewModels.Comparison;
using TiaGitAddIn.UI.Views.Comparison;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class TextComparisonNavigationTests
    {
        [Fact]
        public void NextAndPreviousWalkContiguousChangedBlocks()
        {
            TextComparisonViewModel viewModel = Create(
                TextDiffLineKind.Unchanged,
                TextDiffLineKind.Removed,
                TextDiffLineKind.Added,
                TextDiffLineKind.Unchanged,
                TextDiffLineKind.Added,
                TextDiffLineKind.Omitted,
                TextDiffLineKind.Removed);

            Assert.Equal("3 changes", viewModel.ChangePositionText);
            Assert.False(viewModel.PreviousChangeCommand.CanExecute(null));
            Assert.True(viewModel.NextChangeCommand.CanExecute(null));

            viewModel.NextChangeCommand.Execute(null);
            Assert.Equal("removed-1", viewModel.CurrentChangeLine!.Text);
            Assert.Equal("Change 1 of 3", viewModel.ChangePositionText);
            Assert.False(viewModel.PreviousChangeCommand.CanExecute(null));

            viewModel.NextChangeCommand.Execute(null);
            Assert.Equal("added-4", viewModel.CurrentChangeLine.Text);
            Assert.Equal("Change 2 of 3", viewModel.ChangePositionText);

            viewModel.NextChangeCommand.Execute(null);
            Assert.Equal("removed-6", viewModel.CurrentChangeLine.Text);
            Assert.Equal("Change 3 of 3", viewModel.ChangePositionText);
            Assert.False(viewModel.NextChangeCommand.CanExecute(null));
            Assert.True(viewModel.PreviousChangeCommand.CanExecute(null));

            viewModel.PreviousChangeCommand.Execute(null);
            Assert.Equal("added-4", viewModel.CurrentChangeLine.Text);
            Assert.Equal("Change 2 of 3", viewModel.ChangePositionText);
        }

        [Fact]
        public void DiffWithNoColoredLinesHasNothingToJumpTo()
        {
            TextComparisonViewModel viewModel = Create(TextDiffLineKind.Unchanged, TextDiffLineKind.Omitted);

            Assert.Equal("No changes", viewModel.ChangePositionText);
            Assert.False(viewModel.NextChangeCommand.CanExecute(null));
            Assert.False(viewModel.PreviousChangeCommand.CanExecute(null));
            Assert.Null(viewModel.CurrentChangeLine);
        }

        [Fact]
        public void NextChangeSelectsTheFirstChangedLineInTheList()
        {
            TextDiffLineKind[] kinds = new TextDiffLineKind[31];
            for (int index = 0; index < 30; index++)
            {
                kinds[index] = TextDiffLineKind.Unchanged;
            }

            kinds[30] = TextDiffLineKind.Added;
            TextComparisonViewModel viewModel = Create(kinds);

            WpfTestHost.Run(_ =>
            {
                var view = new TextDiffView
                {
                    DataContext = viewModel,
                    Width = 400,
                    Height = 120
                };
                view.Measure(new Size(400, 120));
                view.Arrange(new Rect(0, 0, 400, 120));
                view.UpdateLayout();

                viewModel.NextChangeCommand.Execute(null);

                var list = (ListView)view.FindName("LinesListView");
                Assert.Same(viewModel.CurrentChangeLine, list.SelectedItem);
                Assert.Equal("added-30", ((TextDiffLineViewModel)list.SelectedItem).Text);
            });
        }

        private static TextComparisonViewModel Create(params TextDiffLineKind[] kinds)
        {
            TextDiffLine[] lines = kinds
                .Select((kind, index) => new TextDiffLine(kind, index, index, Label(kind, index)))
                .ToArray();
            var presentation = new TextPresentation(lines);
            var result = new PlcComparisonResult(
                PlcArtifactKind.Scl,
                PlcComparisonMode.Text,
                PlcComparisonMode.Text,
                PlcSupportLevel.Fallback,
                "Fell back to line-based text comparison.",
                Array.Empty<PlcComparisonDiagnostic>(),
                presentation,
                null);

            return new TextComparisonViewModel(presentation, ComparisonViewModelMetadata.From(result));
        }

        private static string Label(TextDiffLineKind kind, int index) => kind switch
        {
            TextDiffLineKind.Added => "added-" + index,
            TextDiffLineKind.Removed => "removed-" + index,
            TextDiffLineKind.Omitted => "omitted-" + index,
            _ => "unchanged-" + index,
        };
    }
}
