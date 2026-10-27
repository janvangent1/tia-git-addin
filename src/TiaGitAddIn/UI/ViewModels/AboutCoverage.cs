using System;
using System.Collections.Generic;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class AboutSection
    {
        public AboutSection(string title, IReadOnlyList<AboutRow> rows)
        {
            Title = title ?? string.Empty;
            Rows = rows ?? Array.Empty<AboutRow>();
        }

        public string Title { get; }

        public IReadOnlyList<AboutRow> Rows { get; }
    }

    public sealed class AboutRow
    {
        public AboutRow(string item, string export, string import)
        {
            Item = item ?? string.Empty;
            Export = export ?? string.Empty;
            Import = import ?? string.Empty;
            IsGap = IsLeftOut(Export) || IsLeftOut(Import);
        }

        public string Item { get; }

        public string Export { get; }

        public string Import { get; }

        public bool IsGap { get; }

        private static bool IsLeftOut(string value)
        {
            return value.Equals("None", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("None.", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static class AddInVersion
    {
        public const string Current = "1.2.55";
    }

    /// <summary>
    /// The same export and import coverage as the README, shown on the About page.
    /// </summary>
    public static class AboutCoverage
    {
        public static IReadOnlyList<AboutSection> Sections { get; } = new[]
        {
            new AboutSection("WinCC Unified", new[]
            {
                Row("Screens, including screens in screen groups", "Snapshot of the folder, size, screen properties, and every widget Openness exposes, with events, dynamizations, and appearance. Buttons and I/O fields also store text and process value.", "Screen name, size, properties, events, dynamizations, and appearance. Widgets are created when Openness has that type, and widgets missing from the file are removed."),
                Row("Tag tables and tag groups", "Full Siemens tag export.", "Full Siemens tag import. Tag tables missing from the folder stay in the project."),
                Row("System tags", "Snapshot.", "Writable attributes on the matching tag. System tags are not created or deleted."),
                Row("Plant-object tags", "Snapshot.", "Writable attributes on the matching tag. Plant-object tags are not created or deleted."),
                Row("JavaScript modules", "Full Siemens script export.", "Full Siemens script import."),
                Row("Text lists", "Full.", "Full."),
                Row("System text lists", "Full.", "Full."),
                Row("Graphic lists", "Full.", "Full."),
                Row("Discrete alarms, analog alarms, alarm classes", "Snapshot, including event text and info text.", "Attributes and texts on the matching alarm. A new alarm is created when the composition allows it. Alarms missing from the file are removed."),
                Row("OPC UA alarm types, alarm audit classes", "Snapshot.", "Attributes on the matching object. Objects missing from the file are removed when the composition can delete them."),
                Row("Connections, data logs, alarm logs, audit trails", "Snapshot.", "Attributes on the matching object. Objects missing from the file are removed when the composition can delete them."),
                Row("Runtime settings", "Snapshot.", "Attributes on the runtime settings."),
                Row("Any other composition Openness lists", "Snapshot under Other/.", "Attributes on the matching composition. Objects missing from the file are removed when the composition can delete them."),
                Row("Recipes and parameter sets", "None. V21 Openness has no recipe or parameter-set object.", "None."),
                Row("Users, cycles, and device settings", "None.", "None."),
                Row("Images stored only on the device, outside the project graphic collection", "None.", "None.")
            }),
            new AboutSection("WinCC Comfort", new[]
            {
                Row("Screens and screen folders", "Full Siemens screen export.", "Full. Screens missing from the folder are deleted."),
                Row("Popups, templates, slide-in screens", "Full.", "Full. Objects missing from the folder are deleted."),
                Row("Tag tables", "Full.", "Full for user tables. User tables missing from the folder are deleted. System tables stay."),
                Row("VB scripts", "Full.", "Full. Scripts missing from the folder are deleted."),
                Row("Text lists", "Full.", "Full. Lists missing from the folder are deleted."),
                Row("Connections", "Full.", "Full. Connections missing from the folder are deleted."),
                Row("User cycles", "Full.", "Full. User cycles missing from the folder are deleted."),
                Row("System cycles", "Full Siemens cycle export.", "The file is applied when Openness accepts it. System cycles are not created or deleted."),
                Row("Permanent area and overview", "Full.", "Full."),
                Row("Graphic lists", "Snapshot. Siemens GraphicList.Export is not called.", "Attributes and list entries on the matching graphic list."),
                Row("Alarms and messages", "Snapshot of compositions whose name contains alarm or message.", "Attributes on the matching object. Objects missing from the file are removed when the composition can delete them."),
                Row("Graphics and any other composition Openness lists", "Snapshot, plus a Siemens graphic file when the composition exports one.", "Siemens graphic files are imported. Attribute snapshots are written onto the matching composition."),
                Row("Recipes", "Snapshot only when Openness lists a recipe composition. The V21 public API has no recipe type.", "Attributes on that composition when the snapshot exists."),
                Row("Users and groups, scheduled jobs, runtime settings, device settings, and fonts", "None. Fonts set on a screen object travel with that screen.", "None.")
            }),
            new AboutSection("Project", new[]
            {
                Row("Project pictures", "Full Siemens graphic export, including the image files.", "Full. Pictures missing from the folder are removed."),
                Row("Faceplate types in the project library", "Full library document, using the in-work version when one exists.", "A missing type is created. An existing type updates its in-work version. Types missing from the folder stay. Global libraries are left out."),
                Row("Styles and style sheets in the project library", "Full library document, using the in-work version when one exists.", "A missing type is created. An existing type updates its in-work version. Types missing from the folder stay. Global libraries are left out.")
            }),
            new AboutSection("Hardware", new[]
            {
                Row("Device and module tree, type, rack position, PLC I/O addresses", "Type, rack position, and I/O addresses, in Hardware.xml. Number and enum attributes are left out, because a null number closes TIA Portal.", "Attributes on the matching device and modules. Devices and modules are not created, deleted, or moved."),
                Row("Subnet name, type, and IO system", "Full.", "Attributes on the matching subnet."),
                Row("Node IP address, subnet mask, router, PROFINET device name", "Full when Openness provides the value.", "Full, on the matching node."),
                Row("Port-to-port links", "Full.", "Full, on the matching ports."),
                Row("SINAMICS parameters whose value differs from the factory default", "Full.", "Full. The value is written onto the matching drive."),
                Row("SINAMICS telegrams and security settings", "Telegram type, and text security attributes. Number and enum attributes are left out.", "Security settings on the matching drive. The telegram type is written when Openness accepts the change."),
                Row("Drive function settings under Settings/", "Text attributes of function settings and technology extensions. Number and enum attributes are left out.", "Function settings and technology extensions on the matching module.")
            }),
            new AboutSection("PLC blocks", new[]
            {
                Row("PLC blocks", "TIA Portal VCI writes them into the Git workspace. The add-in compares those files.", "None. There is no PLC import button. Restore checks the commit's files out on a restore branch.")
            })
        };

        private static AboutRow Row(string item, string export, string import)
        {
            return new AboutRow(item, export, import);
        }
    }
}
