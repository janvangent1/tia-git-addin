using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.HmiAlarm;
using Siemens.Engineering.HmiUnified.HmiAlarm.HmiAlarmCommon;
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
        public static int Export(
            HmiSoftware software,
            string deviceName,
            string deviceFolder,
            CancellationToken ct,
            Action<string> report,
            ExportCategorySet? categories = null)
        {
            if (software == null)
            {
                throw new ArgumentNullException(nameof(software));
            }

            stepDevice = deviceName ?? string.Empty;
            stepReport = report;
            ExportCategorySet previous = enabledCategories;
            enabledCategories = categories ?? ExportCategorySet.All;
            try
            {
                return ExportCore(software, deviceFolder, ct);
            }
            finally
            {
                stepCategory = string.Empty;
                stepReport = null;
                enabledCategories = previous;
            }
        }

        private static ExportCategorySet enabledCategories = ExportCategorySet.All;

        private static bool Want(string category)
        {
            if (enabledCategories.AllowsHmi(category))
            {
                return true;
            }

            Skip(category, "Turned off in Settings.");
            return false;
        }

        private static int ExportCore(HmiSoftware software, string deviceFolder, CancellationToken ct)
        {
            int count = 0;
            if (Want("Tags"))
            {
                Begin("Tags");
                if (software.Tags != null && software.Tags.Count > 0)
                {
                    count += ExportTagLevel(software.TagTables, software.TagTableGroups, Path.Combine(deviceFolder, "Tags"), ct, Track);
                }

                count += ExportObjects(software.PlantObjectTags, Path.Combine(deviceFolder, "Tags", "PlantObjects.xml"), "PlantObject", "plant object", ct, true);
                Finish("Tags", stepCount);
            }

            if (Want("System tags"))
            {
                Begin("System tags");
                count += ExportObjects(software.SystemTags, Path.Combine(deviceFolder, "Tags", "SystemTags.xml"), "SystemTag", "system tag", ct, false);
                Finish("System tags", stepCount);
            }

            if (Want("Scripts"))
            {
                Begin("Scripts");
                if (software.Scripts != null && software.Scripts.Count > 0)
                {
                    count += ExportFolder(Path.Combine(deviceFolder, "Scripts"), directory => software.Scripts.Export(directory), ct, Track, "scripts");
                }

                Finish("Scripts", stepCount);
            }

            if (Want("Text lists"))
            {
                Begin("Text lists");
                if (software.HmiTextLists != null && software.HmiTextLists.Count > 0)
                {
                    count += ExportNamedFile(Path.Combine(deviceFolder, "TextLists"), "TextLists.xml", (directory, fileName) => software.HmiTextLists.Export(directory, fileName), ct, Track, "text lists");
                }

                Finish("Text lists", stepCount);
            }

            if (Want("Graphic lists"))
            {
                Begin("Graphic lists");
                if (software.HmiGraphicLists != null && software.HmiGraphicLists.Count > 0)
                {
                    count += ExportNamedFile(Path.Combine(deviceFolder, "GraphicLists"), "GraphicLists.xml", (directory, fileName) => software.HmiGraphicLists.Export(directory, fileName), ct, Track, "graphic lists");
                }

                Finish("Graphic lists", stepCount);
            }

            if (Want("System text lists"))
            {
                Begin("System text lists");
                if (software.HmiSystemTextLists != null && software.HmiSystemTextLists.Count > 0)
                {
                    count += ExportNamedFile(Path.Combine(deviceFolder, "TextLists"), "SystemTextLists.xml", (directory, fileName) => software.HmiSystemTextLists.Export(directory, fileName), ct, Track, "system text lists");
                }

                Finish("System text lists", stepCount);
            }

            if (Want("Alarms"))
            {
                Begin("Alarms");
                count += ExportAlarmFile(software.DiscreteAlarms, alarm => alarm == null ? string.Empty : alarm.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "DiscreteAlarms.xml"), "Discrete", "discrete alarm", ct, Track);
                count += ExportAlarmFile(software.AnalogAlarms, alarm => alarm == null ? string.Empty : alarm.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "AnalogAlarms.xml"), "Analog", "analog alarm", ct, Track);
                count += ExportAlarmFile(software.AlarmClasses, alarmClass => alarmClass == null ? string.Empty : alarmClass.Name ?? string.Empty, Path.Combine(deviceFolder, "Alarms", "AlarmClasses.xml"), "AlarmClass", "alarm class", ct, Track);
                count += ExportObjects(software.OpcUaAlarmTypes, Path.Combine(deviceFolder, "Alarms", "OpcUaAlarmTypes.xml"), "OpcUaAlarm", "OPC UA alarm", ct, false);
                count += ExportObjects(software.HmiAlarmAuditClass, Path.Combine(deviceFolder, "Alarms", "AlarmAuditClasses.xml"), "AuditClass", "alarm audit class", ct, false);
                Finish("Alarms", stepCount);
            }

            if (Want("Connections"))
            {
                Begin("Connections");
                count += ExportObjects(software.Connections, Path.Combine(deviceFolder, "Connections", "Connections.xml"), "Connection", "connection", ct, true);
                Finish("Connections", stepCount);
            }

            if (Want("Logs"))
            {
                Begin("Logs");
                count += ExportObjects(software.DataLogs, Path.Combine(deviceFolder, "Logs", "DataLogs.xml"), "DataLog", "data log", ct, true);
                count += ExportObjects(software.AlarmLogs, Path.Combine(deviceFolder, "Logs", "AlarmLogs.xml"), "AlarmLog", "alarm log", ct, true);
                count += ExportObjects(software.AuditTrails, Path.Combine(deviceFolder, "Logs", "AuditTrails.xml"), "AuditTrail", "audit trail", ct, true);
                Finish("Logs", stepCount);
            }

            if (Want("Other"))
            {
                count += ExportRecipeCompositions(software, deviceFolder, ct);
            }

            if (Want("Runtime settings"))
            {
                count += ExportRuntimeSettings(software, deviceFolder);
            }

            return count;
        }

        private static string stepDevice = string.Empty;

        private static string stepCategory = string.Empty;

        private static int stepCount;

        private static Action<string>? stepReport;

        private static void Begin(string category)
        {
            stepCategory = category;
            stepCount = 0;
            stepReport?.Invoke(HmiExportProgress.Format(stepDevice, category, "running", 0, string.Empty));
        }

        private static void Finish(string category, int count)
        {
            stepReport?.Invoke(HmiExportProgress.Format(stepDevice, category, "done", count, string.Empty));
            stepCategory = string.Empty;
            stepCount = 0;
        }

        private static void Skip(string category, string detail)
        {
            stepReport?.Invoke(HmiExportProgress.Format(stepDevice, category, "skipped", 0, detail));
            stepCategory = string.Empty;
            stepCount = 0;
        }

        private static void Track(string message)
        {
            if (string.IsNullOrEmpty(stepCategory))
            {
                stepReport?.Invoke(message);
                return;
            }

            stepCount++;
            stepReport?.Invoke(HmiExportProgress.Format(stepDevice, stepCategory, "running", stepCount, message));
        }

        private static int ExportObjects(
            System.Collections.IEnumerable? items,
            string filePath,
            string kind,
            string label,
            CancellationToken ct,
            bool includeChildren)
        {
            if (items == null)
            {
                return 0;
            }

            var objects = new List<IEngineeringObject>();
            foreach (object entry in items)
            {
                ct.ThrowIfCancellationRequested();
                if (entry is IEngineeringObject engineering)
                {
                    objects.Add(engineering);
                }
            }

            if (objects.Count == 0)
            {
                return 0;
            }

            var root = new XElement("HmiItems", new XAttribute("kind", kind));
            int index = 0;
            foreach (IEngineeringObject item in objects)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                string itemName = ObjectName(item, index);
                Track(label + " " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ": " + itemName);
                var element = new XElement("Item", new XAttribute("name", itemName));
                try
                {
                    AppendAttributes(element, item, includeChildren ? 1 : 1);
                    if (includeChildren)
                    {
                        AppendChildCompositions(element, item, ct);
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    HmiExportTrace.Error(ex);
                    element.Add(new XAttribute("error", ex.Message));
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

        private static int ExportRecipeCompositions(HmiSoftware software, string deviceFolder, CancellationToken ct)
        {
            if (!(software is IEngineeringObject engineering))
            {
                Skip("Other", "This Unified device does not expose engineering compositions.");
                return 0;
            }

            List<string> names;
            try
            {
                names = CompositionNames(engineering.GetCompositionInfos(), ct);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                HmiExportTrace.Error(ex);
                Skip("Other", ex.Message);
                return 0;
            }

            var recipeNames = new List<string>();
            var otherNames = new List<string>();
            foreach (string name in names)
            {
                if (IsCoveredComposition(name))
                {
                    continue;
                }

                if (name.IndexOf("recipe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("parameterset", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("parameter set", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    recipeNames.Add(name);
                }
                else
                {
                    otherNames.Add(name);
                }
            }

            int files = 0;
            if (recipeNames.Count > 0)
            {
                files += WriteCompositions(engineering, recipeNames, "Recipes", Path.Combine(deviceFolder, "Recipes"), ct);
            }

            if (otherNames.Count == 0)
            {
                Skip("Other", "No further HMI compositions.");
            }
            else
            {
                files += WriteCompositions(engineering, otherNames, "Other", Path.Combine(deviceFolder, "Other"), ct);
            }
            return files;
        }

        private static int WriteCompositions(
            IEngineeringObject engineering,
            List<string> names,
            string category,
            string directory,
            CancellationToken ct)
        {
            if (names == null || names.Count == 0)
            {
                Skip(category, category == "Recipes"
                    ? "Openness lists no recipe composition."
                    : "No further HMI compositions.");
                return 0;
            }

            Begin(category);
            int files = 0;
            foreach (string name in names)
            {
                ct.ThrowIfCancellationRequested();
                stepCategory = category;
                try
                {
                    IEngineeringCompositionOrObject composition = engineering.GetComposition(name);
                    files += ExportObjects(
                        composition as System.Collections.IEnumerable,
                        Path.Combine(directory, HmiExportStore.FolderName(name) + ".xml"),
                        name,
                        name,
                        ct,
                        true);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    HmiExportTrace.Error(ex);
                    Track(name + " failed: " + ex.Message);
                }
            }

            Finish(category, stepCount);
            return files;
        }

        private static int ExportRuntimeSettings(HmiSoftware software, string deviceFolder)
        {
            Begin("Runtime settings");
            try
            {
                object settings = software.RuntimeSettings;
                if (settings is IEngineeringObject engineering)
                {
                    var root = new XElement("RuntimeSettings");
                    AppendAttributes(root, engineering, 1);
                    string directory = Path.Combine(deviceFolder, "RuntimeSettings");
                    Directory.CreateDirectory(directory);
                    new XDocument(root).Save(Path.Combine(directory, "RuntimeSettings.xml"));
                    Finish("Runtime settings", 1);
                    return 1;
                }

                Finish("Runtime settings", 0);
                return 0;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                HmiExportTrace.Error(ex);
                Skip("Runtime settings", ex.Message);
                return 0;
            }
        }

        private static bool IsCoveredComposition(string name)
        {
            switch ((name ?? string.Empty).Replace(" ", string.Empty).ToUpperInvariant())
            {
                case "SCREENS":
                case "SCREENGROUPS":
                case "TAGS":
                case "TAGTABLES":
                case "TAGTABLEGROUPS":
                case "SCRIPTS":
                case "HMITEXTLISTS":
                case "HMIGRAPHICLISTS":
                case "HMISYSTEMTEXTLISTS":
                case "DISCRETEALARMS":
                case "ANALOGALARMS":
                case "ALARMCLASSES":
                case "CONNECTIONS":
                case "DATALOGS":
                case "ALARMLOGS":
                case "AUDITTRAILS":
                case "OPCUAALARMTYPES":
                case "HMIALARMAUDITCLASS":
                case "SYSTEMTAGS":
                case "PLANTOBJECTTAGS":
                case "RUNTIMESETTINGS":
                    return true;
                default:
                    return false;
            }
        }

        private static List<string> CompositionNames(IEnumerable<EngineeringCompositionInfo> infos, CancellationToken ct)
        {
            var names = new List<string>();
            if (infos == null)
            {
                return names;
            }

            foreach (EngineeringCompositionInfo info in infos)
            {
                ct.ThrowIfCancellationRequested();
                if (info != null && !string.IsNullOrWhiteSpace(info.Name))
                {
                    names.Add(info.Name);
                }
            }

            return names;
        }

        private static string ObjectName(IEngineeringObject item, int index)
        {
            try
            {
                object name = item.GetAttribute("Name");
                string text = Convert.ToString(name, System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return "Item" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void AppendChildCompositions(XElement parent, IEngineeringObject engineering, CancellationToken ct)
        {
            IEnumerable<EngineeringCompositionInfo> infos;
            try
            {
                infos = engineering.GetCompositionInfos();
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (infos == null)
            {
                return;
            }

            foreach (EngineeringCompositionInfo info in infos)
            {
                ct.ThrowIfCancellationRequested();
                if (info == null || string.IsNullOrWhiteSpace(info.Name))
                {
                    continue;
                }

                var items = new XElement("Items", new XAttribute("composition", info.Name));
                try
                {
                    IEngineeringCompositionOrObject composition = engineering.GetComposition(info.Name);
                    if (composition is System.Collections.IEnumerable enumerable)
                    {
                        int index = 0;
                        foreach (object entry in enumerable)
                        {
                            ct.ThrowIfCancellationRequested();
                            index++;
                            var child = new XElement("Item", new XAttribute("index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                            if (entry is IEngineeringObject engineeringChild)
                            {
                                child.SetAttributeValue("name", ObjectName(engineeringChild, index));
                                AppendAttributes(child, engineeringChild, 0);
                            }

                            items.Add(child);
                        }
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    items.Add(new XAttribute("error", ex.Message));
                }

                parent.Add(items);
            }
        }

        public static void Import(HmiSoftware software, string deviceFolder, CancellationToken ct)
        {
            if (software == null)
            {
                throw new ArgumentNullException(nameof(software));
            }

            ImportTagLevel(software.TagTables, software.TagTableGroups, Path.Combine(deviceFolder, "Tags"), ct);
            ImportFolder(Path.Combine(deviceFolder, "Scripts"), directory => software.Scripts.Import(directory), ct, "scripts");
            ImportNamedFiles(
                Path.Combine(deviceFolder, "TextLists"),
                (directory, fileName) => fileName.Equals("SystemTextLists.xml", StringComparison.OrdinalIgnoreCase) || software.HmiTextLists.Import(directory, fileName),
                ct,
                "text lists");
            ImportNamedFiles(
                Path.Combine(deviceFolder, "TextLists"),
                (directory, fileName) => !fileName.Equals("SystemTextLists.xml", StringComparison.OrdinalIgnoreCase) || software.HmiSystemTextLists.Import(directory, fileName),
                ct,
                "system text lists");
            ImportNamedFiles(Path.Combine(deviceFolder, "GraphicLists"), (directory, fileName) => software.HmiGraphicLists.Import(directory, fileName), ct, "graphic lists");
            RestoreSnapshots(software, deviceFolder);
        }

        private static void RestoreSnapshots(HmiSoftware software, string deviceFolder)
        {
            EngineeringSnapshotRestorer.ApplyFile(software.SystemTags, Path.Combine(deviceFolder, "Tags", "SystemTags.xml"), "Item", false);
            EngineeringSnapshotRestorer.ApplyFile(software.PlantObjectTags, Path.Combine(deviceFolder, "Tags", "PlantObjects.xml"), "Item", false);
            EngineeringSnapshotRestorer.ApplyFile(software.DiscreteAlarms, Path.Combine(deviceFolder, "Alarms", "DiscreteAlarms.xml"), "Alarm", true);
            EngineeringSnapshotRestorer.ApplyFile(software.AnalogAlarms, Path.Combine(deviceFolder, "Alarms", "AnalogAlarms.xml"), "Alarm", true);
            EngineeringSnapshotRestorer.ApplyFile(software.AlarmClasses, Path.Combine(deviceFolder, "Alarms", "AlarmClasses.xml"), "Alarm", true);
            EngineeringSnapshotRestorer.ApplyFile(software.OpcUaAlarmTypes, Path.Combine(deviceFolder, "Alarms", "OpcUaAlarmTypes.xml"), "Item", true);
            EngineeringSnapshotRestorer.ApplyFile(software.HmiAlarmAuditClass, Path.Combine(deviceFolder, "Alarms", "AlarmAuditClasses.xml"), "Item", true);
            EngineeringSnapshotRestorer.ApplyFile(software.Connections, Path.Combine(deviceFolder, "Connections", "Connections.xml"), "Item", true);
            EngineeringSnapshotRestorer.ApplyFile(software.DataLogs, Path.Combine(deviceFolder, "Logs", "DataLogs.xml"), "Item", true);
            EngineeringSnapshotRestorer.ApplyFile(software.AlarmLogs, Path.Combine(deviceFolder, "Logs", "AlarmLogs.xml"), "Item", true);
            EngineeringSnapshotRestorer.ApplyFile(software.AuditTrails, Path.Combine(deviceFolder, "Logs", "AuditTrails.xml"), "Item", true);
            if (software.RuntimeSettings is IEngineeringObject runtime)
            {
                XElement settings = LoadRoot(Path.Combine(deviceFolder, "RuntimeSettings", "RuntimeSettings.xml"));
                EngineeringSnapshotRestorer.ApplyElement(runtime, settings);
            }

            if (software is IEngineeringObject engineering)
            {
                RestoreFolder(engineering, Path.Combine(deviceFolder, "Other"), true);
            }
        }

        private static void RestoreFolder(IEngineeringObject owner, string folder, bool deleteMissing)
        {
            if (owner == null || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                EngineeringSnapshotRestorer.ApplyOwnedFile(owner, file, deleteMissing);
            }
        }

        private static XElement LoadRoot(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return XDocument.Load(path).Root;
            }
            catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException)
            {
                return null;
            }
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

                try
                {
                    AppendAlarmTexts(element, alarm);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    HmiExportTrace.Error(ex);
                    element.Add(new XAttribute("textError", ex.Message));
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

                if (attribute.Value is MultilingualText multilingual)
                {
                    AppendMultilingual(parent, attribute.Key, ReadLanguageTexts(multilingual));
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

        private static void AppendAlarmTexts(XElement element, object alarm)
        {
            if (!(alarm is AlarmBase alarmBase))
            {
                return;
            }

            AppendKnownText(element, "EventText", alarmBase.EventText);
            AppendKnownText(element, "EventText1", alarmBase.EventText1);
            AppendKnownText(element, "EventText2", alarmBase.EventText2);
            AppendKnownText(element, "EventText3", alarmBase.EventText3);
            AppendKnownText(element, "EventText4", alarmBase.EventText4);
            AppendKnownText(element, "EventText5", alarmBase.EventText5);
            AppendKnownText(element, "EventText6", alarmBase.EventText6);
            AppendKnownText(element, "EventText7", alarmBase.EventText7);
            AppendKnownText(element, "EventText8", alarmBase.EventText8);
            AppendKnownText(element, "EventText9", alarmBase.EventText9);
            AppendKnownText(element, "InfoText", alarmBase.InfoText);
        }

        private static void AppendKnownText(XElement element, string name, MultilingualText text)
        {
            List<KeyValuePair<string, string>> texts = ReadLanguageTexts(text);
            if (!texts.Exists(item => !string.IsNullOrEmpty(item.Value)))
            {
                return;
            }

            AppendMultilingual(element, name, texts);
        }

        private static List<KeyValuePair<string, string>> ReadLanguageTexts(MultilingualText text)
        {
            var texts = new List<KeyValuePair<string, string>>();
            if (text?.Items == null)
            {
                return texts;
            }

            foreach (MultilingualTextItem item in text.Items)
            {
                if (item == null)
                {
                    continue;
                }

                texts.Add(new KeyValuePair<string, string>(
                    item.Language?.Culture?.Name ?? string.Empty,
                    item.Text ?? string.Empty));
            }

            return texts;
        }

        internal static void AppendMultilingual(XElement parent, string name, IEnumerable<KeyValuePair<string, string>> texts)
        {
            if (parent == null || string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            foreach (XElement existing in parent.Elements("Attribute").ToList())
            {
                XAttribute attributeName = existing.Attribute("name");
                if (attributeName == null || !string.Equals(attributeName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (existing.Elements("Text").Any())
                {
                    return;
                }

                existing.Remove();
            }

            var element = new XElement("Attribute", new XAttribute("name", name));
            if (texts != null)
            {
                foreach (KeyValuePair<string, string> text in texts)
                {
                    element.Add(new XElement(
                        "Text",
                        new XAttribute("language", text.Key ?? string.Empty),
                        text.Value ?? string.Empty));
                }
            }

            parent.Add(element);
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
