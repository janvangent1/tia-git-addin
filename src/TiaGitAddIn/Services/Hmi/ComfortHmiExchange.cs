using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.Hmi.Communication;
using Siemens.Engineering.Hmi.Globalization;
using Siemens.Engineering.Hmi.Cycle;
using Siemens.Engineering.Hmi.RuntimeScripting;
using Siemens.Engineering.Hmi.Screen;
using Siemens.Engineering.Hmi.Tag;
using Siemens.Engineering.Hmi.TextGraphicList;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Writes a classic WinCC Comfort panel as Siemens screen XML and reads that folder back.
    /// </summary>
    internal static class ComfortHmiExchange
    {
        private static Action<string>? progressReport;

        public static int Export(
            HmiTarget target,
            string deviceFolder,
            CancellationToken ct,
            Action<string> report,
            ExportCategorySet? categories = null)
        {
            Action<string>? previousReport = progressReport;
            string previousKind = progressKind;
            ExportCategorySet previousCategories = enabledCategories;
            progressReport = report;
            enabledCategories = categories ?? ExportCategorySet.All;
            try
            {
                return ExportCore(target, deviceFolder, ct);
            }
            finally
            {
                progressReport = previousReport;
                progressKind = previousKind;
                enabledCategories = previousCategories;
            }
        }

        private static int ExportCore(HmiTarget target, string deviceFolder, CancellationToken ct)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            progressDevice = target.Name ?? string.Empty;
            foreach (string category in HmiExportProgress.ComfortCategories)
            {
                Emit(category, "pending", 0, string.Empty);
            }

            int count = 0;
            count += Run("Screens", "screen", () => ExportScreens(target.ScreenFolder, Path.Combine(deviceFolder, "Screens"), ct));
            count += Run("Popups", "popup", () => ExportPopups(target.ScreenPopupFolder, Path.Combine(deviceFolder, "Popups"), ct));
            count += Run("Templates", "template", () => ExportTemplates(target.ScreenTemplateFolder, Path.Combine(deviceFolder, "Templates"), ct));
            count += Run("Slide-ins", "slide-in", () => ExportSlideins(target.ScreenSlideinFolder, Path.Combine(deviceFolder, "Slideins"), ct));
            count += Run("Tags", "tag table", () => ExportTagTables(target.TagFolder, Path.Combine(deviceFolder, "TagTables"), ct));
            count += Run("Scripts", "script", () => ExportScripts(target.VBScriptFolder, Path.Combine(deviceFolder, "Scripts"), ct));
            count += Run("Text lists", "text list", () => ExportNamed(target.TextLists, Path.Combine(deviceFolder, "TextLists"), list => list.Name, (list, file) => list.Export(file, ExportOptions.WithDefaults), ct));
            count += Run("Connections", "connection", () => ExportNamed(target.Connections, Path.Combine(deviceFolder, "Connections"), connection => connection.Name, (connection, file) => connection.Export(file, ExportOptions.WithDefaults), ct));
            count += Run("Cycles", "cycle", () => ExportCycles(target.Cycles, Path.Combine(deviceFolder, "Cycles"), ct));
            count += Run("Permanent area", "permanent area", () => ExportSingle(target.ScreenGlobalElements, Path.Combine(deviceFolder, "GlobalElements.xml"), (item, file) => item.Export(file, ExportOptions.WithDefaults), ct));
            count += Run("Overview", "overview", () => ExportSingle(target.ScreenOverview, Path.Combine(deviceFolder, "Overview.xml"), (item, file) => item.Export(file, ExportOptions.WithDefaults), ct));
            count += Run("Graphic lists", "graphic list", () => ExportGraphicListSnapshots(target, Path.Combine(deviceFolder, "GraphicLists"), ct));
            count += Run("Alarms", "alarm", () => ExportAlarmCompositions(target, Path.Combine(deviceFolder, "Alarms"), ct));
            count += ExportOpenCompositions(target, deviceFolder, ct);
            return count;
        }

        private static ExportCategorySet enabledCategories = ExportCategorySet.All;

        private static string progressKind = "item";

        private static string progressDevice = string.Empty;

        private static string progressCategory = string.Empty;

        private static int progressCount;

        private static int Run(string category, string kind, Func<int> export)
        {
            if (!enabledCategories.AllowsHmi(category))
            {
                Emit(category, "skipped", 0, "Turned off in Settings.");
                return 0;
            }

            progressKind = kind;
            progressCategory = category;
            progressCount = 0;
            Emit(category, "running", 0, string.Empty);
            int count = export();
            Emit(category, "done", count, string.Empty);
            progressCategory = string.Empty;
            return count;
        }

        private static void Emit(string category, string state, int count, string detail)
        {
            progressReport?.Invoke(HmiExportProgress.Format(progressDevice, category, state, count, detail));
        }

        private static void Report(string message)
        {
            if (string.IsNullOrEmpty(progressCategory))
            {
                progressReport?.Invoke(message);
                return;
            }

            progressCount++;
            Emit(progressCategory, "running", progressCount, message);
        }

        public static void Import(HmiTarget target, string deviceFolder, CancellationToken ct)
        {
            ImportScreens(target.ScreenFolder, Path.Combine(deviceFolder, "Screens"), ct);
            ImportPopups(target.ScreenPopupFolder, Path.Combine(deviceFolder, "Popups"), ct);
            ImportTemplates(target.ScreenTemplateFolder, Path.Combine(deviceFolder, "Templates"), ct);
            ImportSlideins(target.ScreenSlideinFolder, Path.Combine(deviceFolder, "Slideins"), ct);
            ImportTagTables(target.TagFolder, Path.Combine(deviceFolder, "TagTables"), ct);
            ImportScripts(target.VBScriptFolder, Path.Combine(deviceFolder, "Scripts"), ct);
            ImportNamed(target.TextLists, Path.Combine(deviceFolder, "TextLists"), file => target.TextLists.Import(file, ImportOptions.Override), list => list.Name, list => true, list => list.Delete(), ct);
            ImportNamed(target.Connections, Path.Combine(deviceFolder, "Connections"), file => target.Connections.Import(file, ImportOptions.Override), connection => connection.Name, connection => true, connection => connection.Delete(), ct);
            ImportNamed(target.Cycles, Path.Combine(deviceFolder, "Cycles"), file => target.Cycles.Import(file, ImportOptions.Override), cycle => cycle.Name, cycle => !cycle.IsSystemObject, cycle => cycle.Delete(), ct);
            ImportSystemCycles(target, Path.Combine(deviceFolder, "Cycles", "System"), ct);
            ImportSingle(Path.Combine(deviceFolder, "GlobalElements.xml"), file => target.ImportScreenGlobalElements(file, ImportOptions.Override), ct);
            ImportSingle(Path.Combine(deviceFolder, "Overview.xml"), file => target.ImportScreenOverview(file, ImportOptions.Override), ct);
            RestoreGraphicLists(target, Path.Combine(deviceFolder, "GraphicLists"));
            if (target is IEngineeringObject engineering)
            {
                RestoreSnapshotFolder(engineering, Path.Combine(deviceFolder, "Alarms"));
                RestoreSnapshotFolder(engineering, Path.Combine(deviceFolder, "Graphics"));
                RestoreSnapshotFolder(engineering, Path.Combine(deviceFolder, "Other"));
                RestoreSnapshotFolder(engineering, Path.Combine(deviceFolder, "Recipes"));
                ImportGraphicFiles(engineering, Path.Combine(deviceFolder, "Graphics"));
            }
        }

        private static void RestoreGraphicLists(HmiTarget target, string folder)
        {
            if (target?.GraphicLists == null || !Directory.Exists(folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                XElement root;
                try
                {
                    root = XDocument.Load(file).Root;
                }
                catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException)
                {
                    continue;
                }

                if (root == null || root.Name.LocalName != "GraphicList")
                {
                    continue;
                }

                string name = (string)root.Attribute("name") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                GraphicList list = target.GraphicLists.Find(name);
                if (list is IEngineeringObject engineering)
                {
                    EngineeringSnapshotRestorer.ApplyElement(engineering, root);
                }
            }
        }

        private static void RestoreSnapshotFolder(IEngineeringObject owner, string folder)
        {
            if (owner == null || !Directory.Exists(folder))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                EngineeringSnapshotRestorer.ApplyOwnedFile(owner, file, true);
            }
        }

        private static void ImportGraphicFiles(IEngineeringObject owner, string folder)
        {
            if (owner == null || !Directory.Exists(folder))
            {
                return;
            }

            MultiLingualGraphicComposition graphics = null;
            try
            {
                foreach (EngineeringCompositionInfo info in owner.GetCompositionInfos() ?? Enumerable.Empty<EngineeringCompositionInfo>())
                {
                    if (info == null || info.Name == null || info.Name.IndexOf("graphic", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    if (owner.GetComposition(info.Name) is MultiLingualGraphicComposition composition)
                    {
                        graphics = composition;
                        break;
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (graphics == null)
            {
                return;
            }

            foreach (string file in Directory.GetFiles(folder, "*.xml"))
            {
                XElement root;
                try
                {
                    root = XDocument.Load(file).Root;
                }
                catch (Exception ex) when (ex is IOException || ex is System.Xml.XmlException)
                {
                    continue;
                }

                if (root == null || root.Name.LocalName == "HmiItems" || root.Name.LocalName == "AlarmList" || root.Name.LocalName == "GraphicList")
                {
                    continue;
                }

                try
                {
                    graphics.Import(new FileInfo(file), ImportOptions.Override);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }
        }

        private static int ExportScreens(ScreenSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = ExportNamed(folder.Screens, directory, screen => screen.Name, (screen, file) => screen.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportScreens(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportScreens(ScreenUserFolder folder, string directory, CancellationToken ct)
        {
            int count = ExportNamed(folder.Screens, directory, screen => screen.Name, (screen, file) => screen.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportScreens(child, childDirectory, ct), ct);
            return count;
        }

        private static void ImportScreens(ScreenSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return;
            }

            ImportNamed(folder.Screens, directory, file => folder.Screens.Import(file, ImportOptions.Override), screen => screen.Name, screen => true, screen => screen.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportScreens(child, childDirectory, ct), ct);
        }

        private static void ImportScreens(ScreenUserFolder folder, string directory, CancellationToken ct)
        {
            ImportNamed(folder.Screens, directory, file => folder.Screens.Import(file, ImportOptions.Override), screen => screen.Name, screen => true, screen => screen.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportScreens(child, childDirectory, ct), ct);
        }

        private static int ExportPopups(ScreenPopupSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = ExportNamed(folder.ScreenPopups, directory, popup => popup.Name, (popup, file) => popup.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportPopups(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportPopups(ScreenPopupUserFolder folder, string directory, CancellationToken ct)
        {
            int count = ExportNamed(folder.ScreenPopups, directory, popup => popup.Name, (popup, file) => popup.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportPopups(child, childDirectory, ct), ct);
            return count;
        }

        private static void ImportPopups(ScreenPopupSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return;
            }

            ImportNamed(folder.ScreenPopups, directory, file => folder.ScreenPopups.Import(file, ImportOptions.Override), popup => popup.Name, popup => true, popup => popup.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportPopups(child, childDirectory, ct), ct);
        }

        private static void ImportPopups(ScreenPopupUserFolder folder, string directory, CancellationToken ct)
        {
            ImportNamed(folder.ScreenPopups, directory, file => folder.ScreenPopups.Import(file, ImportOptions.Override), popup => popup.Name, popup => true, popup => popup.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportPopups(child, childDirectory, ct), ct);
        }

        private static int ExportTemplates(ScreenTemplateSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = ExportNamed(folder.ScreenTemplates, directory, template => template.Name, (template, file) => template.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportTemplates(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportTemplates(ScreenTemplateUserFolder folder, string directory, CancellationToken ct)
        {
            int count = ExportNamed(folder.ScreenTemplates, directory, template => template.Name, (template, file) => template.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportTemplates(child, childDirectory, ct), ct);
            return count;
        }

        private static void ImportTemplates(ScreenTemplateSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return;
            }

            ImportNamed(folder.ScreenTemplates, directory, file => folder.ScreenTemplates.Import(file, ImportOptions.Override), template => template.Name, template => true, template => template.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportTemplates(child, childDirectory, ct), ct);
        }

        private static void ImportTemplates(ScreenTemplateUserFolder folder, string directory, CancellationToken ct)
        {
            ImportNamed(folder.ScreenTemplates, directory, file => folder.ScreenTemplates.Import(file, ImportOptions.Override), template => template.Name, template => true, template => template.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportTemplates(child, childDirectory, ct), ct);
        }

        private static int ExportSlideins(ScreenSlideinSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            return ExportNamed(folder.ScreenSlideins, directory, slidein => slidein.SlideinType.ToString(), (slidein, file) => slidein.Export(file, ExportOptions.WithDefaults), ct);
        }

        private static void ImportSlideins(ScreenSlideinSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                folder.ScreenSlideins.Import(new FileInfo(file), ImportOptions.Override);
            }
        }

        private static int ExportTagTables(TagSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = ExportTagTables(folder.TagTables, folder.DefaultTagTable, directory, ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportTagTables(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportTagTables(TagUserFolder folder, string directory, CancellationToken ct)
        {
            int count = ExportNamed(folder.TagTables, directory, table => table.Name, (table, file) => table.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportTagTables(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportTagTables(TagTableComposition tables, TagTable defaultTable, string directory, CancellationToken ct)
        {
            int count = ExportNamed(tables, directory, table => table.Name, (table, file) => table.Export(file, ExportOptions.WithDefaults), ct);
            if (defaultTable != null && tables.Find(defaultTable.Name) == null)
            {
                count += ExportNamed(new[] { defaultTable }, directory, table => table.Name, (table, file) => table.Export(file, ExportOptions.WithDefaults), ct);
            }

            return count;
        }

        private static void ImportTagTables(TagSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return;
            }

            ImportNamed(folder.TagTables, directory, file => folder.TagTables.Import(file, ImportOptions.Override), table => table.Name, table => !table.IsSystemObject, table => table.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportTagTables(child, childDirectory, ct), ct);
        }

        private static void ImportTagTables(TagUserFolder folder, string directory, CancellationToken ct)
        {
            ImportNamed(folder.TagTables, directory, file => folder.TagTables.Import(file, ImportOptions.Override), table => table.Name, table => !table.IsSystemObject, table => table.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportTagTables(child, childDirectory, ct), ct);
        }

        private static int ExportScripts(VBScriptSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return 0;
            }

            int count = ExportNamed(folder.VBScripts, directory, script => script.Name, (script, file) => script.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportScripts(child, childDirectory, ct), ct);
            return count;
        }

        private static int ExportScripts(VBScriptUserFolder folder, string directory, CancellationToken ct)
        {
            int count = ExportNamed(folder.VBScripts, directory, script => script.Name, (script, file) => script.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportChildFolders(folder.Folders, directory, child => child.Name, (child, childDirectory) => ExportScripts(child, childDirectory, ct), ct);
            return count;
        }

        private static void ImportScripts(VBScriptSystemFolder folder, string directory, CancellationToken ct)
        {
            if (folder == null)
            {
                return;
            }

            ImportNamed(folder.VBScripts, directory, file => folder.VBScripts.Import(file, ImportOptions.Override), script => script.Name, script => true, script => script.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportScripts(child, childDirectory, ct), ct);
        }

        private static void ImportScripts(VBScriptUserFolder folder, string directory, CancellationToken ct)
        {
            ImportNamed(folder.VBScripts, directory, file => folder.VBScripts.Import(file, ImportOptions.Override), script => script.Name, script => true, script => script.Delete(), ct);
            ImportChildFolders(folder.Folders, directory, name => folder.Folders.Find(name) ?? folder.Folders.Create(name), child => child.Delete(), child => child.Name, (child, childDirectory) => ImportScripts(child, childDirectory, ct), ct);
        }

        private static int ExportCycles(CycleComposition cycles, string directory, CancellationToken ct)
        {
            int count = ExportNamed(UserCycles(cycles), directory, cycle => cycle.Name, (cycle, file) => cycle.Export(file, ExportOptions.WithDefaults), ct);
            count += ExportNamed(SystemCycles(cycles), Path.Combine(directory, "System"), cycle => cycle.Name, (cycle, file) => cycle.Export(file, ExportOptions.WithDefaults), ct);
            return count;
        }

        private static void ImportSystemCycles(HmiTarget target, string directory, CancellationToken ct)
        {
            if (target?.Cycles == null || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    target.Cycles.Import(new FileInfo(file), ImportOptions.Override);
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException("System cycle \"" + Path.GetFileName(file) + "\" could not be imported.", ex);
                    }
                }
            }
        }

        private static IEnumerable<Cycle> SystemCycles(CycleComposition cycles)
        {
            if (cycles == null)
            {
                return Array.Empty<Cycle>();
            }

            return cycles.Where(cycle => cycle.IsSystemObject);
        }

        private static IEnumerable<Cycle> UserCycles(CycleComposition cycles)
        {
            if (cycles == null)
            {
                return Array.Empty<Cycle>();
            }

            return cycles.Where(cycle => !cycle.IsSystemObject);
        }

        private static int ExportChildFolders<TFolder>(
            IEnumerable<TFolder> folders,
            string directory,
            Func<TFolder, string> name,
            Func<TFolder, string, int> exportChild,
            CancellationToken ct)
        {
            if (folders == null)
            {
                return 0;
            }

            int count = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TFolder child in folders)
            {
                ct.ThrowIfCancellationRequested();
                string childDirectory = Path.Combine(directory, HmiExportStore.UniqueName(name(child), used));
                count += exportChild(child, childDirectory);
            }

            return count;
        }

        private static void ImportChildFolders<TFolder>(
            IEnumerable<TFolder> folders,
            string directory,
            Func<string, TFolder> findOrCreate,
            Action<TFolder> delete,
            Func<TFolder, string> name,
            Action<TFolder, string> importChild,
            CancellationToken ct)
        {
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(directory))
            {
                foreach (string childDirectory in Directory.GetDirectories(directory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    string folderName = Path.GetFileName(childDirectory);
                    keep.Add(folderName);
                    importChild(findOrCreate(folderName), childDirectory);
                }
            }

            var existing = new List<TFolder>();
            foreach (TFolder folder in folders)
            {
                existing.Add(folder);
            }

            foreach (TFolder folder in existing)
            {
                if (!keep.Contains(name(folder) ?? string.Empty))
                {
                    delete(folder);
                }
            }
        }

        private static int ExportNamed<T>(
            IEnumerable<T> items,
            string directory,
            Func<T, string> name,
            Action<T, FileInfo> export,
            CancellationToken ct)
        {
            if (items == null)
            {
                return 0;
            }

            int count = 0;
            int index = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (T item in items)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                string itemName;
                try
                {
                    itemName = name(item);
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException(
                            progressKind + " " + index.ToString(CultureInfo.InvariantCulture) + " could not be read. TIA Portal ended the Openness session, so the export stopped there.",
                            ex);
                    }

                    Report(progressKind + " " + index.ToString(CultureInfo.InvariantCulture) + " skipped: the name could not be read.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(itemName))
                {
                    Report(progressKind + " " + index.ToString(CultureInfo.InvariantCulture) + " skipped: it has no name.");
                    continue;
                }

                Report(progressKind + " " + index.ToString(CultureInfo.InvariantCulture) + ": " + itemName);
                if (count == 0)
                {
                    Directory.CreateDirectory(directory);
                }

                string fileName = HmiExportStore.UniqueName(itemName, used) + ".xml";
                try
                {
                    export(item, new FileInfo(Path.Combine(directory, fileName)));
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException(
                            progressKind + " \"" + itemName + "\" could not be exported. TIA Portal ended the Openness session on that call, so nothing after it was exported. Files written before it are already in HmiExport.",
                            ex);
                    }

                    Report(progressKind + " " + itemName + " failed: " + ex.Message);
                    continue;
                }

                count++;
            }

            return count;
        }

        private static int ExportGraphicListSnapshots(HmiTarget target, string directory, CancellationToken ct)
        {
            if (target?.GraphicLists == null)
            {
                return 0;
            }

            var names = new List<string>();
            foreach (GraphicList list in target.GraphicLists)
            {
                ct.ThrowIfCancellationRequested();
                string itemName = list == null ? string.Empty : (list.Name ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(itemName))
                {
                    names.Add(itemName);
                }
            }

            int count = 0;
            int index = 0;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string listName in names)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                Report(progressKind + " " + index.ToString(CultureInfo.InvariantCulture) + ": " + listName);
                GraphicList list = target.GraphicLists.Find(listName);
                if (list == null)
                {
                    Report(progressKind + " " + listName + " skipped: it is no longer in the project.");
                    continue;
                }

                try
                {
                    if (count == 0)
                    {
                        Directory.CreateDirectory(directory);
                    }

                    string fileName = HmiExportStore.UniqueName(listName, used) + ".xml";
                    GraphicListDocument(list).Save(Path.Combine(directory, fileName));
                    count++;
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException(
                            "Graphic list \"" + listName + "\" could not be read. TIA Portal ended the Openness session on that call.",
                            ex);
                    }

                    Report(progressKind + " " + listName + " failed: " + ex.Message);
                }
            }

            return count;
        }

        private static int ExportAlarmCompositions(HmiTarget target, string directory, CancellationToken ct)
        {
            if (!(target is IEngineeringObject engineering))
            {
                Report("alarms skipped. This Comfort device does not expose engineering compositions.");
                return 0;
            }

            List<string> names;
            try
            {
                names = CompositionNames(engineering.GetCompositionInfos(), ct);
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Comfort alarm compositions could not be read. TIA Portal ended the Openness session on that call.", ex);
                }

                Report("alarms skipped: " + ex.Message);
                return 0;
            }

            HmiExportTrace.Info("Comfort compositions: " + string.Join(", ", names));
            int count = 0;
            bool found = false;
            foreach (string name in names)
            {
                if (name.IndexOf("alarm", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("message", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                found = true;
                count += ExportAlarmComposition(engineering, name, directory, ct);
            }

            if (!found)
            {
                Report("alarms skipped. Openness lists no alarm composition. Compositions: " + string.Join(", ", names));
            }

            return count;
        }

        private static int ExportAlarmComposition(IEngineeringObject engineering, string name, string directory, CancellationToken ct)
        {
            IEngineeringCompositionOrObject composition;
            try
            {
                composition = engineering.GetComposition(name);
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Comfort composition \"" + name + "\" could not be read. TIA Portal ended the Openness session on that call.", ex);
                }

                Report("alarm composition " + name + " failed: " + ex.Message);
                return 0;
            }

            var items = new List<IEngineeringObject>();
            try
            {
                if (composition is System.Collections.IEnumerable enumerable)
                {
                    foreach (object entry in enumerable)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (entry is IEngineeringObject child)
                        {
                            items.Add(child);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Comfort composition \"" + name + "\" could not be read. TIA Portal ended the Openness session on that call.", ex);
                }

                Report("alarm composition " + name + " failed: " + ex.Message);
                return 0;
            }

            if (items.Count == 0)
            {
                Report("alarm composition " + name + " is empty.");
                return 0;
            }

            var root = new XElement("AlarmList", new XAttribute("composition", name));
            int index = 0;
            foreach (IEngineeringObject item in items)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                string itemName = CompositionItemName(item, index);
                Report("alarm " + index.ToString(CultureInfo.InvariantCulture) + ": " + itemName);
                var element = new XElement("Alarm", new XAttribute("name", itemName));
                try
                {
                    AppendAttributes(element, item);
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException("Comfort alarm \"" + itemName + "\" could not be read. TIA Portal ended the Openness session on that call.", ex);
                    }

                    element.Add(new XAttribute("error", ex.Message));
                }

                root.Add(element);
            }

            Directory.CreateDirectory(directory);
            string fileName = HmiExportStore.FolderName(name) + ".xml";
            new XDocument(root).Save(Path.Combine(directory, fileName));
            return 1;
        }

        private static readonly HashSet<string> CoveredCompositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Connections", "Cycles", "GraphicLists", "TextLists",
            "ScreenFolder", "ScreenPopupFolder", "ScreenSlideinFolder", "ScreenTemplateFolder",
            "TagFolder", "VBScriptFolder", "ScreenGlobalElements", "ScreenOverview",
            "Screens", "Popups", "Templates", "Slideins", "Alarms"
        };

        private static int ExportOpenCompositions(HmiTarget target, string deviceFolder, CancellationToken ct)
        {
            if (!(target is IEngineeringObject engineering))
            {
                Emit("Graphics", "skipped", 0, "This Comfort device does not expose engineering compositions.");
                Emit("Other", "skipped", 0, "This Comfort device does not expose engineering compositions.");
                return 0;
            }

            List<string> names;
            try
            {
                names = CompositionNames(engineering.GetCompositionInfos(), ct);
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                if (IsSessionDead(ex))
                {
                    throw new InvalidOperationException("Comfort compositions could not be read. TIA Portal ended the Openness session on that call.", ex);
                }

                Emit("Graphics", "skipped", 0, ex.Message);
                Emit("Other", "skipped", 0, ex.Message);
                return 0;
            }

            int recipes = 0;
            int graphics = 0;
            int other = 0;
            var otherNames = new List<string>();
            bool sawRecipe = false;
            bool sawGraphic = false;
            foreach (string name in names)
            {
                ct.ThrowIfCancellationRequested();
                if (CoveredCompositions.Contains(name))
                {
                    continue;
                }

                bool isRecipe = name.IndexOf("recipe", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isGraphic = !isRecipe && name.IndexOf("graphic", StringComparison.OrdinalIgnoreCase) >= 0;
                string folder = isRecipe ? "Recipes" : isGraphic ? "Graphics" : "Other";
                progressCategory = isRecipe ? "Recipes" : isGraphic ? "Graphics" : "Other";
                int written;
                try
                {
                    written = SnapshotComposition(engineering, name, Path.Combine(deviceFolder, folder), ct);
                }
                catch (Exception ex)
                {
                    HmiExportTrace.Error(ex);
                    if (IsSessionDead(ex))
                    {
                        throw new InvalidOperationException("Comfort composition \"" + name + "\" could not be read. TIA Portal ended the Openness session on that call.", ex);
                    }

                    Emit(progressCategory, "failed", 0, name + ": " + ex.Message);
                    progressCategory = string.Empty;
                    continue;
                }

                if (isRecipe)
                {
                    sawRecipe = true;
                    recipes += written;
                }
                else if (isGraphic)
                {
                    sawGraphic = true;
                    graphics += written;
                }
                else if (written > 0)
                {
                    other += written;
                    otherNames.Add(name);
                }
            }

            progressCategory = string.Empty;
            if (sawRecipe)
            {
                Emit("Recipes", "done", recipes, string.Empty);
            }

            if (sawGraphic)
            {
                Emit("Graphics", "done", graphics, string.Empty);
            }
            else
            {
                Emit("Graphics", "skipped", 0, "Openness lists no graphics composition.");
            }

            if (otherNames.Count > 0)
            {
                Emit("Other", "done", other, string.Join(", ", otherNames));
            }
            else
            {
                Emit("Other", "skipped", 0, "No further HMI compositions.");
            }

            return recipes + graphics + other;
        }

        private static int SnapshotComposition(IEngineeringObject owner, string name, string directory, CancellationToken ct)
        {
            IEngineeringCompositionOrObject composition = owner.GetComposition(name);
            var items = new List<IEngineeringObject>();
            var pictures = new List<MultiLingualGraphic>();
            if (composition is System.Collections.IEnumerable enumerable)
            {
                foreach (object entry in enumerable)
                {
                    ct.ThrowIfCancellationRequested();
                    if (entry is MultiLingualGraphic graphic)
                    {
                        pictures.Add(graphic);
                    }
                    else if (entry is IEngineeringObject child)
                    {
                        items.Add(child);
                    }
                }
            }

            int count = 0;
            if (pictures.Count > 0)
            {
                Directory.CreateDirectory(directory);
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (MultiLingualGraphic graphic in pictures)
                {
                    ct.ThrowIfCancellationRequested();
                    string itemName = graphic.Name ?? string.Empty;
                    Report(string.IsNullOrWhiteSpace(itemName) ? name : itemName);
                    string fileName = HmiExportStore.UniqueName(string.IsNullOrWhiteSpace(itemName) ? name : itemName, used) + ".xml";
                    graphic.Export(new FileInfo(Path.Combine(directory, fileName)), ExportOptions.WithDefaults);
                    count++;
                }
            }

            if (items.Count == 0)
            {
                return count;
            }

            var root = new XElement("HmiItems", new XAttribute("composition", name));
            int index = 0;
            foreach (IEngineeringObject item in items)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                string itemName = CompositionItemName(item, index);
                Report(itemName);
                var element = new XElement("Item", new XAttribute("name", itemName));
                AppendAttributes(element, item);
                AppendChildCompositions(element, item, ct);
                root.Add(element);
                count++;
            }

            Directory.CreateDirectory(directory);
            new XDocument(root).Save(Path.Combine(directory, HmiExportStore.FolderName(name) + ".xml"));
            return count;
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
                    AppendComposition(items, engineering.GetComposition(info.Name));
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    items.Add(new XAttribute("error", ex.Message));
                }

                parent.Add(items);
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

        private static string CompositionItemName(IEngineeringObject item, int index)
        {
            try
            {
                object name = item.GetAttribute("Name");
                string text = Convert.ToString(name, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return "Item" + index.ToString(CultureInfo.InvariantCulture);
        }

        private static XDocument GraphicListDocument(GraphicList list)
        {
            var root = new XElement(
                "GraphicList",
                new XAttribute("name", list.Name ?? string.Empty));
            IEngineeringObject engineering = list;
            AppendAttributes(root, engineering);
            IEnumerable<EngineeringCompositionInfo> compositions = engineering.GetCompositionInfos();
            if (compositions != null)
            {
                foreach (EngineeringCompositionInfo info in compositions)
                {
                    if (info == null || string.IsNullOrWhiteSpace(info.Name))
                    {
                        continue;
                    }

                    var items = new XElement("Items", new XAttribute("composition", info.Name));
                    AppendComposition(items, engineering.GetComposition(info.Name));
                    root.Add(items);
                }
            }

            return new XDocument(root);
        }

        private static void AppendComposition(XElement parent, IEngineeringCompositionOrObject composition)
        {
            if (!(composition is System.Collections.IEnumerable enumerable))
            {
                return;
            }

            int index = 0;
            foreach (object entry in enumerable)
            {
                index++;
                var item = new XElement("Item", new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)));
                if (entry is IEngineeringObject child)
                {
                    AppendAttributes(item, child);
                }
                else if (entry != null)
                {
                    item.Add(new XAttribute("value", AttributeText(entry)));
                }

                parent.Add(item);
            }
        }

        private static void AppendAttributes(XElement parent, IEngineeringObject engineering)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAttributes(parent, seen, engineering, AttributeAccessOptions.ReadWrite);
            AddAttributes(parent, seen, engineering, AttributeAccessOptions.ReadOnly);
        }

        private static void AddAttributes(
            XElement parent,
            ISet<string> seen,
            IEngineeringObject engineering,
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
                if (!(entry is KeyValuePair<string, object> attribute) || string.IsNullOrWhiteSpace(attribute.Key))
                {
                    continue;
                }

                if (!seen.Add(attribute.Key))
                {
                    continue;
                }

                if (attribute.Value is MultilingualText text)
                {
                    var nested = new XElement("Attribute", new XAttribute("name", attribute.Key));
                    if (text.Items != null)
                    {
                        foreach (MultilingualTextItem item in text.Items)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            nested.Add(new XElement(
                                "Text",
                                new XAttribute("language", item.Language?.Culture?.Name ?? string.Empty),
                                item.Text ?? string.Empty));
                        }
                    }

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
                return "bytes:" + bytes.Length.ToString(CultureInfo.InvariantCulture);
            }

            if (value is IEngineeringObject engineering)
            {
                try
                {
                    object name = engineering.GetAttribute("Name");
                    if (name != null)
                    {
                        return Convert.ToString(name, CultureInfo.InvariantCulture) ?? string.Empty;
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                return string.Empty;
            }

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return text ?? string.Empty;
        }

        private static bool IsSessionDead(Exception ex)
        {
            return ex is NonRecoverableException || ex is EngineeringObjectDisposedException;
        }

        private static void ImportNamed<T>(
            IEnumerable<T> items,
            string directory,
            Func<FileInfo, IList<T>> import,
            Func<T, string> name,
            Func<T, bool> canDelete,
            Action<T> delete,
            CancellationToken ct)
        {
            if (items == null)
            {
                return;
            }

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory, "*.xml").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    IList<T> imported = import(new FileInfo(file));
                    if (imported == null)
                    {
                        continue;
                    }

                    foreach (T item in imported)
                    {
                        string itemName = name(item);
                        if (!string.IsNullOrEmpty(itemName))
                        {
                            keep.Add(itemName);
                        }
                    }
                }
            }

            var existing = new List<T>();
            foreach (T item in items)
            {
                existing.Add(item);
            }

            foreach (T item in existing)
            {
                if (canDelete(item) && !keep.Contains(name(item) ?? string.Empty))
                {
                    delete(item);
                }
            }
        }

        private static int ExportSingle<T>(T item, string path, Action<T, FileInfo> export, CancellationToken ct)
            where T : class
        {
            if (item == null)
            {
                return 0;
            }

            ct.ThrowIfCancellationRequested();
            export(item, new FileInfo(path));
            return 1;
        }

        private static void ImportSingle(string path, Action<FileInfo> import, CancellationToken ct)
        {
            if (!File.Exists(path))
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            import(new FileInfo(path));
        }
    }
}
