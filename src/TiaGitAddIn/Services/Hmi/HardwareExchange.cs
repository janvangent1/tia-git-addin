using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Xml;
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

        public static OperationResult Export(TiaPortal portal, string repositoryPath, CancellationToken ct, Action<string> report)
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

            int devices = 0;
            int modules = 0;
            int networks = 0;
            int drives = 0;
            int parameters = 0;
            int telegrams = 0;
            var seenDrives = new List<DriveObject>();
            var usedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (Project project in portal.Projects)
            {
                ct.ThrowIfCancellationRequested();
                networks += WriteSubnets(project, root, ct, report);

                foreach (Device device in project.Devices)
                {
                    ExportDevice(device, root, usedFolders, seenDrives, ct, report, ref devices, ref modules, ref drives, ref parameters, ref telegrams);
                }

                foreach (DeviceUserGroup group in project.DeviceGroups)
                {
                    ExportGroup(group, root, usedFolders, seenDrives, ct, report, ref devices, ref modules, ref drives, ref parameters, ref telegrams);
                }
            }

            Finish(report, "Devices", devices);
            Finish(report, "Modules", modules);
            Finish(report, "Networks", networks);
            Finish(report, "Drives", drives);
            Finish(report, "Parameters", parameters);
            Finish(report, "Telegrams", telegrams);

            if (devices == 0)
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

        private static void ExportGroup(
            DeviceUserGroup group,
            string root,
            ISet<string> usedFolders,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int devices,
            ref int modules,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            foreach (Device device in group.Devices)
            {
                ExportDevice(device, root, usedFolders, seenDrives, ct, report, ref devices, ref modules, ref drives, ref parameters, ref telegrams);
            }

            foreach (DeviceUserGroup child in group.Groups)
            {
                ExportGroup(child, root, usedFolders, seenDrives, ct, report, ref devices, ref modules, ref drives, ref parameters, ref telegrams);
            }
        }

        private static void ExportDevice(
            Device device,
            string root,
            ISet<string> usedFolders,
            List<DriveObject> seenDrives,
            CancellationToken ct,
            Action<string> report,
            ref int devices,
            ref int modules,
            ref int drives,
            ref int parameters,
            ref int telegrams)
        {
            ct.ThrowIfCancellationRequested();
            devices++;
            Step(report, "Devices", devices, device.Name);
            string folderName = HmiExportStore.UniqueName(HmiExportStore.FolderName(device.Name), usedFolders);
            string folder = Path.Combine(root, folderName);
            Directory.CreateDirectory(folder);

            var module = new XElement(
                "Device",
                new XAttribute("name", device.Name ?? string.Empty),
                new XAttribute("type", device.TypeIdentifier ?? string.Empty));
            AppendAttributes(module, device);
            var usedSettings = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DeviceItem item in device.DeviceItems)
            {
                module.Add(WriteItem(
                    item,
                    device.Name ?? string.Empty,
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

            new XDocument(module).Save(Path.Combine(folder, "Hardware.xml"));
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
            WriteItemDrives(item, deviceName, folder, usedDrives, seenDrives, ct, report, ref drives, ref parameters, ref telegrams);
            CollectSettings(item, folder, usedSettings);

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
                drives++;
                string label = DriveLabel(drive);
                Step(report, "Drives", drives, label);
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
                WriteAttributeElements(writer, drive);
                parameters += WriteParameters(writer, drive, ct, report, parameters);
                telegrams += WriteTelegrams(writer, drive, report, telegrams);
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
                AppendNamed(element, "Node", subnet.Nodes);
                AppendNamed(element, "IoSystem", subnet.IoSystems);
                document.Add(element);
            }

            if (count > 0)
            {
                string folder = Path.Combine(root, "Networks");
                Directory.CreateDirectory(folder);
                new XDocument(document).Save(Path.Combine(folder, "Subnets.xml"));
            }

            return count;
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
        }

        private static void Step(Action<string> report, string category, int count, string detail)
        {
            report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "running", count, detail ?? string.Empty));
        }

        private static void Finish(Action<string> report, string category, int count)
        {
            report?.Invoke(HmiExportProgress.Format(ChecklistDevice, category, "done", count, string.Empty));
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
