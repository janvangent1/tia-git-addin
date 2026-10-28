using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.UI.ViewModels
{
    /// <summary>
    /// One row in a changed-file tree. Groups have children; a file row keeps the original item.
    /// </summary>
    public sealed class ChangeNode : INotifyPropertyChanged
    {
        public ChangeNode(string title, object? item, IReadOnlyList<ChangeNode>? children, bool isExpanded)
        {
            Title = title;
            Item = item;
            Children = children == null ? null : new ObservableCollection<ChangeNode>(children);
            IsExpanded = isExpanded;
        }

        public string Title { get; }

        public object? Item { get; }

        public ObservableCollection<ChangeNode>? Children { get; }

        public bool IsExpanded { get; }

        private bool isSelected;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected == value)
                {
                    return;
                }

                isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public bool IsFile => Item != null;
    }

    /// <summary>
    /// Groups changed paths. An HMI or hardware export is grouped by device and export folder.
    /// PLC files keep their workspace folders, so a block sits under its software unit.
    /// </summary>
    public static class ChangeTree
    {
        private static readonly string[] CategoryOrder =
        {
            "Screens",
            "Popups",
            "Templates",
            "Slide-ins",
            "Tags",
            "Scripts",
            "Text lists",
            "Graphic lists",
            "Connections",
            "Cycles",
            "Alarms",
            "Logs",
            "Runtime settings",
            "Other",
            "Recipes",
            "Graphics",
            "Pictures",
            "Library",
            "Drives",
            "Settings",
            "Networks",
        };

        public static ObservableCollection<ChangeNode> Group<T>(IEnumerable<T> items, Func<T, string> pathOf)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            if (pathOf == null)
            {
                throw new ArgumentNullException(nameof(pathOf));
            }

            var devices = new Dictionary<string, DeviceBucket>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<string, FolderBucket>(StringComparer.OrdinalIgnoreCase);
            var loose = new List<FileRow>();

            foreach (T item in items)
            {
                string path = pathOf(item) ?? string.Empty;
                (string? device, string category, string label) = Classify(path);
                FileRow row = new FileRow(item!, FileTitle(path));
                if (device != null)
                {
                    string key = label + "\n" + device;
                    if (!devices.TryGetValue(key, out DeviceBucket? bucket))
                    {
                        bucket = new DeviceBucket(label, device);
                        devices.Add(key, bucket);
                    }

                    bucket.Add(category, row);
                }
                else
                {
                    string[] parts = SplitPath(path);
                    if (parts.Length <= 1)
                    {
                        loose.Add(row);
                    }
                    else if (!folders.TryGetValue(parts[0], out FolderBucket? folder))
                    {
                        folder = new FolderBucket(parts[0]);
                        folders.Add(parts[0], folder);
                        folder.Add(parts, 1, row);
                    }
                    else
                    {
                        folder.Add(parts, 1, row);
                    }
                }
            }

            var roots = new List<ChangeNode>();
            foreach (string device in devices.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(devices[device].ToNode());
            }

            foreach (string name in folders.Keys
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                roots.Add(folders[name].ToNode());
            }

            if (loose.Count > 0)
            {
                roots.Add(CategoryNode("Other files", loose));
            }

            return new ObservableCollection<ChangeNode>(roots);
        }

        public static ChangeNode? OnlyFile(IEnumerable<ChangeNode> roots)
        {
            ChangeNode? found = null;
            foreach (ChangeNode node in Walk(roots))
            {
                if (!node.IsFile)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = node;
            }

            return found;
        }

        internal static (string? Device, string Category, string Label) Classify(string path)
        {
            string[] parts = (path ?? string.Empty)
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            int export = -1;
            string label = "HMI";
            for (int index = 0; index < parts.Length; index++)
            {
                if (parts[index].Equals(HmiExportFolder, StringComparison.OrdinalIgnoreCase)
                    || parts[index].Equals(HardwareExportFolder, StringComparison.OrdinalIgnoreCase))
                {
                    export = index;
                    label = parts[index].Equals(HardwareExportFolder, StringComparison.OrdinalIgnoreCase) ? "Hardware" : "HMI";
                    break;
                }
            }

            if (export >= 0 && export + 2 < parts.Length)
            {
                string device = parts[export + 1];
                if (device.Equals(ProjectContentStore.FolderName, StringComparison.OrdinalIgnoreCase) && export + 3 < parts.Length)
                {
                    string category = export + 4 < parts.Length ? CategoryName(parts[export + 3]) : "Configuration";
                    return (parts[export + 2], category, "Project");
                }

                if (export + 3 >= parts.Length)
                {
                    return (device, "Configuration", label);
                }

                return (device, CategoryName(parts[export + 2]), label);
            }

            if (parts.Length <= 1)
            {
                return (null, "Other files", string.Empty);
            }

            return (null, parts[0], string.Empty);
        }

        private static IEnumerable<ChangeNode> Walk(IEnumerable<ChangeNode> nodes)
        {
            foreach (ChangeNode node in nodes)
            {
                yield return node;
                if (node.Children == null)
                {
                    continue;
                }

                foreach (ChangeNode child in Walk(node.Children))
                {
                    yield return child;
                }
            }
        }

        private static ChangeNode CategoryNode(string name, List<FileRow> files)
        {
            files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Title, right.Title));
            var children = new List<ChangeNode>(files.Count);
            foreach (FileRow file in files)
            {
                children.Add(new ChangeNode(file.Title, file.Item, null, false));
            }

            string title = name + " (" + children.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
            return new ChangeNode(title, null, children, false);
        }

        private static string CategoryName(string folder)
        {
            switch (folder.Trim().ToUpperInvariant())
            {
                case "SCREENS":
                    return "Screens";
                case "POPUPS":
                    return "Popups";
                case "TEMPLATES":
                    return "Templates";
                case "SLIDEINS":
                case "SLIDE-INS":
                    return "Slide-ins";
                case "TAGTABLES":
                case "TAGS":
                    return "Tags";
                case "SCRIPTS":
                    return "Scripts";
                case "TEXTLISTS":
                    return "Text lists";
                case "GRAPHICLISTS":
                    return "Graphic lists";
                case "CONNECTIONS":
                    return "Connections";
                case "CYCLES":
                    return "Cycles";
                case "ALARMS":
                    return "Alarms";
                case "LOGS":
                    return "Logs";
                case "RUNTIMESETTINGS":
                    return "Runtime settings";
                case "OTHER":
                    return "Other";
                case "DRIVES":
                    return "Drives";
                case "SETTINGS":
                    return "Settings";
                case "NETWORKS":
                    return "Networks";
                default:
                    return folder;
            }
        }

        private static int CategoryRank(string name)
        {
            if (name.Equals("Configuration", StringComparison.OrdinalIgnoreCase))
            {
                return CategoryOrder.Length + 1;
            }

            for (int index = 0; index < CategoryOrder.Length; index++)
            {
                if (CategoryOrder[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return CategoryOrder.Length;
        }

        private static string FileTitle(string path)
        {
            string[] parts = SplitPath(path);
            return parts.Length == 0 ? path ?? string.Empty : parts[parts.Length - 1];
        }

        private static string[] SplitPath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private const string HmiExportFolder = "HmiExport";

        private const string HardwareExportFolder = "HardwareExport";

        private sealed class FolderBucket
        {
            private readonly string name;
            private readonly List<FileRow> files = new List<FileRow>();
            private readonly Dictionary<string, FolderBucket> folders =
                new Dictionary<string, FolderBucket>(StringComparer.OrdinalIgnoreCase);

            public FolderBucket(string name)
            {
                this.name = name;
            }

            public void Add(string[] parts, int index, FileRow row)
            {
                if (index >= parts.Length - 1)
                {
                    files.Add(row);
                    return;
                }

                string folderName = parts[index];
                if (!folders.TryGetValue(folderName, out FolderBucket? folder))
                {
                    folder = new FolderBucket(folderName);
                    folders.Add(folderName, folder);
                }

                folder.Add(parts, index + 1, row);
            }

            public ChangeNode ToNode()
            {
                var children = new List<ChangeNode>();
                foreach (FolderBucket folder in folders.Values
                    .OrderBy(folder => folder.name, StringComparer.OrdinalIgnoreCase))
                {
                    children.Add(folder.ToNode());
                }

                files.Sort((left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Title, right.Title));
                foreach (FileRow file in files)
                {
                    children.Add(new ChangeNode(file.Title, file.Item, null, false));
                }

                string title = name + " (" + FileCount().ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                return new ChangeNode(title, null, children, false);
            }

            private int FileCount()
            {
                int total = files.Count;
                foreach (FolderBucket folder in folders.Values)
                {
                    total += folder.FileCount();
                }

                return total;
            }
        }

        private sealed class FileRow
        {
            public FileRow(object item, string title)
            {
                Item = item;
                Title = title;
            }

            public object Item { get; }

            public string Title { get; }
        }

        private sealed class DeviceBucket
        {
            private readonly string label;
            private readonly string deviceName;
            private readonly Dictionary<string, List<FileRow>> categories =
                new Dictionary<string, List<FileRow>>(StringComparer.OrdinalIgnoreCase);

            public DeviceBucket(string label, string deviceName)
            {
                this.label = label;
                this.deviceName = deviceName;
            }

            public void Add(string category, FileRow row)
            {
                if (!categories.TryGetValue(category, out List<FileRow>? rows))
                {
                    rows = new List<FileRow>();
                    categories.Add(category, rows);
                }

                rows.Add(row);
            }

            public ChangeNode ToNode()
            {
                var children = new List<ChangeNode>();
                int total = 0;
                foreach (string category in categories.Keys
                    .OrderBy(CategoryRank)
                    .ThenBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    ChangeNode node = CategoryNode(category, categories[category]);
                    total += node.Children?.Count ?? 0;
                    children.Add(node);
                }

                string title = label + " · " + deviceName + " (" + total.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
                return new ChangeNode(title, null, children, false);
            }
        }
    }
}
