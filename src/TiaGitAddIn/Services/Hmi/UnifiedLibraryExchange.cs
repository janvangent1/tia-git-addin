using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiTags;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Writes Unified tags, JavaScript modules, and text and graphic lists beside the screen XML.
    /// Comfort panels and runtimes already do this through <see cref="ComfortHmiExchange"/>.
    /// </summary>
    internal static class UnifiedLibraryExchange
    {
        public static int Export(HmiSoftware software, string deviceFolder, CancellationToken ct, Action<string> report)
        {
            if (software == null)
            {
                throw new ArgumentNullException(nameof(software));
            }

            int count = 0;
            if (software.Tags != null && software.Tags.Count > 0)
            {
                count += ExportTagLevel(software.TagTables, software.TagTableGroups, Path.Combine(deviceFolder, "Tags"), ct, report);
            }

            if (software.Scripts.Count > 0)
            {
                count += ExportFolder(Path.Combine(deviceFolder, "Scripts"), directory => software.Scripts.Export(directory), ct, report, "scripts");
            }

            if (software.HmiTextLists.Count > 0)
            {
                count += ExportNamedFile(Path.Combine(deviceFolder, "TextLists"), "TextLists.xml", (directory, fileName) => software.HmiTextLists.Export(directory, fileName), ct, report, "text lists");
            }

            if (software.HmiGraphicLists.Count > 0)
            {
                count += ExportNamedFile(Path.Combine(deviceFolder, "GraphicLists"), "GraphicLists.xml", (directory, fileName) => software.HmiGraphicLists.Export(directory, fileName), ct, report, "graphic lists");
            }

            count += ExportAlarmFile(software.DiscreteAlarms, alarm => alarm == null ? string.Empty : alarm.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "DiscreteAlarms.xml"), "Discrete", "discrete alarm", ct, report);
            count += ExportAlarmFile(software.AnalogAlarms, alarm => alarm == null ? string.Empty : alarm.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "AnalogAlarms.xml"), "Analog", "analog alarm", ct, report);
            count += ExportAlarmFile(software.AlarmClasses, alarmClass => alarmClass == null ? string.Empty : alarmClass.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "AlarmClasses.xml"), "AlarmClass", "alarm class", ct, report);

            return count;
        }

        public static void Import(HmiSoftware software, string deviceFolder, CancellationToken ct)
        {
            if (software == null)
            {
                throw new ArgumentNullException(nameof(software));
            }

            ImportTagLevel(software.TagTables, software.TagTableGroups, Path.Combine(deviceFolder, "Tags"), ct);
            ImportFolder(Path.Combine(deviceFolder, "Scripts"), directory => software.Scripts.Import(directory), ct, "scripts");
            ImportNamedFiles(Path.Combine(deviceFolder, "TextLists"), (directory, fileName) => software.HmiTextLists.Import(directory, fileName), ct, "text lists");
            ImportNamedFiles(Path.Combine(deviceFolder, "GraphicLists"), (directory, fileName) => software.HmiGraphicLists.Import(directory, fileName), ct, "graphic lists");
        }

        private static int ExportTagLevel(
            HmiTagTableComposition tables,
            HmiTagTableGroupComposition groups,
            string directory,
            CancellationToken ct,
            Action<string> report)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            foreach (string name in ReadNames(tables, table => table == null ? string.Empty : table.Name ?? string.Empty, ct))
            {
                HmiTagTable table = tables.Find(name);
                if (table?.Tags == null || table.Tags.Count == 0)
                {
                    continue;
                }

                string folder = Path.Combine(directory, HmiExportStore.UniqueName(name, used));
                count += ExportTagTable(table, name, folder, ct, report);
            }

            foreach (string name in ReadNames(groups, group => group == null ? string.Empty : group.Name ?? string.Empty, ct))
            {
                HmiTagTableGroup group = groups.Find(name);
                if (group == null)
                {
                    continue;
                }

                string folder = Path.Combine(directory, HmiExportStore.UniqueName(name, used));
                count += ExportTagLevel(group.TagTables, group.Groups, folder, ct, report);
            }

            return count;
        }

        private static int ExportTagTable(
            HmiTagTable table,
            string name,
            string folder,
            CancellationToken ct,
            Action<string> report)
        {
            string label = "tag table " + name;
            try
            {
                return ExportFolder(folder, directory => table.Tags.Export(directory), ct, report, label);
            }
            catch (Exception ex) when (IsDeferredTagExport(ex))
            {
                HmiExportTrace.Info(label + " was refused as a table. Exporting each tag.");
                return ExportTagsOneByOne(table, name, folder, ct, report);
            }
        }

        private static int ExportTagsOneByOne(
            HmiTagTable table,
            string tableName,
            string folder,
            CancellationToken ct,
            Action<string> report)
        {
            var names = new List<string>();
            foreach (HmiTag tag in table.Tags)
            {
                ct.ThrowIfCancellationRequested();
                string tagName = tag == null ? string.Empty : tag.Name ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(tagName))
                {
                    names.Add(tagName);
                }
            }

            Directory.CreateDirectory(folder);
            DirectoryInfo directory = new DirectoryInfo(folder);
            int count = 0;
            foreach (string tagName in names)
            {
                ct.ThrowIfCancellationRequested();
                report?.Invoke("tag table " + tableName + ": " + tagName);
                try
                {
                    count += CountFiles(table.Tags.Export(directory, tagName), report, "tag table " + tableName);
                }
                catch (Exception ex) when (IsDeferredTagExport(ex))
                {
                    HmiExportTrace.Error(ex);
                    report?.Invoke("tag table " + tableName + " skipped: " + ex.Message);
                    return count;
                }
            }

            return count;
        }

        private static void ImportTagLevel(
            HmiTagTableComposition tables,
            HmiTagTableGroupComposition groups,
            string directory,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (DirectoryInfo child in new DirectoryInfo(directory)
                .EnumerateDirectories()
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (child.EnumerateFiles().Any())
                {
                    if (tables == null)
                    {
                        throw new InvalidOperationException("TIA Portal has no tag tables for " + child.FullName + ".");
                    }

                    HmiTagTable table = tables.Find(child.Name) ?? tables.Create(child.Name);
                    if (!table.Tags.Import(child))
                    {
                        throw new InvalidOperationException("TIA Portal did not import the Unified tags from " + child.FullName + ".");
                    }

                    continue;
                }

                if (!child.EnumerateDirectories().Any() || groups == null)
                {
                    continue;
                }

                HmiTagTableGroup group = groups.Find(child.Name) ?? groups.Create(child.Name);
                ImportTagLevel(group.TagTables, group.Groups, child.FullName, ct);
            }
        }

        private static List<string> ReadNames<T>(IEnumerable<T> items, Func<T, string> name, CancellationToken ct)
        {
            var names = new List<string>();
            if (items == null)
            {
                return names;
            }

            foreach (T item in items)
            {
                ct.ThrowIfCancellationRequested();
                string value = item == null ? string.Empty : name(item) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    names.Add(value);
                }
            }

            return names;
        }

        private static bool IsDeferredTagExport(Exception ex)
        {
            for (Exception current = ex; current != null; current = current.InnerException)
            {
                string message = current.Message ?? string.Empty;
                if (message.IndexOf("future version", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    message.IndexOf("device level", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int ExportAlarmFile<T>(
            IEnumerable<T> items,
            Func<T, string> nameOf,
            string filePath,
            string kind,
            string label,
            CancellationToken ct,
            Action<string> report)
            where T : class
        {
            if (items == null)
            {
                return 0;
            }

            var alarms = new List<T>();
            foreach (T item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(nameOf(item)))
                {
                    alarms.Add(item);
                }
            }

            if (alarms.Count == 0)
            {
                return 0;
            }

            var root = new XElement("AlarmList", new XAttribute("kind", kind));
            int index = 0;
            foreach (T alarm in alarms)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                string alarmName = nameOf(alarm);
                report?.Invoke(label + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + alarmName);
                var element = new XElement("Alarm", new XAttribute("name", alarmName));
                if (alarm is IEngineeringObject engineering)
                {
                    try
                    {
                        AppendAttributes(element, engineering, 1);
                    }
                    catch (Exception ex) when (!IsSessionDead(ex))
                    {
                        HmiExportTrace.Error(ex);
                        element.Add(new XAttribute("error", ex.Message));
                    }
                }

                root.Add(element);
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            new XDocument(root).Save(filePath);
            return 1;
        }

        private static void AppendAttributes(XElement parent, IEngineeringObject engineering, int depth)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAttributes(parent, seen, engineering, depth, AttributeAccessOptions.ReadWrite);
            AddAttributes(parent, seen, engineering, depth, AttributeAccessOptions.ReadOnly);
        }

        private static void AddAttributes(
            XElement parent,
            ISet<string> seen,
            IEngineeringObject engineering,
            int depth,
            AttributeAccessOptions access)
        {
            System.Collections.IEnumerable attributes;
            try
            {
                attributes = engineering.GetAttributes(access) as System.Collections.IEnumerable;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (attributes == null)
            {
                return;
            }

            foreach (object entry in attributes)
            {
                if (!(entry is KeyValuePair<string, object> attribute) || string.IsNullOrWhiteSpace(attribute.Key) || !seen.Add(attribute.Key))
                {
                    continue;
                }

                if (depth > 0 && attribute.Value is IEngineeringObject child)
                {
                    var nested = new XElement("Attribute", new XAttribute("name", attribute.Key));
                    AppendAttributes(nested, child, depth - 1);
                    parent.Add(nested);
                    continue;
                }

                parent.Add(new XElement(
                    "Attribute",
                    new XAttribute("name", attribute.Key),
                    new XAttribute("value", AttributeText(attribute.Value))));
            }
        }

        private static string AttributeText(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is byte[] bytes)
            {
                return "bytes:" + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is IEngineeringObject engineering)
            {
                try
                {
                    object name = engineering.GetAttribute("Name");
                    if (name != null)
                    {
                        return Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                return string.Empty;
            }

            string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            return text ?? string.Empty;
        }

        private static bool IsSessionDead(Exception ex)
        {
            return ex is NonRecoverableException || ex is EngineeringObjectDisposedException;
        }

        private static int ExportFolder(
            string folder,
            Func<DirectoryInfo, IEnumerable<FileInfo>> export,
            CancellationToken ct,
            Action<string> report,
            string label)
        {
            ct.ThrowIfCancellationRequested();
            report?.Invoke(label);
            Directory.CreateDirectory(folder);
            return CountFiles(export(new DirectoryInfo(folder)), report, label);
        }

        private static int ExportNamedFile(
            string folder,
            string fileName,
            Func<DirectoryInfo, string, IEnumerable<FileInfo>> export,
            CancellationToken ct,
            Action<string> report,
            string label)
        {
            ct.ThrowIfCancellationRequested();
            report?.Invoke(label);
            Directory.CreateDirectory(folder);
            return CountFiles(export(new DirectoryInfo(folder), fileName), report, label);
        }

        private static void ImportFolder(string folder, Func<DirectoryInfo, bool> import, CancellationToken ct, string label)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder) || !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                return;
            }

            if (!import(new DirectoryInfo(folder)))
            {
                throw new InvalidOperationException("TIA Portal did not import the Unified " + label + " from " + folder + ".");
            }
        }

        private static void ImportNamedFiles(string folder, Func<DirectoryInfo, string, bool> import, CancellationToken ct, string label)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(folder))
            {
                return;
            }

            DirectoryInfo directory = new DirectoryInfo(folder);
            foreach (FileInfo file in directory.GetFiles().OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (!import(directory, file.Name))
                {
                    throw new InvalidOperationException("TIA Portal did not import the Unified " + label + " file " + file.Name + ".");
                }
            }
        }

        private static int CountFiles(IEnumerable<FileInfo> files, Action<string>? report, string label)
        {
            if (files == null)
            {
                return 0;
            }

            int count = 0;
            foreach (FileInfo file in files)
            {
                count++;
                if (file != null)
                {
                    report?.Invoke(label + ": " + file.Name);
                }
            }

            return count;
        }
    }
}
