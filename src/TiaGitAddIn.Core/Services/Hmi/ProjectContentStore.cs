using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Files for project pictures and project-library faceplates and styles.
    /// They live under HmiExport/_Project because they belong to the project, not one HMI device.
    /// </summary>
    public static class ProjectContentStore
    {
        public const string FolderName = "_Project";
        public const string GraphicsFolderName = "Graphics";
        public const string LibraryFolderName = "Library";
        public const string GraphicIndexName = "graphics.txt";
        public const string ManifestName = "type.xml";

        private static readonly XName ManifestElement = "LibraryType";

        public static string SharedRoot(string repositoryPath)
        {
            return Path.Combine(repositoryPath ?? string.Empty, HmiExportStore.RootFolderName, FolderName);
        }

        public static string ProjectFolder(string repositoryPath, string projectName)
        {
            return Path.Combine(SharedRoot(repositoryPath), HmiExportStore.FolderName(projectName));
        }

        public static string GraphicsFolder(string projectFolder)
        {
            return Path.Combine(projectFolder ?? string.Empty, GraphicsFolderName);
        }

        public static string LibraryFolder(string projectFolder)
        {
            return Path.Combine(projectFolder ?? string.Empty, LibraryFolderName);
        }

        public static void WriteGraphicIndex(string graphicsFolder, IReadOnlyList<string> names)
        {
            Directory.CreateDirectory(graphicsFolder);
            var lines = new List<string>();
            if (names != null)
            {
                foreach (string name in names)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        lines.Add(name.Replace("\r", " ").Replace("\n", " "));
                    }
                }
            }

            File.WriteAllLines(Path.Combine(graphicsFolder, GraphicIndexName), lines);
        }

        public static IReadOnlyList<string>? ReadGraphicIndex(string graphicsFolder)
        {
            string index = Path.Combine(graphicsFolder ?? string.Empty, GraphicIndexName);
            if (!File.Exists(index))
            {
                return null;
            }

            return File.ReadAllLines(index)
                .Select(line => (line ?? string.Empty).Trim())
                .Where(line => line.Length > 0)
                .ToArray();
        }

        public static void WriteManifest(string typeFolder, string name, Guid guid, string format, string fileBase)
        {
            Directory.CreateDirectory(typeFolder);
            new XDocument(
                new XElement(
                    ManifestElement,
                    new XAttribute("name", name ?? string.Empty),
                    new XAttribute("guid", guid.ToString("D", CultureInfo.InvariantCulture)),
                    new XAttribute("format", format ?? string.Empty),
                    new XAttribute("file", fileBase ?? string.Empty)))
                .Save(Path.Combine(typeFolder, ManifestName));
        }

        public static bool TryReadManifest(string typeFolder, out string name, out Guid guid, out string format, out string fileBase)
        {
            name = string.Empty;
            guid = Guid.Empty;
            format = string.Empty;
            fileBase = string.Empty;
            string path = Path.Combine(typeFolder ?? string.Empty, ManifestName);
            if (!File.Exists(path))
            {
                return false;
            }

            XElement root = XDocument.Load(path).Root;
            if (root == null || root.Name != ManifestElement)
            {
                return false;
            }

            name = (string)root.Attribute("name") ?? string.Empty;
            format = (string)root.Attribute("format") ?? string.Empty;
            fileBase = (string)root.Attribute("file") ?? string.Empty;
            return Guid.TryParse((string)root.Attribute("guid"), out guid) && fileBase.Length > 0;
        }

        public static string ChooseFormat(IEnumerable<string>? formats)
        {
            string first = string.Empty;
            if (formats == null)
            {
                return first;
            }

            foreach (string format in formats)
            {
                if (string.IsNullOrWhiteSpace(format))
                {
                    continue;
                }

                if (first.Length == 0)
                {
                    first = format;
                }

                if (format.Equals("WinCCML", StringComparison.OrdinalIgnoreCase))
                {
                    return format;
                }
            }

            return first;
        }
    }
}
