using System;
using System.Collections.Generic;

namespace TiaGitAddIn.Reporting
{
    public enum PdfLineStyle
    {
        Body,
        Heading,
        Added,
        Removed,
        Note
    }

    public sealed class PdfLine
    {
        public PdfLine(string text, PdfLineStyle style = PdfLineStyle.Body)
        {
            Text = text ?? string.Empty;
            Style = style;
        }

        public string Text { get; }
        public PdfLineStyle Style { get; }
    }

    public sealed class PdfReport
    {
        public PdfReport(string title, IReadOnlyList<PdfLine> lines)
        {
            Title = string.IsNullOrWhiteSpace(title) ? "TIA Git" : title;
            Lines = lines ?? Array.Empty<PdfLine>();
        }

        public string Title { get; }
        public IReadOnlyList<PdfLine> Lines { get; }
    }
}
