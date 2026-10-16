using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Siemens export XML stores a new creation time on every write. Removing that
    /// element keeps an unchanged HMI object out of the Git diff.
    /// </summary>
    public static class HmiExportNormalizer
    {
        private static readonly Regex DocumentInfoPattern = new Regex(
            @"(<(?:[\w.]+:)?DocumentInfo\b[^>]*>)(.*?)(</(?:[\w.]+:)?DocumentInfo>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex CreatedElementPattern = new Regex(
            @"[ \t]*<(?:[\w.]+:)?Created>\s*\d{4}-\d{2}-\d{2}T[^<]*</(?:[\w.]+:)?Created>[ \t]*(?:\r?\n)?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

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

            byte[] body = encoding.GetBytes(updated);
            if (preambleLength == 0)
            {
                File.WriteAllBytes(path, body);
                return;
            }

            byte[] combined = new byte[preambleLength + body.Length];
            Buffer.BlockCopy(bytes, 0, combined, 0, preambleLength);
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
