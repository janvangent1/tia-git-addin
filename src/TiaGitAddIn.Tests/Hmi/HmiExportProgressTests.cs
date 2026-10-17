using TiaGitAddIn.Services.Hmi;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class HmiExportProgressTests
    {
        [Fact]
        public void ChecklistLineRoundTripsTheCategoryCountAndDetail()
        {
            string line = HmiExportProgress.Format("Line 1", "Screens", "running", 12, "Content/Overview");

            Assert.True(HmiExportProgress.TryParse(line, out HmiExportUpdate update));
            Assert.Equal("Line 1", update.Device);
            Assert.Equal("Screens", update.Category);
            Assert.Equal("running", update.State);
            Assert.Equal(12, update.Count);
            Assert.Equal("Content/Overview", update.Detail);
            Assert.Contains("Screens (12)", update.BusyText);
            Assert.Contains("Content/Overview", update.BusyText);
        }

        [Fact]
        public void ApplyingProgressChecksOffAPendingCategory()
        {
            var viewModel = new HmiExchangeViewModel(
                @"C:\repo",
                new UnavailableHmiDeviceExchange(),
                () => System.Threading.Tasks.Task.CompletedTask,
                ImmediateUiDispatcher.Instance);

            viewModel.ApplyExportProgress(HmiExportProgress.Format("Panel", "Screens", "pending", 0, string.Empty));
            viewModel.ApplyExportProgress(HmiExportProgress.Format("Panel", "Recipes", "pending", 0, string.Empty));
            viewModel.ApplyExportProgress(HmiExportProgress.Format("Panel", "Screens", "done", 4, string.Empty));

            Assert.Equal(2, viewModel.Checklist.Count);
            Assert.Equal("\u2713", viewModel.Checklist[0].Mark);
            Assert.Contains("(4)", viewModel.Checklist[0].Line);
            Assert.Equal("\u25cb", viewModel.Checklist[1].Mark);
            Assert.True(viewModel.HasChecklist);
            Assert.Equal("Screens (4)", viewModel.Checklist[0].Line);
        }

        [Fact]
        public void ChecklistKeepsEachDeviceFoldedUntilItIsTheOneRunning()
        {
            var viewModel = new HmiExchangeViewModel(
                @"C:\repo",
                new UnavailableHmiDeviceExchange(),
                () => System.Threading.Tasks.Task.CompletedTask,
                ImmediateUiDispatcher.Instance);

            viewModel.ApplyExportProgress(HmiExportProgress.Format("HMI_RT_3", "Screens", "pending", 0, string.Empty));
            viewModel.ApplyExportProgress(HmiExportProgress.Format("HMI_RT_3", "Tags", "pending", 0, string.Empty));
            viewModel.ApplyExportProgress(HmiExportProgress.Format("HMI_RT_4", "Screens", "pending", 0, string.Empty));
            viewModel.ApplyExportProgress(HmiExportProgress.Format("HMI_RT_3", "Screens", "running", 93, "screen 2: Dish Cap"));

            Assert.Equal(2, viewModel.ChecklistGroups.Count);
            Assert.Equal("HMI_RT_3", viewModel.ChecklistGroups[0].Device);
            Assert.Equal(2, viewModel.ChecklistGroups[0].Items.Count);
            Assert.True(viewModel.ChecklistGroups[0].IsExpanded);
            Assert.False(viewModel.ChecklistGroups[1].IsExpanded);
            Assert.DoesNotContain("HMI_RT_3", viewModel.ChecklistGroups[0].Items[0].Line);
            Assert.Contains("Dish Cap", viewModel.ChecklistGroups[0].Items[0].Line);

            viewModel.DismissChecklistCommand.Execute(null);
            Assert.False(viewModel.HasChecklist);
            Assert.Empty(viewModel.ChecklistGroups);
        }
    }
}
