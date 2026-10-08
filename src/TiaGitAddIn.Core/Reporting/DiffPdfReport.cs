using System.Collections.Generic;

namespace TiaGitAddIn.Reporting
{
    public readonly struct DiffPdfTextLine
    {
        public DiffPdfTextLine(string marker, string text)
        {
            Marker = marker ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string Marker { get; }
        public string Text { get; }
    }

    public static class DiffPdfReport
    {
        public const string LadderOmittedNote =
            "Ladder diagrams are not included in this PDF.";

        public static PdfReport Create(
            string title,
            IReadOnlyList<string> changedFiles,
            string? selectedFile,
            bool ladderOmitted,
            IReadOnlyList<DiffPdfTextLine>? textLines,
            bool textTruncated,
            string? bodyNote = null)
        {
            var lines = new List<PdfLine>
            {
                new PdfLine(string.IsNullOrWhiteSpace(title) ? "Diff" : title, PdfLineStyle.Heading),
                new PdfLine("Changed files", PdfLineStyle.Heading)
            };

            if (changedFiles == null || changedFiles.Count == 0)
            {
                lines.Add(new PdfLine("No changed files.", PdfLineStyle.Note));
            }
            else
            {
                foreach (string file in changedFiles)
                {
                    lines.Add(new PdfLine(file ?? string.Empty));
                }
            }

            lines.Add(new PdfLine(string.Empty));
            if (selectedFile != null && selectedFile.Trim().Length > 0)
            {
                lines.Add(new PdfLine(selectedFile, PdfLineStyle.Heading));
            }

            if (ladderOmitted)
            {
                lines.Add(new PdfLine(LadderOmittedNote, PdfLineStyle.Note));
                return new PdfReport(title, lines);
            }

            if (textLines == null)
            {
                string note = "This view is not a text diff, so only the file list is included.";
                if (bodyNote != null && bodyNote.Trim().Length > 0)
                {
                    note = bodyNote;
                }

                lines.Add(new PdfLine(note, PdfLineStyle.Note));
                return new PdfReport(title, lines);
            }

            if (textLines.Count == 0)
            {
                lines.Add(new PdfLine("The text diff is empty.", PdfLineStyle.Note));
            }

            foreach (DiffPdfTextLine line in textLines)
            {
                string marker = string.IsNullOrEmpty(line.Marker) ? " " : line.Marker;
                PdfLineStyle style = marker == "+"
                    ? PdfLineStyle.Added
                    : marker == "-" || marker == "\u2212"
                        ? PdfLineStyle.Removed
                        : PdfLineStyle.Body;
                lines.Add(new PdfLine(marker + " " + line.Text, style));
            }

            if (textTruncated)
            {
                lines.Add(new PdfLine("The text diff was truncated.", PdfLineStyle.Note));
            }

            return new PdfReport(title, lines);
        }
    }
}
