using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiaGitAddIn.Services.Hmi;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class HmiExportNormalizerTests
    {
        [Fact]
        public void RemoveCreationTimestampsDropsOnlyTheDocumentInfoCreatedElement()
        {
            const string xml =
                "<Document>\r\n" +
                "  <DocumentInfo>\r\n" +
                "    <Created>2026-10-16T17:39:07.6331138Z</Created>\r\n" +
                "    <ExportSetting>WithDefaults</ExportSetting>\r\n" +
                "  </DocumentInfo>\r\n" +
                "  <Created>keep this text</Created>\r\n" +
                "  <Body>screen</Body>\r\n" +
                "</Document>\r\n";

            string updated = HmiExportNormalizer.RemoveCreationTimestamps(xml);

            Assert.DoesNotContain("2026-10-16T17:39:07.6331138Z", updated);
            Assert.Contains("<ExportSetting>WithDefaults</ExportSetting>", updated);
            Assert.Contains("<Created>keep this text</Created>", updated);
            Assert.Contains("<Body>screen</Body>", updated);
        }

        [Fact]
        public void StripCreationTimestampsRewritesTheFileAndKeepsTheBom()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-created-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "Screen.xml");
                string xml =
                    "<Document>\n" +
                    "  <DocumentInfo>\n" +
                    "    <Created>2026-10-16T17:45:08.1233992Z</Created>\n" +
                    "    <ExportSetting>None</ExportSetting>\n" +
                    "  </DocumentInfo>\n" +
                    "</Document>\n";
                File.WriteAllBytes(path, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(xml)).ToArray());

                HmiExportNormalizer.StripCreationTimestamps(root);

                byte[] written = File.ReadAllBytes(path);
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written.Take(3));
                string text = Encoding.UTF8.GetString(written, 3, written.Length - 3);
                Assert.DoesNotContain("2026-10-16T17:45:08.1233992Z", text);
                Assert.Contains("<ExportSetting>None</ExportSetting>", text);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void StabilizeKeepsTheTimestampAlreadyStoredForThatFile()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-created-" + Path.GetRandomFileName());
            string slideins = Path.Combine(root, "Slideins");
            Directory.CreateDirectory(slideins);
            try
            {
                string path = Path.Combine(slideins, "Left.xml");
                File.WriteAllText(
                    path,
                    "<Document>\r\n  <DocumentInfo>\r\n    <Created>2026-10-16T17:45:39.5834035Z</Created>\r\n    <ExportSetting>WithDefaults</ExportSetting>\r\n  </DocumentInfo>\r\n</Document>\r\n");
                IReadOnlyDictionary<string, string> previous = HmiExportNormalizer.CaptureCreationTimestamps(root);

                File.WriteAllText(
                    path,
                    "<Document>\r\n  <DocumentInfo>\r\n    <Created>2026-10-17T21:48:02.0000000Z</Created>\r\n    <ExportSetting>WithDefaults</ExportSetting>\r\n  </DocumentInfo>\r\n</Document>\r\n");
                HmiExportNormalizer.StabilizeCreationTimestamps(root, previous);

                string updated = File.ReadAllText(path);
                Assert.Contains("<Created>2026-10-16T17:45:39.5834035Z</Created>", updated);
                Assert.DoesNotContain("2026-10-17T21:48:02.0000000Z", updated);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void StabilizeDropsTheTimestampWhenTheFileHadNone()
        {
            string root = Path.Combine(Path.GetTempPath(), "tia-git-created-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "Left.xml");
                File.WriteAllText(
                    path,
                    "<Document>\n  <DocumentInfo>\n    <Created>2026-10-17T21:48:02.0000000Z</Created>\n    <ExportSetting>WithDefaults</ExportSetting>\n  </DocumentInfo>\n</Document>\n");
                HmiExportNormalizer.StabilizeCreationTimestamps(root, new Dictionary<string, string>());

                string updated = File.ReadAllText(path);
                Assert.DoesNotContain("<Created>", updated);
                Assert.Contains("<ExportSetting>WithDefaults</ExportSetting>", updated);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}
