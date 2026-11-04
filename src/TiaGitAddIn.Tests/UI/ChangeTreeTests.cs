using System.Linq;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class ChangeTreeTests
    {
        [Fact]
        public void GroupPlacesHmiExportFilesUnderDeviceCategories()
        {
            string[] paths =
            {
                @"HmiExport\Line 1\device.xml",
                "HmiExport/Line 1/Overview.xml",
                "HmiExport/Line 1/Screens/Start.xml",
                "HmiExport/Line 1/Screens/Area/Motor.xml",
                "HmiExport/Line 1/Scripts/StartClick.xml",
                "HmiExport/Line 1/TagTables/Default.xml",
                "HmiExport/Line 1/Tags/Extra.xml",
                "HmiExport/Line 1/GraphicLists/Valve_0.xml",
                "HmiExport/Line 1/TextLists/States.xml",
                "HmiExport/Line 1/Slideins/Banner.xml",
                "HmiExport/Line 1/Popups/Alarm.xml",
                "HmiExport/Line 1/Templates/Base.xml",
                "HmiExport/Line 1/Connections/Plc.xml",
                "HmiExport/Line 1/Cycles/Fast.xml",
                "Blocks/Main.xml",
                "readme.txt",
            };

            var roots = ChangeTree.Group(paths, path => path);

            ChangeNode device = roots[0];
            Assert.Equal("HMI · Line 1 (14)", device.Title);
            Assert.False(device.IsExpanded);
            Assert.Equal(
                new[]
                {
                    "Screens (2)",
                    "Popups (1)",
                    "Templates (1)",
                    "Slide-ins (1)",
                    "Tags (2)",
                    "Scripts (1)",
                    "Text lists (1)",
                    "Graphic lists (1)",
                    "Connections (1)",
                    "Cycles (1)",
                    "Configuration (2)",
                },
                device.Children!.Select(child => child.Title).ToArray());

            ChangeNode screens = device.Children!.First();
            Assert.Equal(new[] { "Motor.xml", "Start.xml" }, screens.Children!.Select(child => child.Title).ToArray());
            Assert.Equal("HmiExport/Line 1/Screens/Area/Motor.xml", screens.Children![0].Item);
            Assert.False(screens.IsExpanded);

            Assert.Equal("Blocks (1)", roots[1].Title);
            Assert.Equal("Main.xml", Assert.Single(roots[1].Children!).Title);
            Assert.Equal("Other files (1)", roots[2].Title);
            Assert.Equal("readme.txt", Assert.Single(roots[2].Children!).Title);
        }

        [Fact]
        public void GroupStartsCollapsed()
        {
            string[] paths = Enumerable.Range(1, 9)
                .Select(index => "HmiExport/Panel/Screens/Screen" + index.ToString("00") + ".xml")
                .Append("HmiExport/Panel/device.xml")
                .Append("HardwareExport/G120/Drives/Drive.xml")
                .ToArray();

            var roots = ChangeTree.Group(paths, path => path);

            Assert.All(roots, node => Assert.False(node.IsExpanded));
            Assert.All(roots.SelectMany(node => node.Children!), node => Assert.False(node.IsExpanded));
            ChangeNode screens = roots.SelectMany(node => node.Children!).Single(child => child.Title.StartsWith("Screens"));
            Assert.Equal(9, screens.Children!.Count);
        }

        [Fact]
        public void GroupKeepsSeparateDevicesInNameOrder()
        {
            var roots = ChangeTree.Group(
                new[]
                {
                    "HmiExport/Zebra/Screens/A.xml",
                    "HmiExport/Alpha/Scripts/B.xml",
                },
                path => path);

            Assert.Equal(new[] { "HMI · Alpha (1)", "HMI · Zebra (1)" }, roots.Select(node => node.Title).ToArray());
        }

        [Fact]
        public void GroupPlacesHardwareBesideTheHmiDevice()
        {
            var roots = ChangeTree.Group(
                new[]
                {
                    "HardwareExport/G120/Hardware.xml",
                    "HardwareExport/G120/Drives/Drive.xml",
                    "HardwareExport/G120/Settings/CU.xml",
                    "HardwareExport/Networks/Subnets.xml",
                    "HmiExport/Panel/Screens/Start.xml",
                },
                path => path);

            Assert.Equal(
                new[] { "Hardware · G120 (3)", "Hardware · Networks (1)", "HMI · Panel (1)" },
                roots.Select(node => node.Title).ToArray());
        }

        [Fact]
        public void GroupNestsBlocksUnderTheirSoftwareUnit()
        {
            var roots = ChangeTree.Group(
                new[]
                {
                    @"PLC\Software units\Unit_600_Motion\Program blocks\Motion\FB_Jog.xml",
                    "PLC/Software units/Unit_600_Motion/Program blocks/FB_Stop.xml",
                    "PLC/Software units/Unit_100_Library/Program blocks/FC_Scale.xml",
                    "PLC/Program blocks/Main.xml",
                },
                path => path);

            ChangeNode plc = Assert.Single(roots);
            Assert.Equal("PLC (4)", plc.Title);
            Assert.False(plc.IsExpanded);
            Assert.Equal(
                new[] { "Program blocks (1)", "Software units (3)" },
                plc.Children!.Select(child => child.Title).ToArray());

            ChangeNode units = plc.Children![1];
            Assert.Equal(
                new[] { "Unit_100_Library (1)", "Unit_600_Motion (2)" },
                units.Children!.Select(child => child.Title).ToArray());

            ChangeNode libraryBlocks = Assert.Single(units.Children![0].Children!);
            Assert.Equal("Program blocks (1)", libraryBlocks.Title);
            Assert.Equal("FC_Scale.xml", Assert.Single(libraryBlocks.Children!).Title);
            Assert.Equal(
                "PLC/Software units/Unit_100_Library/Program blocks/FC_Scale.xml",
                libraryBlocks.Children![0].Item);

            ChangeNode motionBlocks = Assert.Single(units.Children![1].Children!);
            Assert.Equal("Program blocks (2)", motionBlocks.Title);
            Assert.Equal(
                new[] { "Motion (1)", "FB_Stop.xml" },
                motionBlocks.Children!.Select(child => child.Title).ToArray());
            Assert.Equal("FB_Jog.xml", Assert.Single(motionBlocks.Children![0].Children!).Title);
            Assert.False(motionBlocks.Children![0].IsExpanded);
        }

        [Fact]
        public void GroupPlacesProjectPicturesAndLibraryTypesBesideTheDevices()
        {
            var roots = ChangeTree.Group(
                new[]
                {
                    "HmiExport/_Project/Line 1/Graphics/Logo.xml",
                    "HmiExport/_Project/Line 1/Library/Motors/Pump/type.xml",
                },
                path => path);

            ChangeNode project = Assert.Single(roots);
            Assert.Equal("Project · Line 1 (2)", project.Title);
            Assert.Equal(new[] { "Graphics (1)", "Library (1)" }, project.Children!.Select(child => child.Title).ToArray());
        }
    }
}
