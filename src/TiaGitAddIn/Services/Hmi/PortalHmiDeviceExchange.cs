using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.Hmi;
using Siemens.Engineering.HmiUnified;
using Siemens.Engineering.HmiUnified.UI.Base;
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
            return RunOnCallerAsync(progress, ct, () => ExportCore(repositoryPath, progress, ct));
        }

        public Task<OperationResult> ImportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default)
        {
            return RunOnCallerAsync(progress, ct, () => ImportCore(repositoryPath, progress, ct));
        }

        private Task<OperationResult> RunOnCallerAsync(IProgress<string> progress, CancellationToken ct, Func<OperationResult> action)
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
                    return Task.FromResult(Guard(action));
                }

                progress?.Report("Waiting for TIA Portal…");
                return Task.Run(() =>
                {
                    DispatcherOperation<OperationResult> operation = dispatcher.InvokeAsync(() => Guard(action));
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
                    context.Send(_ => result = Guard(action), null);
                    return result ?? OperationResult.Fail("Could not reach TIA Portal to update the HMI device.");
                });
            }

            return Task.FromResult(OperationResult.Fail(
                "Could not export the HMI device.",
                "Open the Git panel from TIA Portal again. The panel could not return to the thread that owns the project."));
        }

        private static OperationResult Guard(Func<OperationResult> action)
        {
            // Screen export pumps Windows messages inside TIA. Disable the dispatcher until the
            // export returns so those messages cannot re-enter the add-in and take TIA down.
            HmiExportTrace.Info("HMI exchange entered the TIA thread.");
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
                    HmiDeviceExport export = ReadDevice(device, progress, ct);
                    screenCount += export.Screens.Count;
                    progress?.Report("Writing " + device.Name + "…");
                    string folder = HmiExportStore.DeviceFolder(repositoryPath, device.Name);
                    fileCount += HmiExportStore.Write(repositoryPath, export).Count;
                    fileCount += UnifiedLibraryExchange.Export(
                        device.Software,
                        folder,
                        ct,
                        message => progress?.Report(device.Name + ": " + message));
                    HmiExportNormalizer.StripCreationTimestamps(folder);
                }

                int comfortFiles = 0;
                for (int index = 0; index < comfort.Count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    ComfortDevice device = comfort[index];
                    string prefix = "WinCC Comfort " + (index + 1).ToString(CultureInfo.InvariantCulture) + " of " + comfort.Count.ToString(CultureInfo.InvariantCulture) + ": " + device.Name;
                    progress?.Report(prefix);
                    string folder = HmiExportStore.PrepareDeviceFolder(repositoryPath, device.Name, HmiDeviceExport.ComfortKind);
                    int written = ComfortHmiExchange.Export(device.Target, folder, ct, message => progress?.Report(prefix + " — " + message)) + 1;
                    HmiExportNormalizer.StripCreationTimestamps(folder);
                    comfortFiles += written;
                    fileCount += written;
                }

                return OperationResult.Ok(ExportMessage(unified.Count, screenCount, comfort.Count, comfortFiles, fileCount));
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
                if (exported.Count == 0)
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

                if (applied == 0)
                {
                    return OperationResult.Fail("Could not import the HMI export.", string.Join(" ", missing));
                }

                string message =
                    "Imported " + applied.ToString(CultureInfo.InvariantCulture) +
                    " HMI device(s) from HmiExport.";
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

        private static string ExportMessage(int unifiedDevices, int screenCount, int comfortDevices, int comfortFiles, int fileCount)
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

            string message = "Exported " + string.Join(" and ", parts) +
                " to HmiExport (" + fileCount.ToString(CultureInfo.InvariantCulture) +
                " files).";
            if (comfortDevices > 0)
            {
                message += " Comfort graphic lists are attribute files, not Siemens graphic-list exports. Comfort alarms are read from Openness when the device lists an alarm composition.";
            }

            return message + " Stage that folder to keep it in Git.";
        }

        private static HmiDeviceExport ReadDevice(UnifiedDevice device, IProgress<string>? progress, CancellationToken ct)
        {
            var export = new HmiDeviceExport
            {
                Name = device.Name,
                Kind = HmiDeviceExport.UnifiedKind
            };

            int index = 0;
            foreach (HmiScreen screen in device.Software.Screens)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                progress?.Report(device.Name + ": screen " + index.ToString(CultureInfo.InvariantCulture) + " — " + screen.Name);
                var screenExport = new HmiScreenExport
                {
                    Name = screen.Name,
                    Width = Convert.ToInt32(screen.Width, CultureInfo.InvariantCulture),
                    Height = Convert.ToInt32(screen.Height, CultureInfo.InvariantCulture)
                };

                foreach (HmiScreenItemBase item in screen.ScreenItems)
                {
                    HmiItemExport? exportedItem = ReadItem(item);
                    if (exportedItem != null)
                    {
                        screenExport.Items.Add(exportedItem);
                    }
                }

                export.Screens.Add(screenExport);
            }

            return export;
        }

        private static HmiItemExport? ReadItem(HmiScreenItemBase item)
        {
            if (item is HmiButton button)
            {
                return new HmiItemExport
                {
                    Kind = "Button",
                    Name = button.Name,
                    Left = Convert.ToInt32(button.Left, CultureInfo.InvariantCulture),
                    Top = Convert.ToInt32(button.Top, CultureInfo.InvariantCulture),
                    Width = Convert.ToInt32(button.Width, CultureInfo.InvariantCulture),
                    Height = Convert.ToInt32(button.Height, CultureInfo.InvariantCulture),
                    Texts = ReadTexts(button.Text)
                };
            }

            if (item is HmiIOField field)
            {
                return new HmiItemExport
                {
                    Kind = "IOField",
                    Name = field.Name,
                    Left = Convert.ToInt32(field.Left, CultureInfo.InvariantCulture),
                    Top = Convert.ToInt32(field.Top, CultureInfo.InvariantCulture),
                    Width = Convert.ToInt32(field.Width, CultureInfo.InvariantCulture),
                    Height = Convert.ToInt32(field.Height, CultureInfo.InvariantCulture),
                    ProcessValue = field.ProcessValue ?? string.Empty
                };
            }

            return null;
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

        private static void Apply(HmiSoftware software, HmiDeviceExport deviceExport, CancellationToken ct)
        {
            foreach (HmiScreenExport screenExport in deviceExport.Screens)
            {
                ct.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(screenExport.Name))
                {
                    continue;
                }

                HmiScreen screen = software.Screens.Find(screenExport.Name) ?? software.Screens.Create(screenExport.Name);
                screen.Width = ToSize(screenExport.Width);
                screen.Height = ToSize(screenExport.Height);

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
                    if ((item is HmiButton || item is HmiIOField) && !keep.Contains(item.Name))
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
                button.Left = itemExport.Left;
                button.Top = itemExport.Top;
                button.Width = ToSize(itemExport.Width);
                button.Height = ToSize(itemExport.Height);
                WriteTexts(button.Text, itemExport.Texts);
                return;
            }

            if (string.Equals(itemExport.Kind, "IOField", StringComparison.OrdinalIgnoreCase))
            {
                HmiIOField field = item as HmiIOField ?? screen.ScreenItems.Create<HmiIOField>(itemExport.Name);
                field.Left = itemExport.Left;
                field.Top = itemExport.Top;
                field.Width = ToSize(itemExport.Width);
                field.Height = ToSize(itemExport.Height);
                field.ProcessValue = itemExport.ProcessValue ?? string.Empty;
            }
        }

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
