using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Siemens export XML stores a new creation time on every write. A file that
    /// already has one keeps that same time. A file that has none loses the new one.
    /// </summary>
    public static class HmiExportNormalizer
    {
        private static readonly Regex DocumentInfoPattern = new Regex(
            @"(<(?:[\w.]+:)?DocumentInfo\b[^>]*>)(.*?)(</(?:[\w.]+:)?DocumentInfo>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CreatedElementPattern = new Regex(
            @"[ \t]*<(?:[\w.]+:)?Created\b[^>]*>.*?</(?:[\w.]+:)?Created>[ \t]*(?:\r?\n)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CreatedValuePattern = new Regex(
            @"<(?:[\w.]+:)?Created\b[^>]*>\s*(.*?)\s*</(?:[\w.]+:)?Created>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static IReadOnlyDictionary<string, string> CaptureCreationTimestamps(string directory)
        {
            var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return captured;
            }

            string root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
            {
                string? stamp = ReadCreationTimestamp(ReadXml(file));
                if (string.IsNullOrEmpty(stamp))
                {
                    continue;
                }

                string relative = Path.GetFullPath(file)
                    .Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                captured[relative] = stamp!;
            }

            return captured;
        }

        public static void StabilizeCreationTimestamps(string directory, IReadOnlyDictionary<string, string> previous)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            string root = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            foreach (string file in Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories))
            {
                string relative = Path.GetFullPath(file)
                    .Substring(root.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                string? previousStamp = null;
                previous?.TryGetValue(relative, out previousStamp);
                RewriteCreationTimestamp(file, previousStamp);
            }
        }

        public static void StripCreationTimestamps(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories))
            {
                StripFile(file);
            }
        }

        public static string RemoveCreationTimestamps(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("DocumentInfo", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return xml ?? string.Empty;
            }

            return DocumentInfoPattern.Replace(xml, match =>
            {
                string body = CreatedElementPattern.Replace(match.Groups[2].Value, string.Empty);
                return match.Groups[1].Value + body + match.Groups[3].Value;
            });
        }

        public static string KeepCreationTimestamp(string xml, string previousTimestamp)
        {
            if (string.IsNullOrEmpty(previousTimestamp))
            {
                return RemoveCreationTimestamps(xml);
            }

            if (string.IsNullOrEmpty(xml) || xml.IndexOf("DocumentInfo", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return xml ?? string.Empty;
            }

            return DocumentInfoPattern.Replace(xml, match =>
            {
                string body = match.Groups[2].Value;
                Match created = CreatedValuePattern.Match(body);
                if (created.Success)
                {
                    body = body.Remove(created.Groups[1].Index, created.Groups[1].Length)
                        .Insert(created.Groups[1].Index, previousTimestamp);
                }
                else
                {
                    string newline = body.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
                    body = newline + "    <Created>" + previousTimestamp + "</Created>" + body;
                }

                return match.Groups[1].Value + body + match.Groups[3].Value;
            });
        }

        private static string? ReadCreationTimestamp(string xml)
        {
            if (string.IsNullOrEmpty(xml) || xml.IndexOf("DocumentInfo", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            Match document = DocumentInfoPattern.Match(xml);
            if (!document.Success)
            {
                return null;
            }

            Match created = CreatedValuePattern.Match(document.Groups[2].Value);
            return created.Success ? created.Groups[1].Value : null;
        }

        private static void RewriteCreationTimestamp(string path, string? previousTimestamp)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = DetectEncoding(bytes, out int preambleLength);
            string xml = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            string updated = string.IsNullOrEmpty(previousTimestamp)
                ? RemoveCreationTimestamps(xml)
                : KeepCreationTimestamp(xml, previousTimestamp!);
            if (string.Equals(updated, xml, StringComparison.Ordinal))
            {
                return;
            }

            WriteXml(path, bytes, preambleLength, encoding, updated);
        }

        private static string ReadXml(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = DetectEncoding(bytes, out int preambleLength);
            return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        }

        private static void StripFile(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Encoding encoding = DetectEncoding(bytes, out int preambleLength);
            string xml = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
            string updated = RemoveCreationTimestamps(xml);
            if (string.Equals(updated, xml, StringComparison.Ordinal))
            {
                return;
            }

            WriteXml(path, bytes, preambleLength, encoding, updated);
        }

        private static void WriteXml(string path, byte[] original, int preambleLength, Encoding encoding, string xml)
        {
            byte[] body = encoding.GetBytes(xml);
            if (preambleLength == 0)
            {
                File.WriteAllBytes(path, body);
                return;
            }

            byte[] combined = new byte[preambleLength + body.Length];
            Buffer.BlockCopy(original, 0, combined, 0, preambleLength);
            Buffer.BlockCopy(body, 0, combined, preambleLength, body.Length);
            File.WriteAllBytes(path, combined);
        }

        private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            {
                preambleLength = 3;
                return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            {
                preambleLength = 2;
                return new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
            }

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            {
                preambleLength = 2;
                return new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
            }

            preambleLength = 0;
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }
}
