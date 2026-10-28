using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TiaGitAddIn.Models;
using TiaGitAddIn.Reporting;
using Xunit;

namespace TiaGitAddIn.Tests.Reporting
{
    public sealed class PdfReportWriterTests
    {
        [Fact]
        public void PdfKeepsGermanLettersAndNumbersThePages()
        {
            var lines = new List<PdfLine> { new PdfLine("l\u00e4uft") };
            for (int index = 0; index < 80; index++)
            {
                lines.Add(new PdfLine("line " + index.ToString()));
            }

            string pdf = Write(new PdfReport("Motor", lines));

            Assert.Contains("(l\\344uft)", pdf, StringComparison.Ordinal);
            Assert.Contains("/Encoding /WinAnsiEncoding", pdf, StringComparison.Ordinal);
            Assert.Contains("(Page 1 of ", pdf, StringComparison.Ordinal);
            Assert.Contains("%%EOF", pdf, StringComparison.Ordinal);
        }

        [Fact]
        public void HistoryPdfNestsABlockUnderItsSoftwareUnit()
        {
            var files = new Dictionary<string, IReadOnlyList<string>?>
            {
                ["abc"] = new[]
                {
                    "PLC/Software units/Unit_600_Motion/Program blocks/FB_Jog.xml",
                    "PLC/Program blocks/Main.xml"
                }
            };
            PdfReport report = HistoryPdfReport.Create(
                @"C:\workspace",
                new[] { new CommitInfo { Hash = "abc", Subject = "Motion" } },
                files);

            string pdf = Write(report);

            Assert.Contains("(PLC)", pdf, StringComparison.Ordinal);
            Assert.Contains("(  Software units)", pdf, StringComparison.Ordinal);
            Assert.Contains("(    Unit_600_Motion)", pdf, StringComparison.Ordinal);
            Assert.Contains("(        FB_Jog.xml)", pdf, StringComparison.Ordinal);
            Assert.Contains("(  Program blocks)", pdf, StringComparison.Ordinal);
            Assert.Contains("(    Main.xml)", pdf, StringComparison.Ordinal);
            Assert.Contains("(Page 1 of 2)", pdf, StringComparison.Ordinal);
            Assert.Contains("(Page 2 of 2)", pdf, StringComparison.Ordinal);
            int listAt = pdf.IndexOf("(abc  ", StringComparison.Ordinal);
            int filesAt = pdf.IndexOf("FB_Jog.xml)", StringComparison.Ordinal);
            Assert.True(listAt >= 0 && filesAt > listAt);
        }

        [Fact]
        public void ChangedLineIsPrintedInTheChangedColor()
        {
            PdfReport report = DiffPdfReport.Create(
                "Diff",
                new[] { "Blocks/Motor.scl" },
                "Blocks/Motor.scl",
                ladderOmitted: false,
                new[] { new DiffPdfTextLine("~", "StartButton [changed]") },
                textTruncated: false);

            string pdf = Write(report);

            Assert.Contains("(StartButton [changed])", pdf, StringComparison.Ordinal);
            Assert.Contains("0.45 0.32 0 rg", pdf, StringComparison.Ordinal);
            Assert.DoesNotContain("(~ ", pdf, StringComparison.Ordinal);
        }

        private static string Write(PdfReport report)
        {
            using (var stream = new MemoryStream())
            {
                PdfReportWriter.Write(report, stream);
                return Encoding.ASCII.GetString(stream.ToArray());
            }
        }
    }
}
