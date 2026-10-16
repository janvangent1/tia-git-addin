using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace TiaGitAddIn.Installer
{
    internal static class Program
    {
        private const string GitDownloadUrl = "https://git-scm.com/download/win";
        private const string UserAddInsName = "UserAddIns";
        private const string SystemAddInsName = "SystemAddIns";
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TGADDIN1");
        private static readonly Regex VersionPattern = new Regex(
            "<version>\\s*([^<\\s]+)\\s*</version>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Payload payload = ReadPayload(Application.ExecutablePath);
                payload.ProductVersion = ReadProductVersion(payload.Bytes);
                using (InstallForm form = new InstallForm(payload))
                {
                    form.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not install the add-in." + Environment.NewLine + Environment.NewLine + ex.Message +
                    Environment.NewLine + Environment.NewLine + "Close TIA Portal if the add-in file is in use, then run this installer again.",
                    "TIA Git Add-In",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        internal static string DefaultInstallFolder(int portalVersion)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Siemens",
                "Automation",
                PortalFolderName(portalVersion),
                UserAddInsName);
        }

        internal static string PortalFolderName(int portalVersion)
        {
            return "Portal V" + portalVersion.ToString();
        }

        internal static FolderAssessment AssessFolder(string path, int portalVersion)
        {
            string portalName = PortalFolderName(portalVersion);
            if (string.IsNullOrWhiteSpace(path))
            {
                return FolderAssessment.Reject("Choose the " + UserAddInsName + " folder inside " + portalName + ".");
            }

            string full;
            try
            {
                full = Path.GetFullPath(path.Trim());
            }
            catch (Exception)
            {
                return FolderAssessment.Reject("That folder path is not valid.");
            }

            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(full);
            if (name.Equals(SystemAddInsName, StringComparison.OrdinalIgnoreCase))
            {
                return FolderAssessment.Reject(
                    SystemAddInsName + " is not used. Choose the " + UserAddInsName + " folder inside " + portalName + ".");
            }

            if (name.Equals(portalName, StringComparison.OrdinalIgnoreCase))
            {
                full = Path.Combine(full, UserAddInsName);
                name = UserAddInsName;
            }

            if (!name.Equals(UserAddInsName, StringComparison.OrdinalIgnoreCase))
            {
                return FolderAssessment.Reject("Choose the " + UserAddInsName + " folder inside " + portalName + ".");
            }

            string parent = Path.GetDirectoryName(full);
            string parentName = string.IsNullOrEmpty(parent) ? string.Empty : Path.GetFileName(parent);
            if (!parentName.Equals(portalName, StringComparison.OrdinalIgnoreCase))
            {
                return FolderAssessment.Reject("This " + UserAddInsName + " folder is not inside " + portalName + ".");
            }

            bool folderExists = Directory.Exists(full);
            bool parentExists = Directory.Exists(parent);
            bool standardFolder = string.Equals(full, DefaultInstallFolder(portalVersion), StringComparison.OrdinalIgnoreCase);
            if (!folderExists && !parentExists && !standardFolder)
            {
                return FolderAssessment.Reject(MissingPortalMessage(portalVersion));
            }

            if (folderExists)
            {
                return FolderAssessment.Accept(full, "This is the TIA Portal V" + portalVersion.ToString() + " user add-ins folder.", false);
            }

            if (parentExists)
            {
                return FolderAssessment.Accept(
                    full,
                    portalName + " was found. " + UserAddInsName + " will be created. Browse if this is not the right folder.",
                    true);
            }

            return FolderAssessment.Accept(
                full,
                "This folder was not found. Install will create it. Browse if TIA Portal V" + portalVersion.ToString() + " keeps add-ins somewhere else.",
                true);
        }

        internal static string ReadProductVersion(byte[] addinBytes)
        {
            if (addinBytes == null || addinBytes.Length < 4)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(addinBytes, false))
                using (ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (!entry.FullName.EndsWith(".psmdcp", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        using (Stream entryStream = entry.Open())
                        using (StreamReader reader = new StreamReader(entryStream, Encoding.UTF8))
                        {
                            Match match = VersionPattern.Match(reader.ReadToEnd());
                            if (match.Success)
                            {
                                return match.Groups[1].Value;
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        internal static string DescribeInstalledCopy(string folder, string fileName, string packageVersion)
        {
            string installedVersion = ReadInstalledVersion(folder, fileName);
            if (installedVersion == null)
            {
                return "No add-in is installed in this folder yet.";
            }

            if (installedVersion.Length == 0)
            {
                return "An add-in is already in this folder. Close TIA Portal if the file is in use, or its version could not be read.";
            }

            if (!string.IsNullOrEmpty(packageVersion) &&
                installedVersion.Equals(packageVersion, StringComparison.OrdinalIgnoreCase))
            {
                return "Version " + installedVersion + " is already installed in this folder. Installing again replaces that copy.";
            }

            if (!string.IsNullOrEmpty(packageVersion))
            {
                return "Already installed in this folder: version " + installedVersion +
                    ". This installer will replace it with version " + packageVersion + ".";
            }

            return "Already installed in this folder: version " + installedVersion + ".";
        }

        private static string ReadInstalledVersion(string folder, string fileName)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(fileName))
            {
                return null;
            }

            string existing = Path.Combine(folder, fileName);
            if (!File.Exists(existing))
            {
                return null;
            }

            try
            {
                string version = ReadProductVersion(File.ReadAllBytes(existing));
                return string.IsNullOrEmpty(version) ? string.Empty : version;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        internal static void InstallTo(Payload payload, string folder)
        {
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(folder, payload.FileName);
            string tempPath = destination + ".installing";
            try
            {
                File.WriteAllBytes(tempPath, payload.Bytes);
                if (File.Exists(destination))
                {
                    string backupPath = destination + ".bak";
                    File.Replace(tempPath, destination, backupPath, true);
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
                else
                {
                    File.Move(tempPath, destination);
                }

                tempPath = null;
            }
            finally
            {
                if (tempPath != null)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }

        internal static string SuccessMessage(Payload payload, string destination)
        {
            string versionLine = string.IsNullOrEmpty(payload.ProductVersion)
                ? "Installed " + payload.FileName + "."
                : "Installed TIA Git Add-In version " + payload.ProductVersion + ".";
            return versionLine + Environment.NewLine + Environment.NewLine +
                destination + Environment.NewLine + Environment.NewLine +
                EnableInstructions(payload.PortalVersion);
        }

        internal static string EnableInstructions(int portalVersion)
        {
            return "To enable it in TIA Portal:" + Environment.NewLine +
                "1. Close TIA Portal if it is open, then start TIA Portal V" + portalVersion.ToString() + " again." + Environment.NewLine +
                "2. Open the Add-ins task card." + Environment.NewLine +
                "3. Enable TIA Git Add-In." + Environment.NewLine +
                "4. Right-click a VCI workspace item and choose Open Git Panel...";
        }

        private static string MissingPortalMessage(int portalVersion)
        {
            string portalName = PortalFolderName(portalVersion);
            string automation = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Siemens",
                "Automation");
            string found = string.Empty;
            try
            {
                if (Directory.Exists(automation))
                {
                    string[] portals = Directory.GetDirectories(automation, "Portal V*");
                    if (portals.Length > 0)
                    {
                        string[] names = new string[portals.Length];
                        for (int i = 0; i < portals.Length; i++)
                        {
                            names[i] = Path.GetFileName(portals[i]);
                        }

                        found = " This PC has " + string.Join(", ", names) + ".";
                    }
                }
            }
            catch (Exception)
            {
            }

            return "TIA Portal V" + portalVersion.ToString() + " was not found in the usual user folder." + found +
                " Browse to " + portalName + "\\" + UserAddInsName + ".";
        }

        private static void PromptToInstallGit(string installedMessage)
        {
            DialogResult answer = MessageBox.Show(
                installedMessage + Environment.NewLine + Environment.NewLine +
                "Git for Windows was not found. The add-in needs Git to work." + Environment.NewLine + Environment.NewLine +
                "Download Git for Windows now?",
                "TIA Git Add-In",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = GitDownloadUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open the download page." + Environment.NewLine + Environment.NewLine +
                    GitDownloadUrl + Environment.NewLine + Environment.NewLine + ex.Message,
                    "TIA Git Add-In",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static string FindGit()
        {
            string fromPath = FindGitOnPath(Environment.GetEnvironmentVariable("PATH"));
            if (fromPath != null)
            {
                return fromPath;
            }

            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] candidates = new string[]
            {
                Path.Combine(programFiles, "Git", "cmd", "git.exe"),
                Path.Combine(programFiles, "Git", "bin", "git.exe"),
                Path.Combine(programFilesX86, "Git", "cmd", "git.exe"),
                Path.Combine(localAppData, "Programs", "Git", "cmd", "git.exe")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                if (FileExists(candidates[i]))
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static string FindGitOnPath(string pathVariable)
        {
            if (pathVariable == null || pathVariable.Trim().Length == 0)
            {
                return null;
            }

            string[] entries = pathVariable.Split(Path.PathSeparator);
            for (int i = 0; i < entries.Length; i++)
            {
                string directory = entries[i].Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                string candidate = Path.Combine(directory, "git.exe");
                if (FileExists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool FileExists(string path)
        {
            try
            {
                return !string.IsNullOrEmpty(path) && File.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static Payload ReadPayload(string exePath)
        {
            using (FileStream stream = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length < 20)
                {
                    throw new InvalidOperationException("This installer does not contain a plugin package.");
                }

                long end = stream.Length;
                byte[] magic = ReadAt(stream, end - 8, 8);
                if (!SameBytes(magic, Magic))
                {
                    throw new InvalidOperationException("This installer does not contain a plugin package.");
                }

                ushort nameLength = BitConverter.ToUInt16(ReadAt(stream, end - 20, 2), 0);
                ushort portalVersion = BitConverter.ToUInt16(ReadAt(stream, end - 18, 2), 0);
                ulong payloadLength = BitConverter.ToUInt64(ReadAt(stream, end - 16, 8), 0);
                if (nameLength == 0 || portalVersion < 1 || portalVersion > 99)
                {
                    throw new InvalidOperationException("The embedded plugin details are not valid.");
                }

                if (payloadLength == 0 || payloadLength > 512L * 1024L * 1024L)
                {
                    throw new InvalidOperationException("The embedded plugin package is not valid.");
                }

                long nameStart = end - 20 - nameLength;
                long payloadStart = nameStart - (long)payloadLength;
                if (payloadStart < 0)
                {
                    throw new InvalidOperationException("The embedded plugin package is not valid.");
                }

                string fileName = Encoding.UTF8.GetString(ReadAt(stream, nameStart, nameLength));
                fileName = Path.GetFileName(fileName);
                if (string.IsNullOrEmpty(fileName) ||
                    fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    !fileName.EndsWith(".addin", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The embedded plugin file name is not valid.");
                }

                return new Payload
                {
                    Bytes = ReadAt(stream, payloadStart, (int)payloadLength),
                    FileName = fileName,
                    PortalVersion = portalVersion
                };
            }
        }

        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int got = stream.Read(buffer, read, count - read);
                if (got == 0)
                {
                    throw new EndOfStreamException("The installer file ended before the plugin package.");
                }

                read += got;
            }

            return buffer;
        }

        private static bool SameBytes(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static string ExistingAncestor(string path)
        {
            try
            {
                string current = path;
                while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
                {
                    current = Path.GetDirectoryName(current);
                }

                return current;
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal sealed class Payload
        {
            public byte[] Bytes;
            public string FileName;
            public int PortalVersion;
            public string ProductVersion;
        }

        internal sealed class FolderAssessment
        {
            public bool CanInstall;
            public string Folder;
            public string Message;

            public bool NeedsCreate;

            public static FolderAssessment Accept(string folder, string message, bool needsCreate)
            {
                return new FolderAssessment
                {
                    CanInstall = true,
                    Folder = folder,
                    Message = message,
                    NeedsCreate = needsCreate
                };
            }

            public static FolderAssessment Reject(string message)
            {
                return new FolderAssessment
                {
                    CanInstall = false,
                    Message = message
                };
            }
        }

        private sealed class InstallForm : Form
        {
            private readonly Payload payload;
            private readonly TextBox folderBox;
            private readonly Label installedVersion;
            private readonly Label folderStatus;
            private readonly Button installButton;
            private FolderAssessment assessment;

            public InstallForm(Payload payload)
            {
                this.payload = payload;
                Text = "TIA Git Add-In";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(640, 292);
                Font = new Font("Segoe UI", 9f);

                Label title = new Label
                {
                    Text = "Install TIA Git Add-In",
                    Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                    AutoSize = true,
                    Location = new Point(16, 14)
                };
                string packageVersion = string.IsNullOrEmpty(payload.ProductVersion)
                    ? "unknown"
                    : payload.ProductVersion;
                Label version = new Label
                {
                    Text = "Version in this installer: " + packageVersion,
                    AutoSize = true,
                    Location = new Point(16, 48)
                };
                installedVersion = new Label
                {
                    AutoSize = false,
                    Location = new Point(16, 70),
                    Size = new Size(608, 36)
                };
                Label folderLabel = new Label
                {
                    Text = "TIA Portal V" + payload.PortalVersion.ToString() + " user add-ins folder",
                    AutoSize = true,
                    Location = new Point(16, 112)
                };
                folderBox = new TextBox
                {
                    Location = new Point(16, 134),
                    Size = new Size(508, 23),
                    ReadOnly = true,
                    Text = DefaultInstallFolder(payload.PortalVersion)
                };
                Button browse = new Button
                {
                    Text = "Browse...",
                    Location = new Point(532, 132),
                    Size = new Size(92, 26)
                };
                browse.Click += BrowseClick;
                folderStatus = new Label
                {
                    AutoSize = false,
                    Location = new Point(16, 166),
                    Size = new Size(608, 52)
                };
                installButton = new Button
                {
                    Text = "Install",
                    Location = new Point(436, 244),
                    Size = new Size(88, 28)
                };
                installButton.Click += InstallClick;
                Button cancel = new Button
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(532, 244),
                    Size = new Size(92, 28)
                };
                AcceptButton = installButton;
                CancelButton = cancel;
                Controls.Add(title);
                Controls.Add(version);
                Controls.Add(installedVersion);
                Controls.Add(folderLabel);
                Controls.Add(folderBox);
                Controls.Add(browse);
                Controls.Add(folderStatus);
                Controls.Add(installButton);
                Controls.Add(cancel);
                folderBox.TextChanged += delegate { RefreshFolder(); };
                RefreshFolder();
            }

            private void BrowseClick(object sender, EventArgs e)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "Select the " + PortalFolderName(payload.PortalVersion) + "\\" + UserAddInsName + " folder.";
                    dialog.ShowNewFolderButton = false;
                    string start = ExistingAncestor(assessment != null && !string.IsNullOrEmpty(assessment.Folder)
                        ? assessment.Folder
                        : folderBox.Text);
                    if (!string.IsNullOrEmpty(start))
                    {
                        dialog.SelectedPath = start;
                    }

                    if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
                    {
                        folderBox.Text = dialog.SelectedPath;
                    }
                }
            }

            private void RefreshFolder()
            {
                assessment = AssessFolder(folderBox.Text, payload.PortalVersion);
                if (assessment.CanInstall &&
                    !string.Equals(folderBox.Text, assessment.Folder, StringComparison.OrdinalIgnoreCase))
                {
                    folderBox.Text = assessment.Folder;
                    return;
                }

                folderStatus.Text = assessment.Message;
                if (!assessment.CanInstall)
                {
                    folderStatus.ForeColor = Color.FromArgb(164, 38, 44);
                }
                else if (assessment.NeedsCreate)
                {
                    folderStatus.ForeColor = Color.FromArgb(140, 90, 0);
                }
                else
                {
                    folderStatus.ForeColor = Color.FromArgb(0, 114, 104);
                }
                installButton.Enabled = assessment.CanInstall;
                installedVersion.Text = assessment.CanInstall
                    ? DescribeInstalledCopy(assessment.Folder, payload.FileName, payload.ProductVersion)
                    : "Install stays unavailable until the folder is the TIA Portal V" +
                        payload.PortalVersion.ToString() + " " + UserAddInsName + " folder.";
            }

            private void InstallClick(object sender, EventArgs e)
            {
                FolderAssessment current = AssessFolder(folderBox.Text, payload.PortalVersion);
                if (!current.CanInstall)
                {
                    MessageBox.Show(this, current.Message, "TIA Git Add-In", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    RefreshFolder();
                    return;
                }

                string destination = Path.Combine(current.Folder, payload.FileName);
                try
                {
                    InstallTo(payload, current.Folder);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "Could not install the add-in." + Environment.NewLine + Environment.NewLine + ex.Message +
                        Environment.NewLine + Environment.NewLine + "Close TIA Portal if the add-in file is in use, then run this installer again.",
                        "TIA Git Add-In",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                string message = SuccessMessage(payload, destination);
                string caption = string.IsNullOrEmpty(payload.ProductVersion)
                    ? "TIA Git Add-In"
                    : "TIA Git Add-In " + payload.ProductVersion;
                if (FindGit() == null)
                {
                    PromptToInstallGit(message);
                }
                else
                {
                    MessageBox.Show(this, message, caption, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
