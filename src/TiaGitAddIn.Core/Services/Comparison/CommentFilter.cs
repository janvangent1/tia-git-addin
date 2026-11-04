using System;
using System.Text;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Removes SCL comments so they can be left out of a line diff.
    /// A line comment starts with <c>//</c>, except inside a string or after a colon (<c>http://</c>).
    /// A block comment sits between <c>(*</c> and <c>*)</c>, or between <c>*(</c> and <c>*)</c>, and may span lines.
    /// </summary>
    public static class CommentFilter
    {
        public static void Strip(string[] lines, out string[] code, out bool[] commentOnly)
        {
            code = new string[lines.Length];
            commentOnly = new bool[lines.Length];
            bool inBlock = false;
            for (int index = 0; index < lines.Length; index++)
            {
                code[index] = StripLine(lines[index] ?? string.Empty, ref inBlock, out commentOnly[index]);
            }
        }

        private static string StripLine(string line, ref bool inBlock, out bool commentOnly)
        {
            var code = new StringBuilder(line.Length);
            bool sawComment = inBlock;
            int index = 0;
            while (index < line.Length)
            {
                if (inBlock)
                {
                    int end = line.IndexOf("*)", index, StringComparison.Ordinal);
                    sawComment = true;
                    if (end < 0)
                    {
                        index = line.Length;
                        break;
                    }

                    inBlock = false;
                    index = end + 2;
                    continue;
                }

                char character = line[index];
                if (character == '\'' || character == '"')
                {
                    index = AppendString(line, index, character, code);
                    continue;
                }

                if (character == '/' && index + 1 < line.Length && line[index + 1] == '/'
                    && (index == 0 || line[index - 1] != ':'))
                {
                    sawComment = true;
                    TrimEndSpace(code);
                    break;
                }

                if (IsBlockStart(line, index))
                {
                    sawComment = true;
                    inBlock = true;
                    index += 2;
                    continue;
                }

                code.Append(character);
                index++;
            }

            string text = code.ToString();
            commentOnly = sawComment && IsBlank(text);
            return text;
        }

        private static int AppendString(string line, int index, char quote, StringBuilder code)
        {
            code.Append(quote);
            index++;
            while (index < line.Length)
            {
                char character = line[index];
                code.Append(character);
                index++;
                if (quote == '\'' && character == '\'' && index < line.Length && line[index] == '\'')
                {
                    code.Append(line[index]);
                    index++;
                    continue;
                }

                if (character == quote)
                {
                    break;
                }
            }

            return index;
        }

        private static bool IsBlockStart(string line, int index)
        {
            if (index + 1 >= line.Length)
            {
                return false;
            }

            return (line[index] == '(' && line[index + 1] == '*')
                || (line[index] == '*' && line[index + 1] == '(');
        }

        private static void TrimEndSpace(StringBuilder code)
        {
            int end = code.Length;
            while (end > 0 && (code[end - 1] == ' ' || code[end - 1] == '\t'))
            {
                end--;
            }

            if (end < code.Length)
            {
                code.Length = end;
            }
        }

        private static bool IsBlank(string text)
        {
            for (int index = 0; index < text.Length; index++)
            {
                char character = text[index];
                if (character != ' ' && character != '\t' && character != '\v' && character != '\f')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
