using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TiaGitAddIn.Reporting
{
    public static class PdfReportWriter
    {
        private const double PageHeight = 842;
        private const double Margin = 40;
        private const double LineHeight = 12;
        private const int CharactersPerLine = 90;

        public static void Write(PdfReport report, string path)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("PDF path is required.", nameof(path));
            }

            using (FileStream stream = File.Create(path))
            {
                Write(report, stream);
            }
        }

        public static void Write(PdfReport report, Stream stream)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            List<List<string>> pages = Paginate(report.Lines);
            var contents = new string[pages.Count];
            for (int page = 0; page < pages.Count; page++)
            {
                contents[page] = BuildContent(pages[page], page + 1, pages.Count);
            }

            WriteDocument(stream, contents);
        }

        private static List<List<string>> Paginate(IReadOnlyList<PdfLine> lines)
        {
            int linesPerPage = Math.Max(1, (int)((PageHeight - (Margin * 2)) / LineHeight));
            var pages = new List<List<string>>();
            var current = new List<string>();
            if (lines != null)
            {
                foreach (PdfLine line in lines)
                {
                    if (line == null)
                    {
                        continue;
                    }

                    if (line.Style == PdfLineStyle.PageBreak)
                    {
                        if (current.Count > 0)
                        {
                            pages.Add(current);
                            current = new List<string>();
                        }

                        continue;
                    }

                    foreach (string wrapped in WrapLine(line))
                    {
                        if (current.Count >= linesPerPage)
                        {
                            pages.Add(current);
                            current = new List<string>();
                        }

                        current.Add(wrapped);
                    }
                }
            }

            if (current.Count > 0 || pages.Count == 0)
            {
                pages.Add(current);
            }

            return pages;
        }

        private static List<string> WrapLine(PdfLine line)
        {
            var wrapped = new List<string>();
            string style = StyleCode(line.Style);
            string text = line.Text ?? string.Empty;
            if (text.Length == 0)
            {
                wrapped.Add(style + " ");
                return wrapped;
            }

            string[] rows = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string row in rows)
            {
                if (row.Length == 0)
                {
                    wrapped.Add(style + " ");
                    continue;
                }

                int start = 0;
                while (start < row.Length)
                {
                    int remaining = row.Length - start;
                    if (remaining <= CharactersPerLine)
                    {
                        wrapped.Add(style + row.Substring(start));
                        break;
                    }

                    int length = BreakLength(row, start, CharactersPerLine);
                    wrapped.Add(style + row.Substring(start, length).TrimEnd());
                    start += length;
                    while (start < row.Length && row[start] == ' ')
                    {
                        start++;
                    }
                }
            }

            return wrapped;
        }

        private static string StyleCode(PdfLineStyle style)
        {
            switch (style)
            {
                case PdfLineStyle.Heading:
                    return "H";
                case PdfLineStyle.Added:
                    return "A";
                case PdfLineStyle.Removed:
                    return "R";
                case PdfLineStyle.Changed:
                    return "C";
                case PdfLineStyle.Note:
                    return "N";
                default:
                    return "B";
            }
        }

        private static int BreakLength(string row, int start, int max)
        {
            int window = Math.Min(max, row.Length - start);
            int slash = row.LastIndexOf('/', start + window - 1, window);
            int space = row.LastIndexOf(' ', start + window - 1, window);
            int best = Math.Max(slash, space);
            if (best > start)
            {
                return best - start + 1;
            }

            return window;
        }

        private static string BuildContent(List<string> lines, int pageNumber, int pageCount)
        {
            var builder = new StringBuilder();
            double y = PageHeight - Margin;
            foreach (string packed in lines)
            {
                y -= LineHeight;
                char style = packed.Length == 0 ? 'B' : packed[0];
                string text = packed.Length == 0 ? string.Empty : packed.Substring(1);
                AppendText(builder, style, Margin, y, text);
            }

            AppendText(
                builder,
                'N',
                Margin,
                24,
                "Page " + pageNumber.ToString(CultureInfo.InvariantCulture) + " of " +
                pageCount.ToString(CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        private static void AppendText(StringBuilder builder, char style, double x, double y, string text)
        {
            builder.Append(ColorFor(style));
            builder.Append(" BT ");
            builder.Append(style == 'H' ? "/F1 11 Tf " : "/F2 9 Tf ");
            builder.Append("1 0 0 1 ");
            builder.Append(x.ToString(CultureInfo.InvariantCulture));
            builder.Append(' ');
            builder.Append(y.ToString(CultureInfo.InvariantCulture));
            builder.Append(" Tm (");
            builder.Append(Escape(text));
            builder.Append(") Tj ET\n");
        }

        private static string ColorFor(char style)
        {
            switch (style)
            {
                case 'A':
                    return "0 0.45 0.15 rg\n";
                case 'R':
                    return "0.7 0.1 0.1 rg\n";
                case 'C':
                    return "0.45 0.32 0 rg\n";
                case 'N':
                    return "0.35 0.35 0.35 rg\n";
                default:
                    return "0 0 0 rg\n";
            }
        }

        private static string Escape(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char character in text)
            {
                if (character == '\\' || character == '(' || character == ')')
                {
                    builder.Append('\\');
                    builder.Append(character);
                    continue;
                }

                if (character == '\u2212')
                {
                    builder.Append('-');
                    continue;
                }

                if (WinAnsi.TryGetValue(character, out byte ansi))
                {
                    builder.Append('\\');
                    builder.Append(Convert.ToString(ansi, 8).PadLeft(3, '0'));
                    continue;
                }

                if (character >= 32 && character <= 126)
                {
                    builder.Append(character);
                    continue;
                }

                if (character > 126 && character <= 255)
                {
                    builder.Append('\\');
                    builder.Append(Convert.ToString(character, 8).PadLeft(3, '0'));
                    continue;
                }

                builder.Append('?');
            }

            return builder.ToString();
        }

        private static void WriteDocument(Stream stream, string[] pageContents)
        {
            var offsets = new List<long> { 0 };
            Write(stream, "%PDF-1.4\n");
            WriteObject(stream, offsets, "1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");

            var kids = new StringBuilder();
            for (int page = 0; page < pageContents.Length; page++)
            {
                int pageObject = 5 + (page * 2);
                if (kids.Length > 0)
                {
                    kids.Append(' ');
                }

                kids.Append(pageObject.ToString(CultureInfo.InvariantCulture));
                kids.Append(" 0 R");
            }

            WriteObject(
                stream,
                offsets,
                "2 0 obj << /Type /Pages /Count " + pageContents.Length.ToString(CultureInfo.InvariantCulture) +
                " /Kids [" + kids + "] >> endobj\n");
            WriteObject(stream, offsets, "3 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >> endobj\n");
            WriteObject(stream, offsets, "4 0 obj << /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >> endobj\n");

            for (int page = 0; page < pageContents.Length; page++)
            {
                int pageObject = 5 + (page * 2);
                int contentObject = pageObject + 1;
                string content = pageContents[page] ?? string.Empty;
                WriteObject(
                    stream,
                    offsets,
                    pageObject.ToString(CultureInfo.InvariantCulture) +
                    " 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents " +
                    contentObject.ToString(CultureInfo.InvariantCulture) +
                    " 0 R /Resources << /Font << /F1 3 0 R /F2 4 0 R >> >> >> endobj\n");
                WriteObject(
                    stream,
                    offsets,
                    contentObject.ToString(CultureInfo.InvariantCulture) +
                    " 0 obj << /Length " + content.Length.ToString(CultureInfo.InvariantCulture) +
                    " >> stream\n" + content + "endstream\nendobj\n");
            }

            long xref = stream.Position;
            Write(stream, "xref\n0 " + offsets.Count.ToString(CultureInfo.InvariantCulture) + "\n");
            Write(stream, "0000000000 65535 f \n");
            for (int index = 1; index < offsets.Count; index++)
            {
                Write(stream, offsets[index].ToString("0000000000", CultureInfo.InvariantCulture) + " 00000 n \n");
            }

            Write(stream, "trailer << /Size " + offsets.Count.ToString(CultureInfo.InvariantCulture) + " /Root 1 0 R >>\n");
            Write(stream, "startxref\n" + xref.ToString(CultureInfo.InvariantCulture) + "\n%%EOF\n");
        }

        private static void WriteObject(Stream stream, List<long> offsets, string body)
        {
            offsets.Add(stream.Position);
            Write(stream, body);
        }

        private static readonly Dictionary<char, byte> WinAnsi = new Dictionary<char, byte>
        {
            { '\u2013', 0x96 },
            { '\u2014', 0x97 },
            { '\u2018', 0x91 },
            { '\u2019', 0x92 },
            { '\u201C', 0x93 },
            { '\u201D', 0x94 },
            { '\u2022', 0x95 },
            { '\u2026', 0x85 },
            { '\u20AC', 0x80 },
            { '\u00A0', 0xA0 },
        };

        private static void Write(Stream stream, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
        }
    }
}
