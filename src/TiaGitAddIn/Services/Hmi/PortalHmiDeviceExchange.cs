using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
using Siemens.Engineering.HmiUnified.UI.ScreenGroup;
using Siemens.Engineering.HmiUnified.UI.Screens;
using Siemens.Engineering.HmiUnified.UI.Widgets;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Models;
using TiaGitAddIn.Models.Hmi;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Writes Unified and WinCC Comfort panels to <c>HmiExport/&lt;device&gt;/</c> and applies that folder
    /// back onto the open project. VCI cannot take the HMI device, so this exchange is the Git working copy for it.
    /// </summary>
    public sealed class PortalHmiDeviceExchange : IHmiDeviceExchange
    {
        private readonly object callerGate = new object();
        private readonly TiaPortal portal;
        private SynchronizationContext? callerContext;
        private Dispatcher? callerDispatcher;
        private StaThreadInvoker? callerInvoker;

        public PortalHmiDeviceExchange(TiaPortal portal)
        {
            this.portal = portal ?? throw new ArgumentNullException(nameof(portal));
            RememberCallerContext();
        }

        public void RememberCallerContext()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                return;
            }

            // TIA has no WPF dispatcher of its own. CurrentDispatcher creates one on this thread.
            // The Git panel keeps that dispatcher pumping until the panel closes.
            Dispatcher dispatcher = Dispatcher.FromThread(Thread.CurrentThread) ?? Dispatcher.CurrentDispatcher;
            SynchronizationContext? context = SynchronizationContext.Current;
            StaThreadInvoker? previous;
            lock (callerGate)
            {
                callerDispatcher = dispatcher;
                if (context != null)
                {
                    callerContext = context;
                }

                previous = callerInvoker;
                callerInvoker = null;
            }

            previous?.Dispose();
        }

        public Task<OperationResult> ExportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
        {
            return RunOnCallerAsync(progress, ct, () => ExportCore(repositoryPath, progress, ct), blockDispatcher: true);
        }

        public Task<OperationResult> ExportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
        {
            // Hardware reads do not pump the dispatcher. Leave it running so each finished
            // checklist row can paint before the next category starts.
            return RunOnCallerAsync(progress, ct, () => HardwareExchange.Export(
                portal,
                repositoryPath,
                ct,
                message => ReportProgress(progress, string.Empty, message),
                ExportCategorySet.CurrentOr(ExportCategorySet.All)), blockDispatcher: false);
        }

        public Task<OperationResult> ImportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
        {
            return RunOnCallerAsync(progress, ct, () => ImportCore(repositoryPath, progress, ct), blockDispatcher: true);
        }

        public Task<OperationResult> ImportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
        {
            return RunOnCallerAsync(progress, ct, () => HardwareExchange.Import(
                portal,
                repositoryPath,
                ct,
                message => ReportProgress(progress, string.Empty, message)), blockDispatcher: false);
        }

        private Task<OperationResult> RunOnCallerAsync(IProgress<string> progress, CancellationToken ct, Func<OperationResult> action, bool blockDispatcher)
        {
            Dispatcher? dispatcher;
            SynchronizationContext? context;
            lock (callerGate)
            {
                dispatcher = callerDispatcher;
                context = callerContext;
            }

            if (dispatcher != null)
            {
                if (dispatcher.CheckAccess())
                {
                    return Task.FromResult(Guard(action, blockDispatcher));
                }

                progress?.Report("Waiting for TIA Portal…");
                return Task.Run(() =>
                {
                    DispatcherOperation<OperationResult> operation = dispatcher.InvokeAsync(() => Guard(action, blockDispatcher));
                    try
                    {
                        operation.Task.Wait(ct);
                    }
                    catch (AggregateException ex) when (ex.InnerException is OperationCanceledException canceled)
                    {
                        throw canceled;
                    }

                    return operation.Task.Result;
                }, ct);
            }

            if (context != null)
            {
                return Task.Run(() =>
                {
                    OperationResult? result = null;
                    context.Send(_ => result = Guard(action, blockDispatcher), null);
                    return result ?? OperationResult.Fail("Could not reach TIA Portal to update the HMI device.");
                });
            }

            return Task.FromResult(OperationResult.Fail(
                "Could not export the HMI device.",
                "Open the Git panel from TIA Portal again. The panel could not return to the thread that owns the project."));
        }

        private static OperationResult Guard(Func<OperationResult> action, bool blockDispatcher)
        {
            HmiExportTrace.Info(blockDispatcher
                ? "HMI exchange entered the TIA thread."
                : "Hardware exchange entered the TIA thread.");
            if (!blockDispatcher)
            {
                return action();
            }

            // Screen export pumps Windows messages inside TIA. Disable the dispatcher until the
            // export returns so those messages cannot re-enter the add-in and take TIA down.
            using (Dispatcher.CurrentDispatcher.DisableProcessing())
            {
                return action();
            }
        }

        private OperationResult ExportCore(string repositoryPath, IProgress<string> progress, CancellationToken ct)
        {
            progress = new LoggingProgress(progress);
            try
            {
                progress?.Report("Looking for HMI devices…");
                List<UnifiedDevice> unified = new List<UnifiedDevice>();
                List<ComfortDevice> comfort = new List<ComfortDevice>();
                FindDevices(unified, comfort);
                if (unified.Count == 0 && comfort.Count == 0)
                {
                    return OperationResult.Fail("No Unified or WinCC Comfort HMI device was found in the open TIA project.");
                }

                int screenCount = 0;
                int fileCount = 0;
                for (int index = 0; index < unified.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    UnifiedDevice device = unified[index];
                    progress?.Report("Unified panel " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + unified.Count.ToString(CultureInfo.InvariantCulture) + ": " + device.Name);
                    ExportCategorySet categories = ExportCategorySet.CurrentOr(ExportCategorySet.All);
                    Announce(progress, device.Name, HmiExportProgress.UnifiedCategories);
                    HmiDeviceExport export = ReadDevice(device, progress, ct, categories);
                    screenCount += export.Screens.Count;
                    progress?.Report("Writing " + device.Name + "…");
                    string folder = HmiExportStore.DeviceFolder(repositoryPath, device.Name);
                    IReadOnlyDictionary<string, string> previousTimestamps = HmiExportNormalizer.CaptureCreationTimestamps(folder);
                    fileCount += HmiExportStore.Write(repositoryPath, export).Count;
                    fileCount += UnifiedLibraryExchange.Export(
                        device.Software,
                        device.Name,
                        folder,
                        ct,
                        message => ReportProgress(progress, device.Name + ": ", message),
                        categories);
                    HmiExportNormalizer.StabilizeCreationTimestamps(folder, previousTimestamps);
                }

                int comfortFiles = 0;
                for (int index = 0; index < comfort.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    ComfortDevice device = comfort[index];
                    string prefix = "WinCC Comfort " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + comfort.Count.ToString(CultureInfo.InvariantCulture) + ": " + device.Name;
                    progress?.Report(prefix);
                    string folder = HmiExportStore.DeviceFolder(repositoryPath, device.Name);
                    IReadOnlyDictionary<string, string> previousTimestamps = HmiExportNormalizer.CaptureCreationTimestamps(folder);
                    folder = HmiExportStore.PrepareDeviceFolder(repositoryPath, device.Name, HmiDeviceExport.ComfortKind);
                    int written = ComfortHmiExchange.Export(
                        device.Target,
                        folder,
                        ct,
                        message => ReportProgress(progress, prefix + " — ", message),
                        ExportCategorySet.CurrentOr(ExportCategorySet.All)) + 1;
                    HmiExportNormalizer.StabilizeCreationTimestamps(folder, previousTimestamps);
                    comfortFiles += written;
                    fileCount += written;
                }

                int projectFiles = 0;
                foreach (Project project in portal.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    Announce(progress, project.Name, new[] { "Pictures", "Library" });
                    projectFiles += ProjectContentExchange.Export(
                        project,
                        repositoryPath,
                        ct,
                        message => ReportProgress(progress, string.Empty, message));
                }

                fileCount += projectFiles;
                return OperationResult.Ok(ExportMessage(unified.Count, screenCount, comfort.Count, comfortFiles, fileCount, projectFiles));
            }
            catch (OperationCanceledException)
            {
                HmiExportTrace.Info("HMI export cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                return OperationResult.Fail("Could not export the HMI device.", ex.Message);
            }
        }

        private OperationResult ImportCore(string repositoryPath, IProgress<string> progress, CancellationToken ct)
        {
            progress = new LoggingProgress(progress);
            try
            {
                progress?.Report("Reading HmiExport…");
                IReadOnlyList<HmiDeviceExport> exported = HmiExportStore.ReadAll(repositoryPath);
                bool hasProjectContent = Directory.Exists(ProjectContentStore.SharedRoot(repositoryPath));
                if (exported.Count == 0 && !hasProjectContent)
                {
                    return OperationResult.Fail("This repository has no HmiExport folder yet. Export the HMI device first.");
                }

                List<UnifiedDevice> unified = new List<UnifiedDevice>();
                List<ComfortDevice> comfort = new List<ComfortDevice>();
                FindDevices(unified, comfort);
                if (unified.Count == 0 && comfort.Count == 0)
                {
                    return OperationResult.Fail("No Unified or WinCC Comfort HMI device was found in the open TIA project.");
                }

                int projectItems = 0;
                foreach (Project project in portal.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report("Importing project pictures and library types for " + project.Name + "…");
                    projectItems += ProjectContentExchange.Import(project, repositoryPath, ct);
                }

                var usedUnified = new HashSet<HmiSoftware>();
                var usedComfort = new HashSet<HmiTarget>();
                int applied = 0;
                var missing = new List<string>();
                foreach (HmiDeviceExport deviceExport in exported)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.Equals(deviceExport.Kind, HmiDeviceExport.ComfortKind, StringComparison.OrdinalIgnoreCase))
                    {
                        progress?.Report("Importing Comfort panel " + deviceExport.Name + "…");
                        ComfortDevice match = comfort.FirstOrDefault(device =>
                            !usedComfort.Contains(device.Target) &&
                            string.Equals(device.Name, deviceExport.Name, StringComparison.OrdinalIgnoreCase));
                        if (match == null)
                        {
                            missing.Add("No open WinCC Comfort HMI device is named \"" + deviceExport.Name + "\".");
                            continue;
                        }

                        usedComfort.Add(match.Target);
                        ComfortHmiExchange.Import(match.Target, HmiExportStore.DeviceFolder(repositoryPath, deviceExport.Name), ct);
                        applied++;
                        continue;
                    }

                    if (!string.Equals(deviceExport.Kind, HmiDeviceExport.UnifiedKind, StringComparison.OrdinalIgnoreCase))
                    {
                        missing.Add(deviceExport.Name + " has an unsupported HMI export.");
                        continue;
                    }

                    progress?.Report("Importing Unified panel " + deviceExport.Name + "…");
                    UnifiedDevice unifiedMatch = unified.FirstOrDefault(device =>
                        !usedUnified.Contains(device.Software) &&
                        string.Equals(device.Name, deviceExport.Name, StringComparison.OrdinalIgnoreCase));
                    if (unifiedMatch == null)
                    {
                        missing.Add("No open Unified HMI device is named \"" + deviceExport.Name + "\".");
                        continue;
                    }

                    usedUnified.Add(unifiedMatch.Software);
                    string unifiedFolder = HmiExportStore.DeviceFolder(repositoryPath, deviceExport.Name);
                    progress?.Report("Importing Unified tags and scripts for " + deviceExport.Name + "…");
                    UnifiedLibraryExchange.Import(unifiedMatch.Software, unifiedFolder, ct);
                    Apply(unifiedMatch.Software, deviceExport, ct);
                    applied++;
                }

                if (applied == 0 && projectItems == 0)
                {
                    return OperationResult.Fail("Could not import the HMI export.", string.Join(" ", missing));
                }

                string message =
                    "Imported " + applied.ToString(CultureInfo.InvariantCulture) +
                    " HMI device(s) from HmiExport.";
                if (projectItems > 0)
                {
                    message += " Wrote " + projectItems.ToString(CultureInfo.InvariantCulture) + " project picture(s) or library type(s).";
                }
                return missing.Count == 0
                    ? OperationResult.Ok(message)
                    : OperationResult.Ok(message + " " + string.Join(" ", missing));
            }
            catch (OperationCanceledException)
            {
                HmiExportTrace.Info("HMI import cancelled.");
                throw;
            }
            catch (Exception ex)
            {
                HmiExportTrace.Error(ex);
                return OperationResult.Fail("Could not import the HMI export.", ex.Message);
            }
        }

        private void FindDevices(List<UnifiedDevice> unified, List<ComfortDevice> comfort)
        {
            foreach (Project project in portal.Projects)
            {
                foreach (Device device in project.Devices)
                {
                    CollectDevice(device, unified, comfort);
                }

                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    CollectGroup(group, unified, comfort);
                }
            }
        }

        private static void CollectGroup(DeviceUserGroup group, List<UnifiedDevice> unified, List<ComfortDevice> comfort)
        {
            foreach (Device device in group.Devices)
            {
                CollectDevice(device, unified, comfort);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                CollectGroup(child, unified, comfort);
            }
        }

        private static void CollectDevice(Device device, List<UnifiedDevice> unified, List<ComfortDevice> comfort)
        {
            foreach (DeviceItem item in device.DeviceItems)
            {
                CollectItem(device.Name, item, unified, comfort);
            }
        }

        private static void CollectItem(string deviceName, DeviceItem item, List<UnifiedDevice> unified, List<ComfortDevice> comfort)
        {
            SoftwareContainer container = item.GetService<SoftwareContainer>();
            if (container?.Software is HmiSoftware software)
            {
                unified.Add(new UnifiedDevice(deviceName, software));
            }
            else if (container?.Software is HmiTarget target)
            {
                comfort.Add(new ComfortDevice(deviceName, target));
            }

            foreach (DeviceItem child in item.DeviceItems)
            {
                CollectItem(deviceName, child, unified, comfort);
            }
        }

        private static string ExportMessage(int unifiedDevices, int screenCount, int comfortDevices, int comfortFiles, int fileCount, int projectFiles)
        {
            var parts = new List<string>();
            if (unifiedDevices > 0)
            {
                parts.Add(
                    screenCount.ToString(CultureInfo.InvariantCulture) + " Unified screen(s), plus tags, alarms, and scripts, from " +
                    unifiedDevices.ToString(CultureInfo.InvariantCulture) + " device(s)");
            }

            if (comfortDevices > 0)
            {
                parts.Add(
                    comfortFiles.ToString(CultureInfo.InvariantCulture) + " WinCC Comfort file(s) from " +
                    comfortDevices.ToString(CultureInfo.InvariantCulture) + " device(s)");
            }

            if (projectFiles > 0)
            {
                parts.Add(projectFiles.ToString(CultureInfo.InvariantCulture) + " project picture(s) or library type(s)");
            }

            string message = "Exported " + string.Join(" and ", parts) +
                " to HmiExport (" + fileCount.ToString(CultureInfo.InvariantCulture) +
                " files).";
            if (comfortDevices > 0)
            {
                message += " Comfort graphic lists are attribute files, not Siemens graphic-list exports. Comfort alarms are read from Openness when the device lists an alarm composition.";
            }

            return message + " Stage that folder to keep it in Git.";
        }

        private static HmiDeviceExport ReadDevice(
            UnifiedDevice device,
            IProgress<string>? progress,
            CancellationToken ct,
            ExportCategorySet categories)
        {
            var export = new HmiDeviceExport
            {
                Name = device.Name,
                Kind = HmiDeviceExport.UnifiedKind
            };

            if (categories != null && !categories.AllowsHmi("Screens"))
            {
                progress?.Report(HmiExportProgress.Format(device.Name, "Screens", "skipped", 0, "Turned off in Settings."));
                return export;
            }

            int index = 0;
            progress?.Report(HmiExportProgress.Format(device.Name, "Screens", "running", 0, string.Empty));
            ReadScreenLevel(device.Software.Screens, device.Software.ScreenGroups, string.Empty, device.Name, export, progress, ref index, ct);
            progress?.Report(HmiExportProgress.Format(device.Name, "Screens", "done", index, string.Empty));
            return export;
        }

        private static void Announce(IProgress<string>? progress, string deviceName, IEnumerable<string> categories)
        {
            if (progress == null || categories == null)
            {
                return;
            }

            foreach (string category in categories)
            {
                progress.Report(HmiExportProgress.Format(deviceName, category, "pending", 0, string.Empty));
            }
        }

        private static void ReportProgress(IProgress<string>? progress, string prefix, string message)
        {
            if (progress == null)
            {
                return;
            }

            if (HmiExportProgress.IsChecklist(message))
            {
                progress.Report(message);
                return;
            }

            progress.Report((prefix ?? string.Empty) + (message ?? string.Empty));
        }

        private static void ReadScreenLevel(
            HmiScreenComposition screens,
            HmiScreenGroupComposition groups,
            string folder,
            string deviceName,
            HmiDeviceExport export,
            IProgress<string>? progress,
            ref int index,
            CancellationToken ct)
        {
            foreach (string name in CompositionNames(screens, screen => screen == null ? string.Empty : screen.Name, ct))
            {
                ct.ThrowIfCancellationRequested();
                HmiScreen screen = screens.Find(name);
                if (screen == null)
                {
                    continue;
                }

                index++;
                string location = string.IsNullOrEmpty(folder) ? screen.Name : folder + "/" + screen.Name;
                progress?.Report(HmiExportProgress.Format(deviceName, "Screens", "running", index, location));
                var screenExport = new HmiScreenExport
                {
                    Name = screen.Name,
                    Folder = folder,
                    Width = Convert.ToInt32(screen.Width, CultureInfo.InvariantCulture),
                    Height = Convert.ToInt32(screen.Height, CultureInfo.InvariantCulture)
                };

                foreach (HmiScreenItemBase item in screen.ScreenItems)
                {
                    ct.ThrowIfCancellationRequested();
                    HmiItemExport? exportedItem = ReadItem(item);
                    if (exportedItem != null)
                    {
                        screenExport.Items.Add(exportedItem);
                    }
                }

                screenExport.Content = UnifiedObjectSnapshot.Capture(screen, skipScreenItems: true);
                export.Screens.Add(screenExport);
            }

            foreach (string name in CompositionNames(groups, group => group == null ? string.Empty : group.Name, ct))
            {
                ct.ThrowIfCancellationRequested();
                HmiScreenGroup group = groups.Find(name);
                if (group == null)
                {
                    continue;
                }

                string child = string.IsNullOrEmpty(folder) ? name : folder + "/" + name;
                ReadScreenLevel(group.Screens, group.Groups, child, deviceName, export, progress, ref index, ct);
            }
        }

        private static List<string> CompositionNames<T>(IEnumerable<T> items, Func<T, string> name, CancellationToken ct)
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

        private static HmiItemExport ReadItem(HmiScreenItemBase item)
        {
            var exported = new HmiItemExport
            {
                Kind = WidgetKind(item),
                Name = item.Name ?? string.Empty,
                Left = WidgetNumber(item, "Left"),
                Top = WidgetNumber(item, "Top"),
                Width = WidgetNumber(item, "Width"),
                Height = WidgetNumber(item, "Height"),
                Content = UnifiedObjectSnapshot.Capture(item, skipScreenItems: false)
            };

            if (item is HmiButton button)
            {
                exported.Texts = ReadTexts(button.Text);
            }
            else if (item is HmiIOField field)
            {
                exported.ProcessValue = field.ProcessValue ?? string.Empty;
            }
            else
            {
                exported.Texts = ReadOptionalTexts(item);
                exported.ProcessValue = ReadOptionalString(item, "ProcessValue");
            }

            return exported;
        }

        private static string WidgetKind(HmiScreenItemBase item)
        {
            string name = item.GetType().Name ?? string.Empty;
            if (name.StartsWith("Hmi", StringComparison.Ordinal) && name.Length > 3)
            {
                name = name.Substring(3);
            }

            return name.Length == 0 ? "Item" : name;
        }

        private static int WidgetNumber(object item, string propertyName)
        {
            try
            {
                PropertyInfo property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null)
                {
                    return 0;
                }

                object value = property.GetValue(item);
                return value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static List<HmiTextExport> ReadOptionalTexts(object item)
        {
            try
            {
                PropertyInfo property = item.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && typeof(MultilingualText).IsAssignableFrom(property.PropertyType))
                {
                    var text = property.GetValue(item) as MultilingualText;
                    return text == null ? new List<HmiTextExport>() : ReadTexts(text);
                }
            }
            catch (Exception)
            {
            }

            return new List<HmiTextExport>();
        }

        private static string ReadOptionalString(object item, string propertyName)
        {
            try
            {
                PropertyInfo property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.PropertyType == typeof(string))
                {
                    return property.GetValue(item) as string ?? string.Empty;
                }
            }
            catch (Exception)
            {
            }

            return string.Empty;
        }

        private static List<HmiTextExport> ReadTexts(MultilingualText text)
        {
            var texts = new List<HmiTextExport>();
            if (text == null)
            {
                return texts;
            }

            foreach (MultilingualTextItem item in text.Items)
            {
                texts.Add(new HmiTextExport
                {
                    Language = item.Language?.Culture?.Name ?? string.Empty,
                    Text = item.Text ?? string.Empty
                });
            }

            return texts;
        }

        private static HmiScreenComposition ScreensIn(HmiSoftware software, string folder)
        {
            HmiScreenComposition screens = software.Screens;
            HmiScreenGroupComposition groups = software.ScreenGroups;
            if (string.IsNullOrWhiteSpace(folder))
            {
                return screens;
            }

            foreach (string segment in folder.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (groups == null)
                {
                    throw new InvalidOperationException("TIA Portal has no screen group for " + segment + ".");
                }

                HmiScreenGroup group = groups.Find(segment) ?? groups.Create(segment);
                screens = group.Screens;
                groups = group.Groups;
            }

            return screens;
        }

        private static void Apply(HmiSoftware software, HmiDeviceExport deviceExport, CancellationToken ct)
        {
            foreach (HmiScreenExport screenExport in deviceExport.Screens)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(screenExport.Name))
                {
                    continue;
                }

                HmiScreenComposition screens = ScreensIn(software, screenExport.Folder);
                HmiScreen screen = screens.Find(screenExport.Name) ?? screens.Create(screenExport.Name);
                screen.Width = ToSize(screenExport.Width);
                screen.Height = ToSize(screenExport.Height);
                if (screen is IEngineeringObject screenObject)
                {
                    EngineeringSnapshotRestorer.ApplyElement(screenObject, screenExport.Content);
                }

                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (HmiItemExport itemExport in screenExport.Items)
                {
                    if (string.IsNullOrWhiteSpace(itemExport.Name))
                    {
                        continue;
                    }

                    keep.Add(itemExport.Name);
                    ApplyItem(screen, itemExport);
                }

                var existing = new List<HmiScreenItemBase>();
                foreach (HmiScreenItemBase item in screen.ScreenItems)
                {
                    existing.Add(item);
                }

                foreach (HmiScreenItemBase item in existing)
                {
                    if (!string.IsNullOrWhiteSpace(item.Name) && !keep.Contains(item.Name))
                    {
                        item.Delete();
                    }
                }
            }
        }

        private static void ApplyItem(HmiScreen screen, HmiItemExport itemExport)
        {
            HmiScreenItemBase item = screen.ScreenItems.Find(itemExport.Name);
            if (string.Equals(itemExport.Kind, "Button", StringComparison.OrdinalIgnoreCase))
            {
                HmiButton button = item as HmiButton ?? screen.ScreenItems.Create<HmiButton>(itemExport.Name);
                SetGeometry(button, itemExport);
                WriteTexts(button.Text, itemExport.Texts);
                ApplyItemSnapshot(button, itemExport);
                return;
            }

            if (string.Equals(itemExport.Kind, "IOField", StringComparison.OrdinalIgnoreCase))
            {
                HmiIOField field = item as HmiIOField ?? screen.ScreenItems.Create<HmiIOField>(itemExport.Name);
                SetGeometry(field, itemExport);
                field.ProcessValue = itemExport.ProcessValue ?? string.Empty;
                ApplyItemSnapshot(field, itemExport);
                return;
            }

            if (item == null || !string.Equals(WidgetKind(item), itemExport.Kind, StringComparison.OrdinalIgnoreCase))
            {
                item = CreateWidget(screen, itemExport) ?? item;
            }

            if (item == null)
            {
                return;
            }

            SetGeometry(item, itemExport);
            ApplyItemSnapshot(item, itemExport);
        }

        private static void SetGeometry(HmiScreenItemBase item, HmiItemExport itemExport)
        {
            SetNumber(item, "Left", itemExport.Left);
            SetNumber(item, "Top", itemExport.Top);
            SetNumber(item, "Width", itemExport.Width);
            SetNumber(item, "Height", itemExport.Height);
        }

        private static void SetNumber(object item, string propertyName, int value)
        {
            try
            {
                PropertyInfo property = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property == null || !property.CanWrite)
                {
                    return;
                }

                property.SetValue(item, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
            }
        }

        private static void ApplyItemSnapshot(HmiScreenItemBase item, HmiItemExport itemExport)
        {
            if (item is IEngineeringObject engineering)
            {
                EngineeringSnapshotRestorer.ApplyElement(engineering, itemExport.Content);
            }
        }

        private static HmiScreenItemBase CreateWidget(HmiScreen screen, HmiItemExport itemExport)
        {
            if (screen == null || itemExport == null || string.IsNullOrWhiteSpace(itemExport.Name))
            {
                return null;
            }

            Type widgetType = WidgetType(itemExport.Kind);
            MethodInfo create = typeof(HmiScreenItemBaseComposition).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "Create" && method.IsGenericMethodDefinition && method.GetParameters().Length == 1);
            if (widgetType == null || create == null)
            {
                return null;
            }

            try
            {
                return create.MakeGenericMethod(widgetType).Invoke(screen.ScreenItems, new object[] { itemExport.Name }) as HmiScreenItemBase;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Type WidgetType(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                return null;
            }

            if (widgetTypes == null)
            {
                widgetTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
                foreach (Type type in WidgetTypes())
                {
                    if (type == null || type.IsAbstract || !typeof(HmiScreenItemBase).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    widgetTypes[type.Name] = type;
                    if (type.Name.StartsWith("Hmi", StringComparison.Ordinal) && type.Name.Length > 3)
                    {
                        widgetTypes[type.Name.Substring(3)] = type;
                    }
                }
            }

            Type match;
            return widgetTypes.TryGetValue(kind, out match) ? match : null;
        }

        private static IEnumerable<Type> WidgetTypes()
        {
            try
            {
                return typeof(HmiButton).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types ?? Array.Empty<Type>();
            }
        }

        private static Dictionary<string, Type> widgetTypes = null!;

        private static void WriteTexts(MultilingualText target, IReadOnlyList<HmiTextExport> texts)
        {
            if (target == null || texts == null)
            {
                return;
            }

            foreach (HmiTextExport text in texts)
            {
                foreach (MultilingualTextItem item in target.Items)
                {
                    string language = item.Language?.Culture?.Name ?? string.Empty;
                    if (string.Equals(language, text.Language ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Text = text.Text ?? string.Empty;
                    }
                }
            }
        }

        private static uint ToSize(int value)
        {
            return value < 0 ? 0u : (uint)value;
        }

        private sealed class LoggingProgress : IProgress<string>
        {
            private readonly IProgress<string>? inner;

            public LoggingProgress(IProgress<string>? inner)
            {
                this.inner = inner;
            }

            public void Report(string value)
            {
                HmiExportTrace.Info(value);
                inner?.Report(value);
            }
        }

        private sealed class ComfortDevice
        {
            public ComfortDevice(string name, HmiTarget target)
            {
                Name = name ?? string.Empty;
                Target = target;
            }

            public string Name { get; }

            public HmiTarget Target { get; }
        }

        private sealed class UnifiedDevice
        {
            public UnifiedDevice(string name, HmiSoftware software)
            {
                Name = name ?? string.Empty;
                Software = software;
            }

            public string Name { get; }

            public HmiSoftware Software { get; }
        }
    }
}
