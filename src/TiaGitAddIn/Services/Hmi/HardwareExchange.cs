using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Xml;
using System.Linq;
using System.Xml.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.MC.Drives;
using Siemens.Engineering.MC.Drives.DFI;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Writes the project hardware tree, networks, and offline SINAMICS drive parameters to HardwareExport.
    /// Only parameters that differ from the factory value are written. PLC blocks stay on the VCI path.
    /// </summary>
    internal static class HardwareExchange
    {
        private const string ChecklistDevice = "Hardware";

        private static ExportCategorySet scope = ExportCategorySet.All;

        private static void Skip(Action<string> report, string category)
        {
            report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "skipped", 0, "Turned off in Settings."));
        }

        public static OperationResult Export(
            TiaPortal portal,
            string repositoryPath,
            CancellationToken ct,
            Action<string> report,
            ExportCategorySet? categories = null)
        {
            if (portal == null)
            {
                throw new ArgumentNullException(nameof(portal));
            }

            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                return OperationResult.Fail("Choose a Git repository before exporting hardware.");
            }

            string root = Path.Combine(repositoryPath, "HardwareExport");
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }

            Directory.CreateDirectory(root);
            Announce(report);
            ExportCategorySet previousScope = scope;
            scope = categories ?? ExportCategorySet.All;

            int devices = 0;
            int modules = 0;
            int seenDevices = 0;
            int networks = 0;
            int drives = 0;
            int parameters = 0;
            int telegrams = 0;
            var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var deviceFolders = new List<string>();
            try
            {
            bool writeDevices = scope.AllowsHardware("Devices");
            bool writeModules = scope.AllowsHardware("Modules");
            if (!writeDevices)
            {
                Skip(report, "Devices");
            }

            if (!writeModules)
            {
                Skip(report, "Modules");
            }

            if (writeDevices)
            {
                Step(report, "Devices", 0, string.Empty);
            }

            if (writeModules)
            {
                Step(report, "Modules", 0, string.Empty);
            }

            DispatcherPump.Yield();
            foreach (Project project in portal.Projects)
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in project.Devices)
                {
                    CollectDevice(device, root, usedFolders, deviceFolders, ct, report, ref devices, ref modules, ref seenDevices);
                }

                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    CollectGroup(group, root, usedFolders, deviceFolders, ct, report, ref devices, ref modules, ref seenDevices);
                }
            }

            if (writeDevices)
            {
                Finish(report, "Devices", devices);
            }

            if (writeModules)
            {
                Finish(report, "Modules", modules);
            }

            if (!scope.AllowsHardware("Networks"))
            {
                Skip(report, "Networks");
            }
            else
            {
                Step(report, "Networks", 0, string.Empty);
                DispatcherPump.Yield();
                foreach (Project project in portal.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    networks += WriteSubnets(project, root, ct, report);
                    DispatcherPump.Yield();
                }

                Finish(report, "Networks", networks);
            }

            bool writeDrives = scope.AllowsHardware("Drives");
            bool writeParameters = scope.AllowsHardware("Parameters");
            bool writeTelegrams = scope.AllowsHardware("Telegrams");
            if (!writeDrives && !writeParameters && !writeTelegrams)
            {
                Skip(report, "Drives");
                Skip(report, "Parameters");
                Skip(report, "Telegrams");
            }
            else
            {
                if (writeDrives)
                {
                    Step(report, "Drives", 0, string.Empty);
                }
                else
                {
                    Skip(report, "Drives");
                }

                if (writeParameters)
                {
                    Step(report, "Parameters", 0, string.Empty);
                }
                else
                {
                    Skip(report, "Parameters");
                }

                if (writeTelegrams)
                {
                    Step(report, "Telegrams", 0, string.Empty);
                }
                else
                {
                    Skip(report, "Telegrams");
                }

                DispatcherPump.Yield();
                var seenDrives = new List<DriveObject>();
                int folderIndex = 0;
                foreach (Project project in portal.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (Device device in project.Devices)
                    {
                        ExportDrives(device, deviceFolders[folderIndex], seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
                        folderIndex++;
                    }

                    foreach (DeviceUserGroup group in project.DeviceGroups)
                    {
                        ExportDriveGroup(group, deviceFolders, ref folderIndex, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
                    }
                }

                if (writeDrives)
                {
                    Finish(report, "Drives", drives);
                }

                if (writeParameters)
                {
                    Finish(report, "Parameters", parameters);
                }

                if (writeTelegrams)
                {
                    Finish(report, "Telegrams", telegrams);
                }
            }

            if (seenDevices == 0)
            {
                return OperationResult.Fail("No hardware device is open in TIA Portal.");
            }

            return OperationResult.Ok(
                "Exported " + devices.ToString(CultureInfo.InvariantCulture) + " device(s), " +
                modules.ToString(CultureInfo.InvariantCulture) + " module(s), " +
                drives.ToString(CultureInfo.InvariantCulture) + " drive object(s), and " +
                parameters.ToString(CultureInfo.InvariantCulture) + " drive parameter(s) to HardwareExport. " +
                "PLC blocks stay on the existing Git path. Stage that folder to keep it in Git.");
            }
            finally
            {
                scope = previousScope;
            }
        }

        public static OperationResult Import(TiaPortal portal, string repositoryPath, CancellationToken ct, Action<string> report)
        {
            if (portal == null)
            {
                throw new ArgumentNullException(nameof(portal));
            }

            if (string.IsNullOrWhiteSpace(repositoryPath))
            {
                return OperationResult.Fail("Choose a Git repository before importing hardware.");
            }

            string root = Path.Combine(repositoryPath, "HardwareExport");
            if (!Directory.Exists(root))
            {
                return OperationResult.Fail("This repository has no HardwareExport folder yet. Export the hardware first.");
            }

            foreach (string category in new[] { "Networks", "Ports", "Parameters" })
            {
                report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "pending", 0, string.Empty));
            }

            DispatcherPump.Yield();

            int nodes = 0;
            int links = 0;
            int parameters = 0;
            int restored = 0;
            int missed = 0;
            string subnetFile = Path.Combine(root, "Networks", "Subnets.xml");
            if (File.Exists(subnetFile))
            {
                XDocument document;
                try
                {
                    document = XDocument.Load(subnetFile);
                }
                catch (Exception ex) when (ex is IOException || ex is XmlException)
                {
                    return OperationResult.Fail("Could not read the network export.", ex.Message);
                }

                var projectNodes = new List<Node>();
                var portDevices = new List<string>();
                var portModules = new List<string>();
                var ports = new List<NetworkPort>();
                foreach (Project project in portal.Projects)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (Device device in project.Devices)
                    {
                        CollectProjectNodes(device.DeviceItems, projectNodes, ct);
                        CollectPorts(device.Name ?? string.Empty, device.DeviceItems, portDevices, portModules, ports, ct);
                    }

                    foreach (DeviceUserGroup group in project.DeviceGroups)
                    {
                        CollectGroupNodes(group, projectNodes, portDevices, portModules, ports, ct);
                    }
                }

                if (document.Root != null)
                {
                    foreach (XElement node in document.Root.Descendants("Node"))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (ApplyNode(projectNodes, node))
                        {
                            nodes++;
                            if (nodes == 1 || nodes % 10 == 0)
                            {
                                Step(report, "Networks", nodes, Attribute(node, "name"));
                            }
                        }
                        else if (HasAddress(node))
                        {
                            missed++;
                        }
                    }

                    XElement linksElement = document.Root.Element("PortLinks");
                    if (linksElement != null)
                    {
                        foreach (XElement link in linksElement.Elements("Link"))
                        {
                            ct.ThrowIfCancellationRequested();
                            if (ApplyLink(portDevices, portModules, ports, link))
                            {
                                links++;
                                Step(report, "Ports", links, Attribute(link, "device") + " — " + Attribute(link, "module"));
                            }
                            else
                            {
                                missed++;
                            }
                        }
                    }
                }
            }

            Finish(report, "Networks", nodes);
            Finish(report, "Ports", links);

            foreach (string deviceFolder in Directory.GetDirectories(root))
            {
                string drivesFolder = Path.Combine(deviceFolder, "Drives");
                if (!Directory.Exists(drivesFolder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(drivesFolder, "*.xml"))
                {
                    ct.ThrowIfCancellationRequested();
                    XDocument driveDocument;
                    try
                    {
                        driveDocument = XDocument.Load(file);
                    }
                    catch (Exception ex) when (ex is IOException || ex is XmlException)
                    {
                        missed++;
                        continue;
                    }

                    XElement driveElement = driveDocument.Root;
                    if (driveElement == null || driveElement.Name.LocalName != "DriveObject")
                    {
                        continue;
                    }

                    DriveObject drive = FindDrive(
                        portal,
                        Attribute(driveElement, "device"),
                        Attribute(driveElement, "module"),
                        Attribute(driveElement, "number"),
                        ct);
                    if (drive != null)
                    {
                        restored += ApplyDriveSnapshot(drive, driveElement);
                    }

                    XElement parameterList = driveElement.Element("Parameters");
                    if (parameterList == null)
                    {
                        continue;
                    }

                    foreach (XElement parameter in parameterList.Elements("Parameter"))
                    {
                        ct.ThrowIfCancellationRequested();
                        if (drive != null && ApplyParameter(drive, parameter))
                        {
                            parameters++;
                            if (parameters == 1 || parameters % 25 == 0)
                            {
                                Step(report, "Parameters", parameters, Attribute(parameter, "name"));
                            }
                        }
                        else
                        {
                            missed++;
                        }
                    }
                }
            }

            Finish(report, "Parameters", parameters);
            restored += ApplyHardwareSnapshots(portal, root, ct);
            if (nodes == 0 && links == 0 && parameters == 0 && restored == 0)
            {
                return OperationResult.Fail(
                    "No hardware settings were imported.",
                    missed == 0
                        ? "HardwareExport has no node addresses, port links, or drive parameters."
                        : missed.ToString(CultureInfo.InvariantCulture) + " item(s) had no match in the open project. Devices and modules are not created or deleted.");
            }

            string message =
                "Imported " + nodes.ToString(CultureInfo.InvariantCulture) + " network node(s), " +
                links.ToString(CultureInfo.InvariantCulture) + " port link(s), and " +
                parameters.ToString(CultureInfo.InvariantCulture) + " drive parameter(s). " +
                (restored > 0
                    ? "Wrote " + restored.ToString(CultureInfo.InvariantCulture) + " hardware setting(s). "
                    : string.Empty) +
                "Devices and modules are not created or deleted.";
            if (missed > 0)
            {
                message += " " + missed.ToString(CultureInfo.InvariantCulture) + " item(s) had no match in the open project.";
            }

            return OperationResult.Ok(message);
        }

        private static void CollectGroup(
            DeviceUserGroup group,
            string root,
            ISet<string> usedFolders,
            List<string> deviceFolders,
            CancellationToken ct,
            Action<string> report,
            ref int devices,
            ref int modules,
            ref int seenDevices)
        {
            foreach (Device device in group.Devices)
            {
                CollectDevice(device, root, usedFolders, deviceFolders, ct, report, ref devices, ref modules, ref seenDevices);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                CollectGroup(child, root, usedFolders, deviceFolders, ct, report, ref devices, ref modules, ref seenDevices);
            }
        }

        private static void CollectDevice(
            Device device,
            string root,
            ISet<string> usedFolders,
            List<string> deviceFolders,
            CancellationToken ct,
            Action<string> report,
            ref int devices,
            ref int modules,
            ref int seenDevices)
        {
            ct.ThrowIfCancellationRequested();
            seenDevices++;
            bool writeDevices = scope.AllowsHardware("Devices");
            bool writeModules = scope.AllowsHardware("Modules");
            string folderName = HmiExportStore.UniqueName(HmiExportStore.FolderName(device.Name), usedFolders);
            string folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);
            deviceFolders.Add(folder);

            if (writeDevices || writeModules)
            {
                var module = new XElement(
                    "Device",
                    new XAttribute("name", device.Name ?? string.Empty),
                    new XAttribute("type", device.TypeIdentifier ?? string.Empty));
                if (writeDevices)
                {
                    devices++;
                    Step(report, "Devices", devices, device.Name);
                    AppendAttributes(module, device);
                }

                if (writeModules)
                {
                    var usedSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (DeviceItem item in device.DeviceItems)
                    {
                        module.Add(WriteItem(item, folder, usedSettings, ct, report, ref modules));
                    }
                }

                new XDocument(module).Save(Path.Combine(folder, "Hardware.xml"));
            }

            DispatcherPump.Yield();
        }

        private static void ExportDriveGroup(
            DeviceUserGroup group,
            IReadOnlyList<string> deviceFolders,
            ref int folderIndex,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            foreach (Device device in group.Devices)
            {
                ExportDrives(device, deviceFolders[folderIndex], seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
                folderIndex++;
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                ExportDriveGroup(child, deviceFolders, ref folderIndex, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
            }
        }

        private static void ExportDrives(
            Device device,
            string folder,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            var usedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string deviceName = device.Name ?? string.Empty;
            foreach (DeviceItem item in device.DeviceItems)
            {
                VisitDrives(item, deviceName, folder, usedDrives, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
            }

            DispatcherPump.Yield();
        }

        private static void VisitDrives(
            DeviceItem item,
            string deviceName,
            string folder,
            ISet<string> usedDrives,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            ct.ThrowIfCancellationRequested();
            WriteItemDrives(item, deviceName, folder, usedDrives, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
            foreach (DeviceItem child in item.DeviceItems)
            {
                VisitDrives(child, deviceName, folder, usedDrives, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
            }
        }

        private static XElement WriteItem(
            DeviceItem item,
            string folder,
            ISet<string> usedSettings,
            CancellationToken ct,
            Action<string> report,
            ref int modules)
        {
            ct.ThrowIfCancellationRequested();
            modules++;
            if (modules == 1 || modules % 25 == 0)
            {
                Step(report, "Modules", modules, item.Name ?? string.Empty);
            }

            var element = new XElement(
                "Module",
                new XAttribute("name", item.Name ?? string.Empty),
                new XAttribute("type", item.TypeIdentifier ?? string.Empty),
                new XAttribute("position", item.PositionNumber.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("classification", item.Classification.ToString()));
            AppendAttributes(element, item);
            WriteAddresses(element, item);
            NoteSoftware(element, item);
            CollectSettings(item, folder, usedSettings);

            foreach (DeviceItem child in item.DeviceItems)
            {
                element.Add(WriteItem(child, folder, usedSettings, ct, report, ref modules));
            }

            return element;
        }

        private static void NoteSoftware(XElement element, DeviceItem item)
        {
            try
            {
                SoftwareContainer container = item.GetService<SoftwareContainer>();
                if (container?.Software != null)
                {
                    element.SetAttributeValue("software", container.Software.GetType().Name);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static void WriteItemDrives(
            DeviceItem item,
            string deviceName,
            string folder,
            ISet<string> usedDrives,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            DriveObjectContainer container;
            try
            {
                container = item.GetService<DriveObjectContainer>();
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (container?.DriveObjects == null)
            {
                return;
            }

            foreach (DriveObject drive in container.DriveObjects)
            {
                ct.ThrowIfCancellationRequested();
                if (drive == null || AlreadySeen(seenDrives, drive))
                {
                    continue;
                }

                seenDrives.Add(drive);
                string label = DriveLabel(drive);
                if (scope.AllowsHardware("Drives"))
                {
                    drives++;
                    Step(report, "Drives", drives, label);
                }

                if (!scope.AllowsHardware("Drives") && !scope.AllowsHardware("Parameters") && !scope.AllowsHardware("Telegrams"))
                {
                    continue;
                }

                string driveFolder = Path.Combine(folder, "Drives");
                Directory.CreateDirectory(driveFolder);
                string fileName = HmiExportStore.UniqueName((item.Name ?? "Module") + "_" + label, usedDrives);
                WriteDriveFile(
                    Path.Combine(driveFolder, fileName + ".xml"),
                    item.Name ?? string.Empty,
                    deviceName,
                    drive,
                    ct,
                    report,
                    ref parameters,
                    ref telegrams);
            }
        }

        private static void CollectSettings(DeviceItem item, string folder, ISet<string> usedSettings)
        {
            try
            {
                DriveFunctionInterface functions = item.GetService<DriveFunctionInterface>();
                TechnologyExtensionContainer extensions = item.GetService<TechnologyExtensionContainer>();
                if (functions == null && (extensions == null || extensions.TechnologyExtensions == null))
                {
                    return;
                }

                string settingsFolder = Path.Combine(folder, "Settings");
                Directory.CreateDirectory(settingsFolder);
                string path = Path.Combine(
                    settingsFolder,
                    HmiExportStore.UniqueName(item.Name ?? "Module", usedSettings) + ".xml");
                var document = new XElement("DriveSettings", new XAttribute("module", item.Name ?? string.Empty));
                if (functions != null)
                {
                    document.Add(Snapshot("Functions", functions, 3));
                }

                if (extensions?.TechnologyExtensions != null)
                {
                    var list = new XElement("TechnologyExtensions");
                    foreach (TechnologyExtension extension in extensions.TechnologyExtensions)
                    {
                        if (extension != null)
                        {
                            list.Add(Snapshot("Extension", extension, 2));
                        }
                    }

                    document.Add(list);
                }

                new XDocument(document).Save(path);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static void WriteDriveFile(
            string path,
            string module,
            string deviceName,
            DriveObject drive,
            CancellationToken ct,
            Action<string> report,
            ref int parameters,
            ref int telegrams)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false)
            };
            using (XmlWriter writer = XmlWriter.Create(path, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("DriveObject");
                writer.WriteAttributeString("module", module ?? string.Empty);
                writer.WriteAttributeString("device", deviceName ?? string.Empty);
                writer.WriteAttributeString("number", drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("name", DriveLabel(drive));
                if (scope.AllowsHardware("Drives"))
                {
                    WriteAttributeElements(writer, drive);
                }

                if (scope.AllowsHardware("Parameters"))
                {
                    parameters += WriteParameters(writer, drive, ct, report, parameters);
                }

                if (scope.AllowsHardware("Telegrams"))
                {
                    telegrams += WriteTelegrams(writer, drive, report, telegrams);
                }
                WriteSecurity(writer, drive);
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
        }

        private static readonly string[] DefaultAttributeNames =
        {
            "DefaultValue",
            "FactoryValue",
            "FactorySetting",
            "ResetValue",
            "DefaultSetting"
        };

        private static int WriteParameters(XmlWriter writer, DriveObject drive, CancellationToken ct, Action<string> report, int already)
        {
            writer.WriteStartElement("Parameters");
            writer.WriteAttributeString("source", "Parameters");
            var kept = new List<DriveParameter>();
            int omitted = 0;
            string? defaultAttribute = null;
            bool filterKnown = false;
            try
            {
                if (drive.Parameters != null)
                {
                    foreach (DriveParameter parameter in drive.Parameters)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (parameter == null)
                        {
                            continue;
                        }

                        if (!filterKnown)
                        {
                            defaultAttribute = FindDefaultAttribute(parameter);
                            filterKnown = true;
                        }

                        if (MatchesDefault(parameter, defaultAttribute))
                        {
                            omitted++;
                            if (omitted == 1 || omitted % 200 == 0)
                            {
                                Step(report, "Parameters", already + kept.Count, omitted.ToString(CultureInfo.InvariantCulture) + " at default");
                            }

                            continue;
                        }

                        kept.Add(parameter);
                        int total = already + kept.Count;
                        if (kept.Count == 1 || total % 25 == 0)
                        {
                            Step(report, "Parameters", total, parameter.Name);
                        }
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            writer.WriteAttributeString("filter", defaultAttribute == null ? "accessible" : "differs-from-default");
            if (defaultAttribute != null)
            {
                writer.WriteAttributeString("defaultAttribute", defaultAttribute);
                writer.WriteAttributeString("omitted", omitted.ToString(CultureInfo.InvariantCulture));
            }

            foreach (DriveParameter parameter in kept)
            {
                WriteParameter(
                    writer,
                    parameter.Number,
                    parameter.Name,
                    parameter.ArrayIndex,
                    parameter.ArrayLength,
                    parameter.Value,
                    parameter.Unit,
                    parameter.MinValue,
                    parameter.MaxValue,
                    parameter.ParameterText,
                    parameter.Bits);
            }

            writer.WriteEndElement();
            return kept.Count;
        }

        private static string? FindDefaultAttribute(DriveParameter parameter)
        {
            try
            {
                foreach (EngineeringAttributeInfo info in parameter.GetAttributeInfos())
                {
                    if (info == null || string.IsNullOrEmpty(info.Name))
                    {
                        continue;
                    }

                    foreach (string candidate in DefaultAttributeNames)
                    {
                        if (info.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                        {
                            return info.Name;
                        }
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return null;
        }

        private static bool MatchesDefault(DriveParameter parameter, string? defaultAttribute)
        {
            if (string.IsNullOrEmpty(defaultAttribute))
            {
                return false;
            }

            try
            {
                string current = Text(parameter.Value);
                string factory = Text(parameter.GetAttribute(defaultAttribute));
                return string.Equals(current, factory, StringComparison.Ordinal);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return false;
            }
        }

        private static void WriteParameter(
            XmlWriter writer,
            object number,
            object name,
            object arrayIndex,
            object arrayLength,
            object value,
            object unit,
            object min,
            object max,
            object text,
            object bits)
        {
            writer.WriteStartElement("Parameter");
            writer.WriteAttributeString("number", Text(number));
            writer.WriteAttributeString("name", Text(name));
            string index = Text(arrayIndex);
            string length = Text(arrayLength);
            if (index.Length > 0 && index != "0")
            {
                writer.WriteAttributeString("index", index);
            }

            if (length.Length > 0 && length != "0" && length != "1")
            {
                writer.WriteAttributeString("length", length);
            }

            writer.WriteAttributeString("value", Text(value));
            string unitText = Text(unit);
            if (unitText.Length > 0)
            {
                writer.WriteAttributeString("unit", unitText);
            }

            string minText = Text(min);
            string maxText = Text(max);
            if (minText.Length > 0)
            {
                writer.WriteAttributeString("min", minText);
            }

            if (maxText.Length > 0)
            {
                writer.WriteAttributeString("max", maxText);
            }

            string description = Text(text);
            if (description.Length > 0)
            {
                writer.WriteAttributeString("text", description);
            }

            WriteBits(writer, bits);
            writer.WriteEndElement();
        }

        private static void WriteBits(XmlWriter writer, object bits)
        {
            if (!(bits is IEnumerable items) || bits is string)
            {
                return;
            }

            foreach (object bit in items)
            {
                if (!(bit is IEngineeringObject engineering))
                {
                    continue;
                }

                writer.WriteStartElement("Bit");
                writer.WriteAttributeString("name", TryGet(engineering, "Name"));
                writer.WriteAttributeString("value", TryGet(engineering, "Value"));
                writer.WriteEndElement();
            }
        }

        private static int WriteTelegrams(XmlWriter writer, DriveObject drive, Action<string> report, int already)
        {
            writer.WriteStartElement("Telegrams");
            int count = 0;
            try
            {
                if (drive.Telegrams != null)
                {
                    foreach (Telegram telegram in drive.Telegrams)
                    {
                        if (telegram == null)
                        {
                            continue;
                        }

                        writer.WriteStartElement("Telegram");
                        writer.WriteAttributeString("number", telegram.TelegramNumber.ToString(CultureInfo.InvariantCulture));
                        writer.WriteAttributeString("type", telegram.Type.ToString());
                        writer.WriteEndElement();
                        count++;
                        Step(report, "Telegrams", already + count, telegram.TelegramNumber.ToString(CultureInfo.InvariantCulture));
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            writer.WriteEndElement();
            return count;
        }

        private static void WriteSecurity(XmlWriter writer, DriveObject drive)
        {
            try
            {
                object security = drive.Security;
                if (security is IEngineeringObject engineering)
                {
                    writer.WriteStartElement("Security");
                    WriteAttributeElements(writer, engineering);
                    writer.WriteEndElement();
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static int WriteSubnets(Project project, string root, CancellationToken ct, Action<string> report)
        {
            if (project.Subnets == null)
            {
                return 0;
            }

            var document = new XElement("Subnets");
            int count = 0;
            foreach (Subnet subnet in project.Subnets)
            {
                ct.ThrowIfCancellationRequested();
                if (subnet == null)
                {
                    continue;
                }

                count++;
                Step(report, "Networks", count, subnet.Name ?? string.Empty);
                var element = new XElement(
                    "Subnet",
                    new XAttribute("name", subnet.Name ?? string.Empty),
                    new XAttribute("netType", subnet.NetType.ToString()));
                AppendAttributes(element, subnet);
                AppendNodes(element, subnet.Nodes);
                AppendNamed(element, "IoSystem", subnet.IoSystems);
                document.Add(element);
            }

            AppendUnassignedNodes(document, project, ct);
            AppendPortLinks(document, project, ct);
            if (document.HasElements)
            {
                string folder = Path.Combine(root, "Networks");
                Directory.CreateDirectory(folder);
                new XDocument(document).Save(Path.Combine(folder, "Subnets.xml"));
            }

            return count;
        }

        private static readonly string[] AddressAttributeNames = { "Address", "IpAddress" };

        private static readonly string[] MaskAttributeNames = { "SubnetMask", "NetworkMask" };

        private static readonly string[] RouterUsedAttributeNames = { "UseRouter" };

        private static readonly string[] RouterAttributeNames = { "RouterAddress", "Router", "DefaultGateway" };

        private static readonly string[] ProfinetAttributeNames = { "PnDeviceName", "ProfinetDeviceName", "PnName" };

        private static void AppendNodes(XElement parent, IEnumerable nodes)
        {
            if (nodes == null)
            {
                return;
            }

            try
            {
                foreach (object entry in nodes)
                {
                    if (entry is Node node)
                    {
                        parent.Add(NodeElement(node));
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static void AppendUnassignedNodes(XElement document, Project project, CancellationToken ct)
        {
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement node in document.Descendants("Node"))
            {
                XAttribute id = node.Attribute("nodeId");
                if (id != null && id.Value.Length > 0)
                {
                    assigned.Add(id.Value);
                }
            }

            var extra = new XElement("UnassignedNodes");
            foreach (Device device in project.Devices)
            {
                ct.ThrowIfCancellationRequested();
                AppendInterfaceNodes(extra, device.DeviceItems, assigned, ct);
            }

            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                AppendGroupInterfaceNodes(extra, group, assigned, ct);
            }

            if (extra.HasElements)
            {
                document.Add(extra);
            }
        }

        private static void AppendGroupInterfaceNodes(XElement parent, DeviceUserGroup group, ISet<string> assigned, CancellationToken ct)
        {
            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                AppendInterfaceNodes(parent, device.DeviceItems, assigned, ct);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                AppendGroupInterfaceNodes(parent, child, assigned, ct);
            }
        }

        private static void AppendInterfaceNodes(XElement parent, IEnumerable<DeviceItem> items, ISet<string> assigned, CancellationToken ct)
        {
            if (items == null)
            {
                return;
            }

            foreach (DeviceItem item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    NetworkInterface network = item.GetService<NetworkInterface>();
                    if (network?.Nodes != null)
                    {
                        foreach (Node node in network.Nodes)
                        {
                            if (node == null)
                            {
                                continue;
                            }

                            string id = node.NodeId ?? string.Empty;
                            if (id.Length > 0 && !assigned.Add(id))
                            {
                                continue;
                            }

                            parent.Add(NodeElement(node));
                        }
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                AppendInterfaceNodes(parent, item.DeviceItems, assigned, ct);
            }
        }

        private static XElement NodeElement(Node node)
        {
            var element = new XElement(
                "Node",
                new XAttribute("name", node.Name ?? string.Empty),
                new XAttribute("nodeId", node.NodeId ?? string.Empty),
                new XAttribute("type", node.NodeType.ToString()));
            SetAttribute(element, "address", FirstAttribute(node, AddressAttributeNames));
            SetAttribute(element, "subnetMask", FirstAttribute(node, MaskAttributeNames));
            SetAttribute(element, "useRouter", FirstAttribute(node, RouterUsedAttributeNames));
            SetAttribute(element, "router", FirstAttribute(node, RouterAttributeNames));
            SetAttribute(element, "profinetName", FirstAttribute(node, ProfinetAttributeNames));
            AppendAttributes(element, node);
            DropPromotedAttributes(element);
            return element;
        }

        private static void SetAttribute(XElement element, string name, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                element.SetAttributeValue(name, value);
            }
        }

        private static string FirstAttribute(IEngineeringObject engineering, string[] names)
        {
            foreach (string name in names)
            {
                string value = TryGet(engineering, name);
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static void DropPromotedAttributes(XElement element)
        {
            var promoted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            promoted.UnionWith(AddressAttributeNames);
            promoted.UnionWith(MaskAttributeNames);
            promoted.UnionWith(RouterUsedAttributeNames);
            promoted.UnionWith(RouterAttributeNames);
            promoted.UnionWith(ProfinetAttributeNames);
            foreach (XElement attribute in element.Elements("Attribute").ToList())
            {
                XAttribute name = attribute.Attribute("name");
                if (name != null && promoted.Contains(name.Value))
                {
                    attribute.Remove();
                }
            }
        }

        private static void AppendPortLinks(XElement document, Project project, CancellationToken ct)
        {
            var devices = new List<string>();
            var modules = new List<string>();
            var ports = new List<NetworkPort>();
            foreach (Device device in project.Devices)
            {
                ct.ThrowIfCancellationRequested();
                CollectPorts(device.Name ?? string.Empty, device.DeviceItems, devices, modules, ports, ct);
            }

            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                CollectGroupPorts(group, devices, modules, ports, ct);
            }

            var links = new XElement("PortLinks");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < ports.Count; index++)
            {
                ct.ThrowIfCancellationRequested();
                NetworkPort port = ports[index];
                IEnumerable partners;
                try
                {
                    partners = port.ConnectedPorts;
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    continue;
                }

                if (partners == null)
                {
                    continue;
                }

                foreach (object entry in partners)
                {
                    if (!(entry is NetworkPort partner))
                    {
                        continue;
                    }

                    int other = IndexOfPort(ports, partner);
                    string partnerDevice = other >= 0 ? devices[other] : string.Empty;
                    string partnerModule = other >= 0 ? modules[other] : string.Empty;
                    string left = devices[index] + "\n" + modules[index];
                    string right = partnerDevice + "\n" + partnerModule;
                    string key = string.Compare(left, right, StringComparison.Ordinal) <= 0 ? left + "\0" + right : right + "\0" + left;
                    if (!seen.Add(key) || (other == index))
                    {
                        continue;
                    }

                    links.Add(new XElement(
                        "Link",
                        new XAttribute("device", devices[index]),
                        new XAttribute("module", modules[index]),
                        new XAttribute("partnerDevice", partnerDevice),
                        new XAttribute("partnerModule", partnerModule)));
                }
            }

            if (links.HasElements)
            {
                document.Add(links);
            }
        }

        private static void CollectGroupPorts(
            DeviceUserGroup group,
            List<string> devices,
            List<string> modules,
            List<NetworkPort> ports,
            CancellationToken ct)
        {
            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                CollectPorts(device.Name ?? string.Empty, device.DeviceItems, devices, modules, ports, ct);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                CollectGroupPorts(child, devices, modules, ports, ct);
            }
        }

        private static void CollectPorts(
            string deviceName,
            IEnumerable<DeviceItem> items,
            List<string> devices,
            List<string> modules,
            List<NetworkPort> ports,
            CancellationToken ct)
        {
            if (items == null)
            {
                return;
            }

            foreach (DeviceItem item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    NetworkPort port = item.GetService<NetworkPort>();
                    if (port != null && IndexOfPort(ports, port) < 0)
                    {
                        devices.Add(deviceName);
                        modules.Add(item.Name ?? string.Empty);
                        ports.Add(port);
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                CollectPorts(deviceName, item.DeviceItems, devices, modules, ports, ct);
            }
        }

        private static int IndexOfPort(List<NetworkPort> ports, NetworkPort port)
        {
            for (int index = 0; index < ports.Count; index++)
            {
                try
                {
                    if (ports[index].Equals(port))
                    {
                        return index;
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            return -1;
        }

        private static void AppendNamed(XElement parent, string elementName, IEnumerable items)
        {
            if (items == null)
            {
                return;
            }

            try
            {
                foreach (object entry in items)
                {
                    if (entry is IEngineeringObject engineering)
                    {
                        parent.Add(new XElement(elementName, new XAttribute("name", TryGet(engineering, "Name"))));
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static void WriteAddresses(XElement parent, DeviceItem item)
        {
            try
            {
                if (item.Addresses == null)
                {
                    return;
                }

                foreach (Address address in item.Addresses)
                {
                    if (address == null)
                    {
                        continue;
                    }

                    parent.Add(new XElement(
                        "Address",
                        new XAttribute("io", address.IoType.ToString()),
                        new XAttribute("start", address.StartAddress.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("length", address.Length.ToString(CultureInfo.InvariantCulture))));
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static XElement Snapshot(string elementName, IEngineeringObject engineering, int depth)
        {
            var element = new XElement(elementName);
            string name = TryGet(engineering, "Name");
            if (name.Length > 0)
            {
                element.SetAttributeValue("name", name);
            }

            AppendAttributes(element, engineering);
            if (depth <= 0)
            {
                return element;
            }

            IEnumerable<EngineeringCompositionInfo> infos;
            try
            {
                infos = engineering.GetCompositionInfos();
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return element;
            }

            if (infos == null)
            {
                return element;
            }

            foreach (EngineeringCompositionInfo info in infos)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.Name))
                {
                    continue;
                }

                IEngineeringCompositionOrObject composition;
                try
                {
                    composition = engineering.GetComposition(info.Name);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    continue;
                }

                if (!(composition is IEnumerable items))
                {
                    continue;
                }

                var list = new XElement("Composition", new XAttribute("name", info.Name));
                foreach (object child in items)
                {
                    if (child is IEngineeringObject childObject)
                    {
                        list.Add(Snapshot("Item", childObject, depth - 1));
                    }
                }

                element.Add(list);
            }

            return element;
        }

        private static void AppendAttributes(XElement parent, IEngineeringObject engineering)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddAttributes(parent, seen, engineering, AttributeAccessOptions.ReadWrite);
            AddAttributes(parent, seen, engineering, AttributeAccessOptions.ReadOnly);
        }

        private static void AddAttributes(XElement parent, ISet<string> seen, IEngineeringObject engineering, AttributeAccessOptions access)
        {
            IEnumerable attributes;
            try
            {
                attributes = engineering.GetAttributes(access);
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

                if (SkipAttribute(attribute.Key, attribute.Value))
                {
                    continue;
                }

                parent.Add(new XElement(
                    "Attribute",
                    new XAttribute("name", attribute.Key),
                    new XAttribute("value", Text(attribute.Value))));
            }
        }

        private static void WriteAttributeElements(XmlWriter writer, IEngineeringObject engineering)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WriteAccess(writer, seen, engineering, AttributeAccessOptions.ReadWrite);
            WriteAccess(writer, seen, engineering, AttributeAccessOptions.ReadOnly);
        }

        private static void WriteAccess(XmlWriter writer, ISet<string> seen, IEngineeringObject engineering, AttributeAccessOptions access)
        {
            IEnumerable attributes;
            try
            {
                attributes = engineering.GetAttributes(access);
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

                if (SkipAttribute(attribute.Key, attribute.Value))
                {
                    continue;
                }

                writer.WriteStartElement("Attribute");
                writer.WriteAttributeString("name", attribute.Key);
                writer.WriteAttributeString("value", Text(attribute.Value));
                writer.WriteEndElement();
            }
        }

        private static bool SkipAttribute(string name, object value)
        {
            if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Items", StringComparison.OrdinalIgnoreCase)
                || name.Equals("DeviceItems", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Parameters", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ReadParameters", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Telegrams", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return value is IEnumerable && !(value is string);
        }

        private static bool AlreadySeen(List<DriveObject> seen, DriveObject drive)
        {
            foreach (DriveObject existing in seen)
            {
                if (ReferenceEquals(existing, drive))
                {
                    return true;
                }

                try
                {
                    if (existing.Equals(drive))
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            return false;
        }

        private static string DriveLabel(DriveObject drive)
        {
            string name = TryGet(drive, "Name");
            if (name.Length > 0)
            {
                return name;
            }

            return "DO" + drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture);
        }

        private static string TryGet(IEngineeringObject engineering, string name)
        {
            try
            {
                return Text(engineering.GetAttribute(name));
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return string.Empty;
            }
        }

        private static string Text(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string text)
            {
                return Clean(text);
            }

            if (value is IEngineeringObject engineering)
            {
                string name = string.Empty;
                try
                {
                    object named = engineering.GetAttribute("Name");
                    name = named as string ?? string.Empty;
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                return Clean(engineering.GetType().Name + (name.Length == 0 ? string.Empty : " " + name));
            }

            string rendered;
            try
            {
                rendered = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                rendered = value.ToString();
            }

            return Clean(rendered);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (character == '\t' || character == '\n' || character == '\r' || (character >= ' ' && character != '\uFFFE' && character != '\uFFFF'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static void Announce(Action<string> report)
        {
            foreach (string category in HmiExportProgress.HardwareCategories)
            {
                report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "pending", 0, string.Empty));
            }

            DispatcherPump.Yield();
        }

        private static void Step(Action<string> report, string category, int count, string detail)
        {
            report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "running", count, detail ?? string.Empty));
        }

        private static void Finish(Action<string> report, string category, int count)
        {
            report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "done", count, string.Empty));
            DispatcherPump.Yield();
        }

        private static void CollectGroupNodes(
            DeviceUserGroup group,
            List<Node> nodes,
            List<string> portDevices,
            List<string> portModules,
            List<NetworkPort> ports,
            CancellationToken ct)
        {
            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                CollectProjectNodes(device.DeviceItems, nodes, ct);
                CollectPorts(device.Name ?? string.Empty, device.DeviceItems, portDevices, portModules, ports, ct);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                CollectGroupNodes(child, nodes, portDevices, portModules, ports, ct);
            }
        }

        private static void CollectProjectNodes(IEnumerable<DeviceItem> items, List<Node> nodes, CancellationToken ct)
        {
            if (items == null)
            {
                return;
            }

            foreach (DeviceItem item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    NetworkInterface network = item.GetService<NetworkInterface>();
                    if (network?.Nodes != null)
                    {
                        foreach (Node node in network.Nodes)
                        {
                            if (node != null)
                            {
                                nodes.Add(node);
                            }
                        }
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                CollectProjectNodes(item.DeviceItems, nodes, ct);
            }
        }

        private static bool HasAddress(XElement node)
        {
            return Attribute(node, "address").Length > 0
                || Attribute(node, "subnetMask").Length > 0
                || Attribute(node, "router").Length > 0
                || Attribute(node, "profinetName").Length > 0;
        }

        private static bool ApplyNode(List<Node> nodes, XElement exported)
        {
            string nodeId = Attribute(exported, "nodeId");
            string name = Attribute(exported, "name");
            Node? match = null;
            foreach (Node node in nodes)
            {
                if (nodeId.Length > 0 && string.Equals(node.NodeId ?? string.Empty, nodeId, StringComparison.OrdinalIgnoreCase))
                {
                    match = node;
                    break;
                }
            }

            if (match == null && name.Length > 0)
            {
                foreach (Node node in nodes)
                {
                    if (string.Equals(node.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    {
                        match = node;
                        break;
                    }
                }
            }

            if (match == null || !HasAddress(exported))
            {
                return false;
            }

            return Assign(match, AddressAttributeNames, Attribute(exported, "address"))
                & Assign(match, MaskAttributeNames, Attribute(exported, "subnetMask"))
                & Assign(match, RouterUsedAttributeNames, Attribute(exported, "useRouter"))
                & Assign(match, RouterAttributeNames, Attribute(exported, "router"))
                & Assign(match, ProfinetAttributeNames, Attribute(exported, "profinetName"));
        }

        private static bool Assign(IEngineeringObject target, string[] names, string value)
        {
            if (value.Length == 0 || FirstAttribute(target, names) == value)
            {
                return true;
            }

            foreach (string name in names)
            {
                if (TryAssign(target, name, value))
                {
                    return true;
                }

                if (bool.TryParse(value, out bool flag) && TryAssign(target, name, flag))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryAssign(IEngineeringObject target, string name, object value)
        {
            try
            {
                target.SetAttribute(name, value);
                return true;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return false;
            }
        }

        private static bool ApplyLink(List<string> devices, List<string> modules, List<NetworkPort> ports, XElement link)
        {
            int left = FindPort(devices, modules, Attribute(link, "device"), Attribute(link, "module"));
            int right = FindPort(devices, modules, Attribute(link, "partnerDevice"), Attribute(link, "partnerModule"));
            if (left < 0 || right < 0 || left == right)
            {
                return false;
            }

            try
            {
                if (ports[left].ConnectedPorts != null)
                {
                    foreach (object entry in ports[left].ConnectedPorts)
                    {
                        if (entry is NetworkPort partner && partner.Equals(ports[right]))
                        {
                            return true;
                        }
                    }
                }

                ports[left].ConnectToPort(ports[right]);
                return true;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return false;
            }
        }

        private static int FindPort(List<string> devices, List<string> modules, string device, string module)
        {
            for (int index = 0; index < devices.Count; index++)
            {
                if (string.Equals(devices[index], device, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(modules[index], module, StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private static DriveObject? FindDrive(TiaPortal portal, string deviceName, string moduleName, string number, CancellationToken ct)
        {
            foreach (Project project in portal.Projects)
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in project.Devices)
                {
                    if (!string.Equals(device.Name ?? string.Empty, deviceName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    DriveObject found = FindDriveOnItems(device.DeviceItems, moduleName, number, ct);
                    if (found != null)
                    {
                        return found;
                    }
                }

                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    DriveObject found = FindDriveInGroup(group, deviceName, moduleName, number, ct);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static DriveObject? FindDriveInGroup(DeviceUserGroup group, string deviceName, string moduleName, string number, CancellationToken ct)
        {
            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                if (!string.Equals(device.Name ?? string.Empty, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DriveObject found = FindDriveOnItems(device.DeviceItems, moduleName, number, ct);
                if (found != null)
                {
                    return found;
                }
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                DriveObject found = FindDriveInGroup(child, deviceName, moduleName, number, ct);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static DriveObject? FindDriveOnItems(IEnumerable<DeviceItem> items, string moduleName, string number, CancellationToken ct)
        {
            if (items == null)
            {
                return null;
            }

            foreach (DeviceItem item in items)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (moduleName.Length == 0 || string.Equals(item.Name ?? string.Empty, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        DriveObjectContainer container = item.GetService<DriveObjectContainer>();
                        if (container?.DriveObjects != null)
                        {
                            foreach (DriveObject drive in container.DriveObjects)
                            {
                                if (drive != null && drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture) == number)
                                {
                                    return drive;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }

                DriveObject child = FindDriveOnItems(item.DeviceItems, moduleName, number, ct);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool ApplyParameter(DriveObject drive, XElement exported)
        {
            string number = Attribute(exported, "number");
            string name = Attribute(exported, "name");
            string index = Attribute(exported, "index");
            string value = Attribute(exported, "value");
            if (drive.Parameters == null || value.Length == 0)
            {
                return false;
            }

            try
            {
                foreach (DriveParameter parameter in drive.Parameters)
                {
                    if (parameter == null || Text(parameter.Number) != number)
                    {
                        continue;
                    }

                    if (name.Length > 0 && !string.Equals(Text(parameter.Name), name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (index.Length > 0 && Text(parameter.ArrayIndex) != index)
                    {
                        continue;
                    }

                    if (Text(parameter.Value) == value)
                    {
                        return true;
                    }

                    return TryAssign((IEngineeringObject)parameter, "Value", value);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return false;
        }

        private static string Attribute(XElement element, string name)
        {
            XAttribute attribute = element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static int ApplyDriveSnapshot(DriveObject drive, XElement driveElement)
        {
            int applied = 0;
            if (drive is IEngineeringObject engineering)
            {
                applied += EngineeringSnapshotRestorer.ApplyElement(engineering, driveElement);
            }

            XElement security = driveElement.Element("Security");
            try
            {
                if (security != null && drive.Security is IEngineeringObject securityObject)
                {
                    applied += EngineeringSnapshotRestorer.ApplyElement(securityObject, security);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            XElement telegrams = driveElement.Element("Telegrams");
            if (telegrams == null || drive.Telegrams == null)
            {
                return applied;
            }

            foreach (XElement element in telegrams.Elements("Telegram"))
            {
                string number = Attribute(element, "number");
                foreach (Telegram telegram in drive.Telegrams)
                {
                    if (telegram == null || telegram.TelegramNumber.ToString(CultureInfo.InvariantCulture) != number)
                    {
                        continue;
                    }

                    if (TrySetTelegramType(telegram, Attribute(element, "type")))
                    {
                        applied++;
                    }

                    if (telegram is IEngineeringObject telegramObject)
                    {
                        applied += EngineeringSnapshotRestorer.ApplyElement(telegramObject, element);
                    }
                }
            }

            return applied;
        }

        private static bool TrySetTelegramType(Telegram telegram, string typeName)
        {
            if (telegram == null || string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            try
            {
                PropertyInfo property = telegram.GetType().GetProperty("Type");
                if (property == null || !property.CanWrite)
                {
                    return telegram is IEngineeringObject target && TryAssign(target, "Type", typeName);
                }

                if (!EngineeringSnapshotRestorer.TryCoerce(property.GetValue(telegram), typeName, out object typed))
                {
                    return false;
                }

                property.SetValue(telegram, typed);
                return true;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return false;
            }
        }

        private static int ApplyHardwareSnapshots(TiaPortal portal, string root, CancellationToken ct)
        {
            int applied = 0;
            string subnetFile = Path.Combine(root, "Networks", "Subnets.xml");
            if (File.Exists(subnetFile))
            {
                try
                {
                    XDocument document = XDocument.Load(subnetFile);
                    foreach (Project project in portal.Projects)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (project.Subnets == null || document.Root == null)
                        {
                            continue;
                        }

                        foreach (XElement element in document.Root.Elements("Subnet"))
                        {
                            string name = Attribute(element, "name");
                            foreach (Subnet subnet in project.Subnets)
                            {
                                if (subnet != null && string.Equals(subnet.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase) && subnet is IEngineeringObject engineering)
                                {
                                    applied += EngineeringSnapshotRestorer.ApplyElement(engineering, element);
                                }
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            foreach (string deviceFolder in Directory.GetDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                applied += ApplyDeviceSnapshot(portal, deviceFolder, ct);
            }

            return applied;
        }

        private static int ApplyDeviceSnapshot(TiaPortal portal, string deviceFolder, CancellationToken ct)
        {
            string hardwareFile = Path.Combine(deviceFolder, "Hardware.xml");
            if (!File.Exists(hardwareFile))
            {
                return 0;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(hardwareFile);
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException)
            {
                return 0;
            }

            if (document.Root == null)
            {
                return 0;
            }

            string deviceName = Attribute(document.Root, "name");
            Device device = FindNamedDevice(portal, deviceName, ct);
            int applied = 0;
            if (device is IEngineeringObject deviceObject)
            {
                applied += EngineeringSnapshotRestorer.ApplyElement(deviceObject, document.Root);
            }

            if (device != null)
            {
                applied += ApplyModuleSnapshot(device.DeviceItems, document.Root.Elements("Module"), ct);
            }

            string settingsFolder = Path.Combine(deviceFolder, "Settings");
            if (device != null && Directory.Exists(settingsFolder))
            {
                foreach (string file in Directory.GetFiles(settingsFolder, "*.xml"))
                {
                    ct.ThrowIfCancellationRequested();
                    applied += ApplySettingsFile(device, file);
                }
            }

            return applied;
        }

        private static int ApplyModuleSnapshot(IEnumerable<DeviceItem> items, IEnumerable<XElement> elements, CancellationToken ct)
        {
            if (items == null || elements == null)
            {
                return 0;
            }

            var modules = new List<DeviceItem>();
            foreach (DeviceItem item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (item != null)
                {
                    modules.Add(item);
                }
            }

            int applied = 0;
            foreach (XElement element in elements)
            {
                string name = Attribute(element, "name");
                foreach (DeviceItem module in modules)
                {
                    if (!string.Equals(module.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (module is IEngineeringObject engineering)
                    {
                        applied += EngineeringSnapshotRestorer.ApplyElement(engineering, element);
                    }

                    applied += ApplyModuleSnapshot(module.DeviceItems, element.Elements("Module"), ct);
                }
            }

            return applied;
        }

        private static int ApplySettingsFile(Device device, string path)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(path);
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException)
            {
                return 0;
            }

            if (document.Root == null)
            {
                return 0;
            }

            DeviceItem module = FindModule(device.DeviceItems, Attribute(document.Root, "module"));
            if (module == null)
            {
                return 0;
            }

            int applied = 0;
            try
            {
                DriveFunctionInterface functions = module.GetService<DriveFunctionInterface>();
                if (functions is IEngineeringObject functionObject)
                {
                    applied += EngineeringSnapshotRestorer.ApplyElement(functionObject, document.Root.Element("Functions"));
                }

                TechnologyExtensionContainer extensions = module.GetService<TechnologyExtensionContainer>();
                XElement list = document.Root.Element("TechnologyExtensions");
                if (extensions?.TechnologyExtensions != null && list != null)
                {
                    applied += EngineeringSnapshotRestorer.ApplyMembers(extensions.TechnologyExtensions, list.Elements("Extension"), false);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return applied;
        }

        private static Device FindNamedDevice(TiaPortal portal, string name, CancellationToken ct)
        {
            foreach (Project project in portal.Projects)
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in project.Devices)
                {
                    if (device != null && string.Equals(device.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
                }

                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    Device found = FindNamedDevice(group, name, ct);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        private static Device FindNamedDevice(DeviceUserGroup group, string name, CancellationToken ct)
        {
            if (group == null)
            {
                return null;
            }

            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                if (device != null && string.Equals(device.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                Device found = FindNamedDevice(child, name, ct);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static DeviceItem FindModule(IEnumerable<DeviceItem> items, string name)
        {
            if (items == null || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (DeviceItem item in items)
            {
                if (item == null)
                {
                    continue;
                }

                if (string.Equals(item.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }

                DeviceItem child = FindModule(item.DeviceItems, name);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsSessionDead(Exception ex)
        {
            if (ex is NonRecoverableException || ex is EngineeringObjectDisposedException)
            {
                throw new InvalidOperationException(
                    "TIA Portal closed the Openness session while exporting hardware. " + ex.Message,
                    ex);
            }

            return false;
        }
    }
}
