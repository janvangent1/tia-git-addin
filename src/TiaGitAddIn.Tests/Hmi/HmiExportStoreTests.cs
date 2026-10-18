using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using TiaGitAddIn.Models;
using TiaGitAddIn.Models.Hmi;
using TiaGitAddIn.Services.Hmi;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class HmiExportStoreTests
    {
        [Fact]
        public void RoundTripWritesOneXmlFilePerScreen()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-hmi-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var device = new HmiDeviceExport
                {
                    Name = "Line 1/Panel",
                    Kind = HmiDeviceExport.UnifiedKind,
                    Screens = new System.Collections.Generic.List<HmiScreenExport>
                    {
                        new HmiScreenExport
                        {
                            Name = "Start",
                            Width = 800,
                            Height = 480,
                            Content = new XElement(
                                "Details",
                                new XAttribute("type", "HmiScreen"),
                                new XElement(
                                    "Attribute",
                                    new XAttribute("name", "BackColor"),
                                    new XAttribute("value", "#112233")),
                                new XElement(
                                    "Composition",
                                    new XAttribute("name", "EventHandlers"),
                                    new XElement(
                                        "Object",
                                        new XAttribute("type", "HmiScreenEventHandler"),
                                        new XAttribute("name", "Loaded")))),
                            Items = new System.Collections.Generic.List<HmiItemExport>
                            {
                                new HmiItemExport
                                {
                                    Kind = "Button",
                                    Name = "StartButton",
                                    Left = 40,
                                    Top = 24,
                                    Width = 160,
                                    Height = 48,
                                    Texts = new System.Collections.Generic.List<HmiTextExport>
                                    {
                                        new HmiTextExport { Language = "en-US", Text = "Start" }
                                    }
                                },
                                new HmiItemExport
                                {
                                    Kind = "IOField",
                                    Name = "Speed",
                                    Left = 40,
                                    Top = 90,
                                    Width = 120,
                                    Height = 32,
                                    ProcessValue = "DB_Data.Speed"
                                },
                                new HmiItemExport
                                {
                                    Kind = "Rectangle",
                                    Name = "Frame",
                                    Left = 8,
                                    Top = 8,
                                    Width = 200,
                                    Height = 80,
                                    Content = new XElement(
                                        "Details",
                                        new XElement(
                                            "Attribute",
                                            new XAttribute("name", "BackColor"),
                                            new XAttribute("value", "255,255,255")),
                                        new XElement(
                                            "Composition",
                                            new XAttribute("name", "Dynamizations"),
                                            new XElement("Object", new XAttribute("type", "HmiTagDynamization"))))
                                }
                            }
                        }
                    }
                };

                HmiExportStore.Write(root, device);

                string folder = HmiExportStore.DeviceFolder(root, "Line 1/Panel");
                Assert.Equal("Line 1_Panel", Path.GetFileName(folder));
                Assert.True(File.Exists(Path.Combine(folder, "device.xml")));
                Assert.True(File.Exists(Path.Combine(folder, "Screens", "Start.xml")));

                HmiDeviceExport read = Assert.Single(HmiExportStore.ReadAll(root));
                Assert.Equal("Line 1/Panel", read.Name);
                HmiScreenExport screen = Assert.Single(read.Screens);
                Assert.Equal(800, screen.Width);
                Assert.Equal("Start", Assert.Single(screen.Items.Single(item => item.Kind == "Button").Texts).Text);
                Assert.Equal("DB_Data.Speed", screen.Items.Single(item => item.Kind == "IOField").ProcessValue);
                Assert.Equal(string.Empty, screen.Folder);
                Assert.Equal("#112233", (string)screen.Content.Elements("Attribute").Single(attribute => (string)attribute.Attribute("name") == "BackColor").Attribute("value"));
                Assert.Equal("Loaded", (string)screen.Content.Element("Composition").Element("Object").Attribute("name"));
                HmiItemExport frame = screen.Items.Single(item => item.Kind == "Rectangle");
                Assert.Equal("255,255,255", (string)frame.Content.Elements("Attribute").Single(attribute => (string)attribute.Attribute("name") == "BackColor").Attribute("value"));
                Assert.Equal("Dynamizations", (string)frame.Content.Element("Composition").Attribute("name"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void RoundTripKeepsScreensInsideTheirGroupFolder()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-hmi-groups-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var device = new HmiDeviceExport
                {
                    Name = "Unified PC",
                    Screens = new System.Collections.Generic.List<HmiScreenExport>
                    {
                        new HmiScreenExport { Name = "Overview", Folder = "Content", Width = 1920, Height = 1080 },
                        new HmiScreenExport { Name = "Detail", Folder = "Content/Area", Width = 1920, Height = 1080 }
                    }
                };

                HmiExportStore.Write(root, device);

                string folder = HmiExportStore.DeviceFolder(root, "Unified PC");
                Assert.True(File.Exists(Path.Combine(folder, "Screens", "Content", "Overview.xml")));
                Assert.True(File.Exists(Path.Combine(folder, "Screens", "Content", "Area", "Detail.xml")));

                HmiDeviceExport read = Assert.Single(HmiExportStore.ReadAll(root));
                Assert.Equal(2, read.Screens.Count);
                Assert.Equal("Content", read.Screens.Single(screen => screen.Name == "Overview").Folder);
                Assert.Equal("Content/Area", read.Screens.Single(screen => screen.Name == "Detail").Folder);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void PrepareDeviceFolderReplacesAnOlderExportAndRecordsComfort()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-hmi-comfort-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                string folder = HmiExportStore.PrepareDeviceFolder(root, "Comfort Panel", HmiDeviceExport.ComfortKind);
                string stale = Path.Combine(folder, "Screens", "Old.xml");
                Directory.CreateDirectory(Path.GetDirectoryName(stale));
                File.WriteAllText(stale, "<Screen />");

                string replaced = HmiExportStore.PrepareDeviceFolder(root, "Comfort Panel", HmiDeviceExport.ComfortKind);

                Assert.Equal(folder, replaced);
                Assert.False(File.Exists(stale));
                HmiDeviceExport device = Assert.Single(HmiExportStore.ReadAll(root));
                Assert.Equal(HmiDeviceExport.ComfortKind, device.Kind);
                Assert.Equal("Comfort Panel", device.Name);
                Assert.Empty(device.Screens);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void ReadAllReturnsNothingWhenTheFolderIsMissing()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-hmi-empty-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                Assert.Empty(HmiExportStore.ReadAll(root));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public async Task ImportRefreshesStatusAfterTheExchangeSucceeds()
        {
            var exchange = new RecordingExchange();
            int refreshes = 0;
            var viewModel = new HmiExchangeViewModel(
                @"C:\repo",
                exchange,
                () =>
                {
                    refreshes++;
                    return Task.CompletedTask;
                },
                ImmediateUiDispatcher.Instance);

            viewModel.ImportHmi = true;
            await viewModel.ImportAsync();

            Assert.Equal(new[] { @"C:\repo" }, exchange.ImportedPaths);
            Assert.Equal(1, refreshes);
            Assert.Contains("Imported", viewModel.LastOperationMessage);
        }

        [Fact]
        public async Task ExportRunsOnlyTheCheckedItems()
        {
            var exchange = new RecordingExchange();
            var viewModel = new HmiExchangeViewModel(
                @"C:\repo",
                exchange,
                () => Task.CompletedTask,
                ImmediateUiDispatcher.Instance);

            viewModel.ExportHardware = false;
            await viewModel.ExportAsync();
            Assert.Equal(new[] { @"C:\repo" }, exchange.ExportedPaths);
            Assert.Empty(exchange.HardwarePaths);
            Assert.StartsWith("Finished in ", viewModel.ElapsedText);
            Assert.Contains(viewModel.ElapsedText, viewModel.LastOperationMessage);

            viewModel.ExportHmi = false;
            viewModel.ExportHardware = true;
            await viewModel.ExportAsync();
            Assert.Single(exchange.ExportedPaths);
            Assert.Equal(new[] { @"C:\repo" }, exchange.HardwarePaths);

            viewModel.ExportHmi = false;
            viewModel.ExportHardware = false;
            Assert.False(viewModel.ExportCommand.CanExecute(null));
        }

        [Fact]
        public async Task ImportRunsOnlyTheCheckedItems()
        {
            var exchange = new RecordingExchange();
            var viewModel = new HmiExchangeViewModel(
                @"C:\repo",
                exchange,
                () => Task.CompletedTask,
                ImmediateUiDispatcher.Instance);

            viewModel.ImportHmi = true;
            viewModel.ImportHardware = false;
            await viewModel.ImportAsync();
            Assert.Equal(new[] { @"C:\repo" }, exchange.ImportedPaths);
            Assert.Empty(exchange.ImportedHardwarePaths);

            viewModel.ImportHmi = false;
            viewModel.ImportHardware = true;
            await viewModel.ImportAsync();
            Assert.Single(exchange.ImportedPaths);
            Assert.Equal(new[] { @"C:\repo" }, exchange.ImportedHardwarePaths);

            viewModel.ImportHmi = false;
            viewModel.ImportHardware = false;
            Assert.False(viewModel.ImportCommand.CanExecute(null));
        }

        private sealed class RecordingExchange : IHmiDeviceExchange
        {
            public System.Collections.Generic.List<string> ImportedPaths { get; } = new System.Collections.Generic.List<string>();

            public System.Collections.Generic.List<string> ImportedHardwarePaths { get; } = new System.Collections.Generic.List<string>();

            public void RememberCallerContext()
            {
            }

            public System.Collections.Generic.List<string> ExportedPaths { get; } = new System.Collections.Generic.List<string>();

            public System.Collections.Generic.List<string> HardwarePaths { get; } = new System.Collections.Generic.List<string>();

            public Task<OperationResult> ExportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
            {
                ExportedPaths.Add(repositoryPath);
                return Task.FromResult(OperationResult.Ok("Exported."));
            }

            public Task<OperationResult> ExportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
            {
                HardwarePaths.Add(repositoryPath);
                return Task.FromResult(OperationResult.Ok("Exported hardware."));
            }

            public Task<OperationResult> ImportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
            {
                ImportedPaths.Add(repositoryPath);
                return Task.FromResult(OperationResult.Ok("Imported the HMI screens."));
            }

            public Task<OperationResult> ImportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
            {
                ImportedHardwarePaths.Add(repositoryPath);
                return Task.FromResult(OperationResult.Ok("Imported hardware."));
            }
        }
    }
}
