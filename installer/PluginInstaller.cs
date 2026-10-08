using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace TiaGitAddIn.Installer
{
    internal static class Program
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TGADDIN1");

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string tempPath = null;
            try
            {
                Payload payload = ReadPayload(Application.ExecutablePath);
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Siemens",
                    "Automation",
                    "Portal V" + payload.PortalVersion.ToString(),
                    "UserAddIns");
                Directory.CreateDirectory(folder);

                string destination = Path.Combine(folder, payload.FileName);
                tempPath = destination + ".installing";
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
                MessageBox.Show(
                    "Installed " + payload.FileName + " to:" + Environment.NewLine + Environment.NewLine +
                    destination + Environment.NewLine + Environment.NewLine +
                    "Restart TIA Portal, then enable the add-in in the Add-ins task card.",
                    "TIA Git Add-In",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
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

                MessageBox.Show(
                    "Could not install the add-in." + Environment.NewLine + Environment.NewLine + ex.Message +
                    Environment.NewLine + Environment.NewLine + "Close TIA Portal if the add-in file is in use, then run this installer again.",
                    "TIA Git Add-In",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
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

        private sealed class Payload
        {
            public byte[] Bytes;
            public string FileName;
            public int PortalVersion;
        }
    }
}
