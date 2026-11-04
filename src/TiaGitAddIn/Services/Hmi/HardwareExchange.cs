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
using Siemens.Engineering.Multiuser;
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
            }

            DispatcherPump.Yield();
            var seenDrives = new List<DriveObject>();
            foreach (ProjectBase project in OpenProjects(portal))
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in ListDevices(project, ct))
                {
                    CollectDevice(
                        device,
                        root,
                        usedFolders,
                        seenDrives,
                        ct,
                        report,
                        ref devices,
                        ref modules,
                        ref seenDevices,
                        ref drives,
                        ref parameters,
                        ref telegrams);
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

            if (!scope.AllowsHardware("Networks"))
            {
                Skip(report, "Networks");
            }
            else
            {
                Step(report, "Networks", 0, string.Empty);
                DispatcherPump.Yield();
                foreach (ProjectBase project in OpenProjects(portal))
                {
                    ct.ThrowIfCancellationRequested();
                    networks += WriteSubnets(project, root, ct, report);
                    DispatcherPump.Yield();
                }

                Finish(report, "Networks", networks);
            }

            if (seenDevices == 0)
            {
                int projectCount = 0;
                foreach (ProjectBase project in OpenProjects(portal))
                {
                    projectCount++;
                }

                HmiExportTrace.Info(
                    "Hardware export found " + seenDevices.ToString(CultureInfo.InvariantCulture) +
                    " device(s) in " + projectCount.ToString(CultureInfo.InvariantCulture) +
                    " open project(s). " + DescribePortal(portal));
                if (projectCount == 0)
                {
                    return OperationResult.Fail("No project is open in this TIA Portal session. " + DescribePortal(portal));
                }

                return OperationResult.Fail("No hardware device was found in the open project. The search includes devices, device groups, and Ungrouped devices.");
            }

            return OperationResult.Ok(
                "Exported " + devices.ToString(CultureInfo.InvariantCulture) + " device(s), " +
                modules.ToString(CultureInfo.InvariantCulture) + " module(s), " +
                drives.ToString(CultureInfo.InvariantCulture) + " drive object(s), and " +
                parameters.ToString(CultureInfo.InvariantCulture) + " drive parameter(s) to HardwareExport. " +
                "PLC blocks stay on the existing Git path. Stage that folder to keep it in Git.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ExportIssues.Remember("Hardware", ex);
                return OperationResult.Ok(
                    "Hardware export finished with issues. Files written so far are in HardwareExport.");
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
                foreach (ProjectBase project in OpenProjects(portal))
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (Device device in ListDevices(project, ct))
                    {
                        CollectProjectNodes(device.DeviceItems, projectNodes, ct);
                        CollectPorts(device.Name ?? string.Empty, device.DeviceItems, portDevices, portModules, ports, ct);
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

        internal static IReadOnlyList<ProjectBase> OpenProjects(TiaPortal portal)
        {
            var found = new List<ProjectBase>();
            ProjectComposition projects = portal.Projects;
            if (projects != null)
            {
                int count = projects.Count;
                for (int index = 0; index < count; index++)
                {
                    Project project = projects[index];
                    if (project != null)
                    {
                        found.Add(project);
                    }
                }
            }

            try
            {
                LocalSessionComposition sessions = portal.LocalSessions;
                if (sessions != null)
                {
                    int count = sessions.Count;
                    for (int index = 0; index < count; index++)
                    {
                        MultiuserProject project = sessions[index] == null ? null : sessions[index].Project;
                        if (project != null)
                        {
                            found.Add(project);
                        }
                    }
                }
            }
            catch (EngineeringException ex)
            {
                HmiExportTrace.Info("Local sessions were not read. " + ex.Message);
            }

            return found;
        }

        private static string DescribePortal(TiaPortal portal)
        {
            try
            {
                TiaPortalProcess process = portal.GetCurrentProcess();
                string path = process == null || process.ProjectPath == null
                    ? string.Empty
                    : process.ProjectPath.FullName;
                if (string.IsNullOrWhiteSpace(path))
                {
                    return "TIA Portal did not report an open project path.";
                }

                return "TIA Portal reports this project: " + path + ".";
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        internal static IReadOnlyList<Device> ListDevices(ProjectBase project, CancellationToken ct)
        {
            var devices = new List<Device>();
            if (project == null)
            {
                return devices;
            }

            foreach (Device device in project.Devices)
            {
                ct.ThrowIfCancellationRequested();
                RememberDevice(devices, device);
            }

            DeviceSystemGroup ungrouped = project.UngroupedDevicesGroup;
            if (ungrouped?.Devices != null)
            {
                foreach (Device device in ungrouped.Devices)
                {
                    ct.ThrowIfCancellationRequested();
                    RememberDevice(devices, device);
                }
            }

            foreach (DeviceUserGroup group in project.DeviceGroups)
            {
                ListGroupDevices(group, devices, ct);
            }

            return devices;
        }

        internal static bool IsDriveDevice(Device device)
        {
            if (device == null)
            {
                return false;
            }

            return ContainsDriveMarker(device.Name) || ContainsDriveMarker(device.TypeIdentifier);
        }

        private static bool ContainsDriveMarker(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            return value.IndexOf("SINAMICS", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("S120", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("G120", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("G220", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ListGroupDevices(DeviceUserGroup group, List<Device> devices, CancellationToken ct)
        {
            if (group == null)
            {
                return;
            }

            foreach (Device device in group.Devices)
            {
                ct.ThrowIfCancellationRequested();
                RememberDevice(devices, device);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                ListGroupDevices(child, devices, ct);
            }
        }

        private static void RememberDevice(List<Device> devices, Device device)
        {
            if (device == null)
            {
                return;
            }

            foreach (Device existing in devices)
            {
                if (existing.Equals(device))
                {
                    return;
                }
            }

            devices.Add(device);
        }

        private static void CollectDevice(
            Device device,
            string root,
            ISet<string> usedFolders,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int devices,
            ref int modules,
            ref int seenDevices,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            ct.ThrowIfCancellationRequested();
            seenDevices++;
            bool writeDevices = scope.AllowsHardware("Devices");
            bool writeModules = scope.AllowsHardware("Modules");
            bool wantDrives = scope.AllowsHardware("Drives") || scope.AllowsHardware("Parameters") || scope.AllowsHardware("Telegrams");
            string folderName = HmiExportStore.UniqueName(HmiExportStore.FolderName(device.Name), usedFolders);
            string folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            var module = new XElement(
                "Device",
                new XAttribute("name", device.Name ?? string.Empty),
                new XAttribute("type", device.TypeIdentifier ?? string.Empty));
            if (writeDevices)
            {
                devices++;
                Step(report, "Devices", devices, device.Name);
                try
                {
                    using (ExportIssues.Push(device.Name ?? "Device"))
                    {
                        AppendAttributes(module, device);
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    ExportIssues.Remember(device.Name ?? "Device", ex);
                }
            }

            if (writeModules || wantDrives)
            {
                var usedSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var usedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                string deviceName = device.Name ?? string.Empty;
                foreach (DeviceItem item in device.DeviceItems)
                {
                    XElement child;
                    try
                    {
                        child = WriteItem(
                            item,
                            deviceName,
                            folder,
                            usedSettings,
                            usedDrives,
                            seenDrives,
                            ct,
                            report,
                            ref modules,
                            ref drives,
                            ref parameters,
                            ref telegrams);
                    }
                    catch (Exception ex) when (!IsSessionDead(ex))
                    {
                        ExportIssues.Remember(deviceName, ex);
                        child = new XElement("Module", new XAttribute("name", deviceName ?? string.Empty));
                    }

                    if (writeModules)
                    {
                        module.Add(child);
                    }
                }
            }

            if (writeDevices || writeModules)
            {
                new XDocument(module).Save(Path.Combine(folder, "Hardware.xml"));
            }

            DispatcherPump.Yield();
        }

        private static XElement WriteItem(
            DeviceItem item,
            string deviceName,
            string folder,
            ISet<string> usedSettings,
            ISet<string> usedDrives,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int modules,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            ct.ThrowIfCancellationRequested();
            if (scope.AllowsHardware("Modules"))
            {
                modules++;
                if (modules == 1 || modules % 25 == 0)
                {
                    Step(report, "Modules", modules, item.Name ?? string.Empty);
                }
            }

            var element = new XElement(
                "Module",
                new XAttribute("name", item.Name ?? string.Empty),
                new XAttribute("type", item.TypeIdentifier ?? string.Empty));
            try
            {
                element.SetAttributeValue("position", item.PositionNumber.ToString(CultureInfo.InvariantCulture));
                element.SetAttributeValue("classification", item.Classification.ToString());
                if (scope.AllowsHardware("Modules"))
                {
                    using (ExportIssues.Push(item.Name ?? "Module"))
                    {
                        AppendAttributes(element, item);
                        WriteAddresses(element, item);
                        NoteSoftware(element, item);
                    }
                }

                if (WriteItemDrives(item, deviceName, folder, usedDrives, seenDrives, ct, report, ref drives, ref parameters, ref telegrams))
                {
                    CollectSettings(item, folder, usedSettings);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                ExportIssues.Remember(item.Name ?? "Module", ex);
            }

            foreach (DeviceItem child in item.DeviceItems)
            {
                element.Add(WriteItem(
                    child,
                    deviceName,
                    folder,
                    usedSettings,
                    usedDrives,
                    seenDrives,
                    ct,
                    report,
                    ref modules,
                    ref drives,
                    ref parameters,
                    ref telegrams));
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

        private static bool WriteItemDrives(
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
                return false;
            }

            if (container?.DriveObjects == null)
            {
                return false;
            }

            bool found = false;
            try
            {
            foreach (DriveObject drive in container.DriveObjects)
            {
                ct.ThrowIfCancellationRequested();
                string moduleName = item.Name ?? "Module";
                try
                {
                if (drive == null || AlreadySeen(seenDrives, drive))
                {
                    continue;
                }

                seenDrives.Add(drive);
                found = true;
                string label = DriveLabel(drive, moduleName, null);
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
                string fileName = HmiExportStore.UniqueName(moduleName + "_" + label, usedDrives);
                WriteDriveFile(
                    Path.Combine(driveFolder, fileName + ".xml"),
                    moduleName,
                    deviceName,
                    drive,
                    ct,
                    report,
                    ref parameters,
                    ref telegrams);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    ExportIssues.Remember(moduleName, ex);
                }
            }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                ExportIssues.Remember(item.Name ?? "Module", ex);
            }

            return found;
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
                string number = DriveNumber(drive, module);
                writer.WriteAttributeString("number", number);
                writer.WriteAttributeString("name", DriveLabel(drive, module, number));
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

                if (scope.AllowsHardware("Parameters"))
                {
                    Step(report, "Parameters", parameters, "safety");
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

        private struct ParameterColumns
        {
            public string[] Names;
            public int Number;
            public int Name;
            public int Index;
            public int Length;
            public int Value;
            public int Unit;
            public int Min;
            public int Max;
            public int Text;
            public int Factory;
            public string FactoryName;
        }

        private struct ExportedParameter
        {
            public string Number;
            public string Name;
            public string Index;
            public string Length;
            public string Value;
            public string Unit;
            public string Min;
            public string Max;
            public string Text;
        }

        private static int WriteParameters(XmlWriter writer, DriveObject drive, CancellationToken ct, Action<string> report, int already)
        {
            writer.WriteStartElement("Parameters");
            writer.WriteAttributeString("source", "Parameters");
            var kept = new List<ExportedParameter>();
            int omitted = 0;
            int read = 0;
            ParameterColumns columns = default;
            bool columnsKnown = false;
            try
            {
                DriveParameterComposition parameters = drive.Parameters;
                int count = parameters == null ? 0 : parameters.Count;
                for (int index = 0; index < count; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    DriveParameter parameter = parameters[index];
                    if (parameter == null)
                    {
                        continue;
                    }

                    if (!columnsKnown)
                    {
                        columns = InspectParameter(parameter);
                        columnsKnown = true;
                    }

                    if (!TryReadParameter(parameter, columns, out ExportedParameter exported, out bool atDefault))
                    {
                        continue;
                    }

                    read++;
                    if (atDefault)
                    {
                        omitted++;
                    }
                    else
                    {
                        kept.Add(exported);
                    }

                    if (read == 1 || read % 100 == 0)
                    {
                        string label = exported.Name.Length > 0 ? exported.Name : exported.Number;
                        Step(report, "Parameters", already + kept.Count, "read " + read.ToString(CultureInfo.InvariantCulture) + " " + label);
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            writer.WriteAttributeString("filter", columns.FactoryName == null ? "accessible" : "differs-from-default");
            if (columns.FactoryName != null)
            {
                writer.WriteAttributeString("defaultAttribute", columns.FactoryName);
                writer.WriteAttributeString("omitted", omitted.ToString(CultureInfo.InvariantCulture));
            }

            foreach (ExportedParameter parameter in kept)
            {
                WriteParameter(writer, parameter);
            }

            writer.WriteEndElement();
            return kept.Count;
        }

        private static ParameterColumns InspectParameter(DriveParameter parameter)
        {
            var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (EngineeringAttributeInfo info in parameter.GetAttributeInfos())
                {
                    if (info != null && !string.IsNullOrEmpty(info.Name))
                    {
                        present.Add(info.Name);
                    }
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            var names = new List<string>();
            var columns = new ParameterColumns
            {
                Number = -1,
                Name = -1,
                Index = -1,
                Length = -1,
                Value = -1,
                Unit = -1,
                Min = -1,
                Max = -1,
                Text = -1,
                Factory = -1
            };
            AddColumn(names, present, "Number", ref columns.Number);
            AddColumn(names, present, "Name", ref columns.Name);
            AddColumn(names, present, "ArrayIndex", ref columns.Index);
            AddColumn(names, present, "ArrayLength", ref columns.Length);
            AddColumn(names, present, "Value", ref columns.Value);
            AddColumn(names, present, "Unit", ref columns.Unit);
            AddColumn(names, present, "MinValue", ref columns.Min);
            AddColumn(names, present, "MaxValue", ref columns.Max);
            AddColumn(names, present, "ParameterText", ref columns.Text);
            foreach (string candidate in DefaultAttributeNames)
            {
                if (present.Contains(candidate))
                {
                    columns.FactoryName = candidate;
                    columns.Factory = names.Count;
                    names.Add(candidate);
                    break;
                }
            }

            columns.Names = names.ToArray();
            return columns;
        }

        private static void AddColumn(List<string> names, ISet<string> present, string name, ref int index)
        {
            if (!present.Contains(name))
            {
                return;
            }

            index = names.Count;
            names.Add(name);
        }

        private static bool TryReadParameter(DriveParameter parameter, ParameterColumns columns, out ExportedParameter exported, out bool atDefault)
        {
            exported = default;
            atDefault = false;
            if (columns.Factory >= 0 && columns.Value >= 0 && columns.Names != null)
            {
                try
                {
                    if (TryReadFactoryMatch(parameter, columns, out atDefault) && atDefault)
                    {
                        return true;
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            object[] values = null;
            if (columns.Names != null && columns.Names.Length > 0)
            {
                try
                {
                    IList<object> read = parameter.GetAttributes(columns.Names);
                    if (read != null)
                    {
                        values = new object[columns.Names.Length];
                        for (int index = 0; index < values.Length && index < read.Count; index++)
                        {
                            values[index] = read[index];
                        }
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            if (values == null)
            {
                exported = ReadParameterProperties(parameter);
            }
            else
            {
                exported.Number = Text(At(values, columns.Number));
                exported.Name = Text(At(values, columns.Name));
                exported.Index = Text(At(values, columns.Index));
                exported.Length = Text(At(values, columns.Length));
                exported.Value = Text(At(values, columns.Value));
                exported.Unit = Text(At(values, columns.Unit));
                exported.Min = Text(At(values, columns.Min));
                exported.Max = Text(At(values, columns.Max));
                exported.Text = Text(At(values, columns.Text));
                if (columns.Factory >= 0)
                {
                    atDefault = string.Equals(exported.Value, Text(At(values, columns.Factory)), StringComparison.Ordinal);
                }
            }

            exported.Number = exported.Number ?? string.Empty;
            exported.Name = exported.Name ?? string.Empty;
            return atDefault || exported.Number.Length > 0 || exported.Name.Length > 0;
        }

        private static bool TryReadFactoryMatch(DriveParameter parameter, ParameterColumns columns, out bool atDefault)
        {
            atDefault = false;
            IList<object> probe = parameter.GetAttributes(new[]
            {
                columns.Names[columns.Value],
                columns.Names[columns.Factory]
            });
            if (probe == null || probe.Count < 2)
            {
                return false;
            }

            atDefault = string.Equals(Text(probe[0]), Text(probe[1]), StringComparison.Ordinal);
            return true;
        }

        private static object At(object[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
            {
                return null;
            }

            return values[index];
        }

        private static ExportedParameter ReadParameterProperties(DriveParameter parameter)
        {
            var exported = new ExportedParameter();
            try
            {
                exported.Number = Text(parameter.Number);
                exported.Name = Text(parameter.Name);
                exported.Index = Text(parameter.ArrayIndex);
                exported.Length = Text(parameter.ArrayLength);
                exported.Value = Text(parameter.Value);
                exported.Unit = Text(parameter.Unit);
                exported.Min = Text(parameter.MinValue);
                exported.Max = Text(parameter.MaxValue);
                exported.Text = Text(parameter.ParameterText);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return exported;
        }

        private static void WriteParameter(XmlWriter writer, ExportedParameter parameter)
        {
            writer.WriteStartElement("Parameter");
            writer.WriteAttributeString("number", parameter.Number ?? string.Empty);
            writer.WriteAttributeString("name", parameter.Name ?? string.Empty);
            if (!string.IsNullOrEmpty(parameter.Index) && parameter.Index != "0")
            {
                writer.WriteAttributeString("index", parameter.Index);
            }

            if (!string.IsNullOrEmpty(parameter.Length) && parameter.Length != "0" && parameter.Length != "1")
            {
                writer.WriteAttributeString("length", parameter.Length);
            }

            writer.WriteAttributeString("value", parameter.Value ?? string.Empty);
            if (!string.IsNullOrEmpty(parameter.Unit))
            {
                writer.WriteAttributeString("unit", parameter.Unit);
            }

            if (!string.IsNullOrEmpty(parameter.Min))
            {
                writer.WriteAttributeString("min", parameter.Min);
            }

            if (!string.IsNullOrEmpty(parameter.Max))
            {
                writer.WriteAttributeString("max", parameter.Max);
            }

            if (!string.IsNullOrEmpty(parameter.Text))
            {
                writer.WriteAttributeString("text", parameter.Text);
            }

            writer.WriteEndElement();
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

        private static int WriteSubnets(ProjectBase project, string root, CancellationToken ct, Action<string> report)
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
                try
                {
                var element = new XElement(
                    "Subnet",
                    new XAttribute("name", subnet.Name ?? string.Empty),
                    new XAttribute("netType", subnet.NetType.ToString()));
                AppendAttributes(element, subnet);
                AppendNodes(element, subnet.Nodes);
                AppendNamed(element, "IoSystem", subnet.IoSystems);
                document.Add(element);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    ExportIssues.Remember(subnet.Name ?? "Subnet", ex);
                }
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

        private static void AppendUnassignedNodes(XElement document, ProjectBase project, CancellationToken ct)
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

        private static void AppendPortLinks(XElement document, ProjectBase project, CancellationToken ct)
        {
            var devices = new List<string>();
            var modules = new List<string>();
            var ports = new List<NetworkPort>();
            foreach (Device device in ListDevices(project, ct))
            {
                CollectPorts(device.Name ?? string.Empty, device.DeviceItems, devices, modules, ports, ct);
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
            AddAttributes(parent, seen, engineering);
        }

        private static void AddAttributes(XElement parent, ISet<string> seen, IEngineeringObject engineering)
        {
            foreach (KeyValuePair<string, object> attribute in ReadAttributes(engineering))
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || !seen.Add(attribute.Key))
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
            WriteAccess(writer, seen, engineering);
        }

        private static void WriteAccess(XmlWriter writer, ISet<string> seen, IEngineeringObject engineering)
        {
            foreach (KeyValuePair<string, object> attribute in ReadAttributes(engineering))
            {
                if (string.IsNullOrWhiteSpace(attribute.Key) || !seen.Add(attribute.Key))
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

        // GetAttributes(access) asks Openness to convert every attribute. A null number or enum
        // throws NotSupportedException on Portal's thread and TIA exits before this method can catch it.
        // Named GetAttributes is limited to reference types, which can be null without closing TIA.
        private static List<KeyValuePair<string, object>> ReadAttributes(IEngineeringObject engineering)
        {
            var result = new List<KeyValuePair<string, object>>();
            bool noted = false;
            IEnumerable<EngineeringAttributeInfo> infos;
            try
            {
                infos = engineering.GetAttributeInfos();
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw;
                }

                NoteSkippedAttributes(engineering, ex, ref noted);
                return result;
            }

            if (infos == null)
            {
                return result;
            }

            var names = new List<string>();
            try
            {
                foreach (EngineeringAttributeInfo info in infos)
                {
                    if (info == null || string.IsNullOrWhiteSpace(info.Name) || SkipName(info.Name) || !SurvivesNull(info) || IsCollectionOnly(info))
                    {
                        continue;
                    }

                    if (!MatchesAccess(info.AccessMode, AttributeAccessOptions.ReadWrite)
                        && !MatchesAccess(info.AccessMode, AttributeAccessOptions.ReadOnly))
                    {
                        continue;
                    }

                    names.Add(info.Name);
                }
            }
            catch (Exception ex)
            {
                if (IsSessionDead(ex))
                {
                    throw;
                }

                NoteSkippedAttributes(engineering, ex, ref noted);
                return result;
            }

            if (names.Count == 0)
            {
                return result;
            }

            try
            {
                IList<object> values = engineering.GetAttributes(names.ToArray());
                if (values != null)
                {
                    int count = Math.Min(names.Count, values.Count);
                    for (int index = 0; index < count; index++)
                    {
                        result.Add(new KeyValuePair<string, object>(names[index], values[index]));
                    }

                    return result;
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            foreach (string name in names)
            {
                try
                {
                    result.Add(new KeyValuePair<string, object>(name, engineering.GetAttribute(name)));
                }
                catch (Exception ex)
                {
                    if (IsSessionDead(ex))
                    {
                        throw;
                    }

                    NoteSkippedAttributes(engineering, ex, ref noted);
                }
            }

            return result;
        }

        private static void NoteSkippedAttributes(IEngineeringObject engineering, Exception ex, ref bool noted)
        {
            ExportIssues.Remember(ex);
            if (noted)
            {
                return;
            }

            noted = true;
            Exception root = ex.GetBaseException();
            HmiExportTrace.Info(
                "Skipped attributes on " + engineering.GetType().Name + ". " + (root.Message ?? ex.Message));
        }

        private static bool SkipName(string name)
        {
            return name.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Items", StringComparison.OrdinalIgnoreCase)
                || name.Equals("DeviceItems", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Parameters", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ReadParameters", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Telegrams", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCollectionOnly(EngineeringAttributeInfo info)
        {
            if (info.SupportedTypes == null)
            {
                return false;
            }

            bool any = false;
            foreach (Type type in info.SupportedTypes)
            {
                if (type == null)
                {
                    continue;
                }

                any = true;
                if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
                {
                    return false;
                }
            }

            return any;
        }

        private static bool MatchesAccess(EngineeringAttributeAccessMode mode, AttributeAccessOptions access)
        {
            if (access == AttributeAccessOptions.ReadWrite)
            {
                return mode == EngineeringAttributeAccessMode.ReadWrite;
            }

            return access == AttributeAccessOptions.ReadOnly && mode == EngineeringAttributeAccessMode.Read;
        }

        private static bool SurvivesNull(EngineeringAttributeInfo info)
        {
            if (info.SupportedTypes == null)
            {
                return false;
            }

            bool any = false;
            foreach (Type type in info.SupportedTypes)
            {
                if (type == null || type.IsValueType)
                {
                    return false;
                }

                any = true;
            }

            return any;
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

        private static string DriveNumber(DriveObject drive, string module)
        {
            try
            {
                return drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                string place = string.IsNullOrWhiteSpace(module) ? "Drive" : module;
                ExportIssues.Remember(place, ex);
                HmiExportTrace.Info("Drive on " + place + " has no object number. " + ex.GetBaseException().Message);
                return string.Empty;
            }
        }

        private static string DriveLabel(DriveObject drive, string fallback, string number)
        {
            string name = TryGet(drive, "Name");
            if (name.Length > 0)
            {
                return name;
            }

            if (!string.IsNullOrEmpty(number))
            {
                return "DO" + number;
            }

            try
            {
                return "DO" + drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return string.IsNullOrWhiteSpace(fallback) ? "Drive" : fallback;
            }
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
            foreach (ProjectBase project in OpenProjects(portal))
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in ListDevices(project, ct))
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
            }

            return null;
        }

        private static DriveObject MatchDrive(IEnumerable<DriveObject> drives, string moduleName, string number)
        {
            DriveObject only = null;
            int seen = 0;
            DriveObject missing = null;
            foreach (DriveObject drive in drives)
            {
                if (drive == null)
                {
                    continue;
                }

                seen++;
                only = drive;
                string driveNumber;
                try
                {
                    driveNumber = drive.DriveObjectNumber.ToString(CultureInfo.InvariantCulture);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    ExportIssues.Remember(moduleName, ex);
                    if (string.IsNullOrEmpty(number) && missing == null)
                    {
                        missing = drive;
                    }

                    continue;
                }

                if (!string.IsNullOrEmpty(number) && string.Equals(driveNumber, number, StringComparison.Ordinal))
                {
                    return drive;
                }
            }

            if (!string.IsNullOrEmpty(number))
            {
                return null;
            }

            if (missing != null)
            {
                return missing;
            }

            return seen == 1 ? only : null;
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
                            DriveObject match = MatchDrive(container.DriveObjects, moduleName, number);
                            if (match != null)
                            {
                                return match;
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
                    foreach (ProjectBase project in OpenProjects(portal))
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
            foreach (ProjectBase project in OpenProjects(portal))
            {
                ct.ThrowIfCancellationRequested();
                foreach (Device device in ListDevices(project, ct))
                {
                    if (device != null && string.Equals(device.Name ?? string.Empty, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return device;
                    }
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
            return ExportIssues.MustStop(ex);
        }
    }
}
