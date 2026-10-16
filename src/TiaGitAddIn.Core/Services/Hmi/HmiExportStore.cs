using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using TiaGitAddIn.Models.Hmi;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Reads and writes <c>HmiExport/&lt;device&gt;/</c> inside a Git working tree.
    /// The files are ordinary XML, so the existing text diff applies to them.
    /// </summary>
    public static class HmiExportStore
    {
        public const string RootFolderName = "HmiExport";

        private static readonly XName DeviceElement = "HmiDevice";
        private static readonly XName ScreenElement = "Screen";
        private static readonly XName ItemElement = "Item";
        private static readonly XName TextElement = "Text";

        public static string DeviceFolder(string repositoryPath, string deviceName)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
            }

            return Path.Combine(repositoryPath, RootFolderName, FolderName(deviceName));
        }

        public static string PrepareDeviceFolder(string repositoryPath, string deviceName, string kind)
        {
            string folder = DeviceFolder(repositoryPath, deviceName);
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }

            Directory.CreateDirectory(folder);
            Save(DeviceDocument(deviceName, kind), Path.Combine(folder, "device.xml"));
            return folder;
        }

        public static string UniqueName(string name, ISet<string> usedNames)
        {
            if (usedNames == null)
            {
                throw new ArgumentNullException(nameof(usedNames));
            }

            return UniqueFileName(FolderName(name), usedNames);
        }

        public static IReadOnlyList<string> Write(string repositoryPath, HmiDeviceExport device)
        {
            if (device == null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            string folder = PrepareDeviceFolder(
                repositoryPath,
                device.Name,
                string.IsNullOrWhiteSpace(device.Kind) ? HmiDeviceExport.UnifiedKind : device.Kind);
            string screensFolder = Path.Combine(folder, "Screens");
            Directory.CreateDirectory(screensFolder);

            var written = new List<string> { Path.Combine(folder, "device.xml") };

            var usedNames = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (HmiScreenExport screen in device.Screens)
            {
                string group = SanitizeFolder(screen.Folder);
                if (!usedNames.TryGetValue(group, out HashSet<string> used))
                {
                    used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    usedNames.Add(group, used);
                }

                string screenDirectory = ScreenDirectory(screensFolder, group);
                Directory.CreateDirectory(screenDirectory);
                string fileName = UniqueFileName(FolderName(screen.Name), used) + ".xml";
                string screenFile = Path.Combine(screenDirectory, fileName);
                Save(ScreenDocument(screen), screenFile);
                written.Add(screenFile);
            }

            return written;
        }

        private static XDocument DeviceDocument(string deviceName, string kind)
        {
            return new XDocument(
                new XElement(
                    DeviceElement,
                    new XAttribute("name", deviceName ?? string.Empty),
                    new XAttribute("kind", string.IsNullOrWhiteSpace(kind) ? HmiDeviceExport.UnifiedKind : kind)));
        }

        public static IReadOnlyList<HmiDeviceExport> ReadAll(string repositoryPath)
        {
            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                throw new ArgumentException("Repository path is required.", nameof(repositoryPath));
            }

            string root = Path.Combine(repositoryPath, RootFolderName);
            if (!Directory.Exists(root))
            {
                return Array.Empty<HmiDeviceExport>();
            }

            var devices = new List<HmiDeviceExport>();
            foreach (string folder in Directory.GetDirectories(root))
            {
                HmiDeviceExport? device = ReadDevice(folder);
                if (device != null)
                {
                    devices.Add(device);
                }
            }

            return devices;
        }

        public static string FolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "HmiDevice";
            }

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new StringBuilder(name.Trim().Length);
            foreach (char character in name.Trim())
            {
                builder.Append(Array.IndexOf(invalid, character) >= 0 ? '_' : character);
            }

            string folder = builder.ToString().Trim(' ', '.');
            if (folder.Length == 0 || folder == "." || folder == "..")
            {
                return "HmiDevice";
            }

            return folder;
        }

        private static HmiDeviceExport? ReadDevice(string folder)
        {
            string deviceFile = Path.Combine(folder, "device.xml");
            if (!File.Exists(deviceFile))
            {
                return null;
            }

            XDocument deviceDocument = XDocument.Load(deviceFile);
            XElement root = deviceDocument.Root;
            if (root == null || root.Name != DeviceElement)
            {
                return null;
            }

            var device = new HmiDeviceExport
            {
                Name = (string)root.Attribute("name") ?? string.Empty,
                Kind = (string)root.Attribute("kind") ?? HmiDeviceExport.UnifiedKind
            };

            string screensFolder = Path.Combine(folder, "Screens");
            if (!Directory.Exists(screensFolder))
            {
                return device;
            }

            ReadScreenFiles(screensFolder, screensFolder, device);
            return device;
        }

        private static void ReadScreenFiles(string directory, string screensFolder, HmiDeviceExport device)
        {
            foreach (string screenFile in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                HmiScreenExport? screen = ReadScreen(screenFile);
                if (screen == null)
                {
                    continue;
                }

                screen.Folder = RelativeFolder(screensFolder, directory);
                device.Screens.Add(screen);
            }

            foreach (string child in Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ReadScreenFiles(child, screensFolder, device);
            }
        }

        private static string ScreenDirectory(string screensFolder, string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return screensFolder;
            }

            string directory = screensFolder;
            foreach (string part in folder.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                directory = Path.Combine(directory, part);
            }

            return directory;
        }

        private static string SanitizeFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return string.Empty;
            }

            var parts = new List<string>();
            foreach (string part in folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                parts.Add(FolderName(part));
            }

            return string.Join("/", parts);
        }

        private static string RelativeFolder(string root, string directory)
        {
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullDirectory.Length < fullRoot.Length ||
                !fullDirectory.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (fullDirectory.Length == fullRoot.Length)
            {
                return string.Empty;
            }

            return fullDirectory.Substring(fullRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
        }

        private static HmiScreenExport? ReadScreen(string path)
        {
            XDocument document = XDocument.Load(path);
            XElement root = document.Root;
            if (root == null || root.Name != ScreenElement)
            {
                return null;
            }

            var screen = new HmiScreenExport
            {
                Name = (string)root.Attribute("name") ?? string.Empty,
                Width = ReadInt(root.Attribute("width")),
                Height = ReadInt(root.Attribute("height"))
            };

            foreach (XElement item in root.Elements(ItemElement))
            {
                screen.Items.Add(new HmiItemExport
                {
                    Kind = (string)item.Attribute("kind") ?? string.Empty,
                    Name = (string)item.Attribute("name") ?? string.Empty,
                    Left = ReadInt(item.Attribute("left")),
                    Top = ReadInt(item.Attribute("top")),
                    Width = ReadInt(item.Attribute("width")),
                    Height = ReadInt(item.Attribute("height")),
                    ProcessValue = (string)item.Attribute("processValue") ?? string.Empty,
                    Texts = item.Elements(TextElement)
                        .Select(text => new HmiTextExport
                        {
                            Language = (string)text.Attribute("language") ?? string.Empty,
                            Text = text.Value ?? string.Empty
                        })
                        .ToList()
                });
            }

            return screen;
        }

        private static XDocument ScreenDocument(HmiScreenExport screen)
        {
            var element = new XElement(
                ScreenElement,
                new XAttribute("name", screen.Name ?? string.Empty),
                new XAttribute("width", screen.Width.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("height", screen.Height.ToString(CultureInfo.InvariantCulture)));

            foreach (HmiItemExport item in screen.Items)
            {
                var itemElement = new XElement(
                    ItemElement,
                    new XAttribute("kind", item.Kind ?? string.Empty),
                    new XAttribute("name", item.Name ?? string.Empty),
                    new XAttribute("left", item.Left.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("top", item.Top.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("width", item.Width.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("height", item.Height.ToString(CultureInfo.InvariantCulture)));
                if (!string.IsNullOrEmpty(item.ProcessValue))
                {
                    itemElement.Add(new XAttribute("processValue", item.ProcessValue));
                }

                foreach (HmiTextExport text in item.Texts)
                {
                    itemElement.Add(new XElement(
                        TextElement,
                        new XAttribute("language", text.Language ?? string.Empty),
                        text.Text ?? string.Empty));
                }

                element.Add(itemElement);
            }

            return new XDocument(element);
        }

        private static void Save(XDocument document, string path)
        {
            using (var writer = XmlWriter.Create(path, new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            }))
            {
                document.Save(writer);
            }
        }

        private static int ReadInt(XAttribute attribute)
        {
            return attribute != null && int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private static string UniqueFileName(string name, ISet<string> usedNames)
        {
            string candidate = name;
            int suffix = 2;
            while (!usedNames.Add(candidate))
            {
                candidate = name + "_" + suffix.ToString(CultureInfo.InvariantCulture);
                suffix++;
            }

            return candidate;
        }
    }
}
