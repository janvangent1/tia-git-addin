using System.Text;
using TiaGitAddIn.Models;
using TiaGitAddIn.Reporting;
using Xunit;

namespace TiaGitAddIn.IntegrationTests.Reporting;

public sealed class PdfReportTests
{
    [Fact]
    public void HistoryPdfContainsTheCommitAndSelectedFiles()
    {
        var selected = new CommitInfo
        {
            Hash = "abcdef1234567890",
            AuthorName = "Ada",
            AuthorDate = new DateTimeOffset(2026, 3, 1, 8, 30, 0, TimeSpan.Zero),
            Subject = "Add motor interlock"
        };
        PdfReport report = HistoryPdfReport.Create(
            @"C:\workspace",
            new[] { selected },
            selected,
            new[] { "Blocks/Motor.scl" });

        string pdf = Write(report);

        Assert.StartsWith("%PDF", pdf, StringComparison.Ordinal);
        Assert.StartsWith("1 0 obj", pdf.Substring(FirstObjectOffset(pdf)), StringComparison.Ordinal);
        Assert.Contains("(Add motor interlock)", pdf, StringComparison.Ordinal);
        Assert.Contains("(Blocks/Motor.scl)", pdf, StringComparison.Ordinal);
        Assert.Contains("%%EOF", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void TextDiffPdfKeepsAddedAndRemovedLines()
    {
        PdfReport report = DiffPdfReport.Create(
            "Working tree",
            new[] { "M  Blocks/Motor.scl" },
            "Blocks/Motor.scl",
            ladderOmitted: false,
            new[]
            {
                new DiffPdfTextLine("+", "NETWORK"),
                new DiffPdfTextLine("\u2212", "old line")
            },
            textTruncated: true);

        string pdf = Write(report);

        Assert.Contains("(+ NETWORK)", pdf, StringComparison.Ordinal);
        Assert.Contains("(- old line)", pdf, StringComparison.Ordinal);
        Assert.Contains("(The text diff was truncated.)", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("/XObject", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void LadderDiffIsOmittedFromThePdf()
    {
        PdfReport report = DiffPdfReport.Create(
            "Commit abcdef1",
            new[] { "M  Blocks/Motor.lad" },
            "Blocks/Motor.lad",
            ladderOmitted: true,
            textLines: null,
            textTruncated: false);

        string pdf = Write(report);

        Assert.Contains("(Ladder diagrams are not included in this PDF.)", pdf, StringComparison.Ordinal);
        Assert.Contains("(M  Blocks/Motor.lad)", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("/XObject", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void LongReportUsesMoreThanOnePage()
    {
        var lines = new List<PdfLine>();
        for (int index = 0; index < 100; index++)
        {
            lines.Add(new PdfLine("line " + index.ToString()));
        }

        string pdf = Write(new PdfReport("Long", lines));
        int pageCount = Count(pdf, "/Type /Page /Parent");

        Assert.Equal(2, pageCount);
    }

    private static string Write(PdfReport report)
    {
        using var stream = new MemoryStream();
        PdfReportWriter.Write(report, stream);
        return Encoding.ASCII.GetString(stream.ToArray());
    }

    private static int FirstObjectOffset(string pdf)
    {
        int xref = pdf.IndexOf("\nxref\n", StringComparison.Ordinal);
        string entry = pdf.Substring(xref + 1).Split('\n')[3];
        return int.Parse(entry.Substring(0, 10), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int Count(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
