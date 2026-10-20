using System.Text;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Models.Comparison
{
    /// <summary>
    /// Git's whitespace options for a line diff. <see cref="IgnoreAllSpace"/> is the product default.
    /// When more than one space rule is on, the strongest one is used: all space, then a change in the
    /// amount of space, then tab expansion and trailing space together. Blank lines are separate.
    /// </summary>
    public readonly struct DiffWhitespace
    {
        public DiffWhitespace(
            bool ignoreTabExpansion,
            bool ignoreTrailingSpace,
            bool ignoreSpaceChange,
            bool ignoreAllSpace,
            bool ignoreBlankLines)
        {
            IgnoreTabExpansion = ignoreTabExpansion;
            IgnoreTrailingSpace = ignoreTrailingSpace;
            IgnoreSpaceChange = ignoreSpaceChange;
            IgnoreAllSpace = ignoreAllSpace;
            IgnoreBlankLines = ignoreBlankLines;
        }

        /// <summary>No whitespace rule. Tests use this so a space still counts as a change.</summary>
        public static DiffWhitespace Exact { get; } = new DiffWhitespace(false, false, false, false, false);

        public bool IgnoreTabExpansion { get; }

        public bool IgnoreTrailingSpace { get; }

        public bool IgnoreSpaceChange { get; }

        public bool IgnoreAllSpace { get; }

        public bool IgnoreBlankLines { get; }

        public static DiffWhitespace From(GitConfiguration configuration)
        {
            return new DiffWhitespace(
                configuration.IgnoreTabExpansion,
                configuration.IgnoreTrailingSpace,
                configuration.IgnoreSpaceChange,
                configuration.IgnoreAllSpace,
                configuration.IgnoreBlankLines);
        }

        public bool LinesMatch(string left, string right)
        {
            if (string.Equals(left, right, System.StringComparison.Ordinal))
            {
                return true;
            }

            if (IgnoreAllSpace)
            {
                return MatchIgnoringAllSpace(left, right);
            }

            if (IgnoreSpaceChange)
            {
                return MatchIgnoringSpaceChange(left, right);
            }

            if (IgnoreTabExpansion || IgnoreTrailingSpace)
            {
                return MatchTabOrTrailing(left, right);
            }

            return false;
        }

        public bool IsBlankLine(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (!IsSpace(text[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private bool MatchTabOrTrailing(string left, string right)
        {
            string normalizedLeft = IgnoreTabExpansion ? ExpandTabs(left) : left;
            string normalizedRight = IgnoreTabExpansion ? ExpandTabs(right) : right;
            if (IgnoreTrailingSpace)
            {
                normalizedLeft = TrimEndSpace(normalizedLeft);
                normalizedRight = TrimEndSpace(normalizedRight);
            }

            return string.Equals(normalizedLeft, normalizedRight, System.StringComparison.Ordinal);
        }

        private static bool MatchIgnoringAllSpace(string left, string right)
        {
            int i = 0;
            int j = 0;
            while (i < left.Length && j < right.Length)
            {
                while (i < left.Length && IsSpace(left[i]))
                {
                    i++;
                }

                while (j < right.Length && IsSpace(right[j]))
                {
                    j++;
                }

                if (i >= left.Length || j >= right.Length)
                {
                    break;
                }

                if (left[i] != right[j])
                {
                    return false;
                }

                i++;
                j++;
            }

            while (i < left.Length && IsSpace(left[i]))
            {
                i++;
            }

            while (j < right.Length && IsSpace(right[j]))
            {
                j++;
            }

            return i == left.Length && j == right.Length;
        }

        private static bool MatchIgnoringSpaceChange(string left, string right)
        {
            int i = 0;
            int j = 0;
            while (i < left.Length && j < right.Length)
            {
                if (IsSpace(left[i]))
                {
                    if (!IsSpace(right[j]))
                    {
                        return false;
                    }

                    while (i < left.Length && IsSpace(left[i]))
                    {
                        i++;
                    }

                    while (j < right.Length && IsSpace(right[j]))
                    {
                        j++;
                    }

                    continue;
                }

                if (left[i] != right[j])
                {
                    return false;
                }

                i++;
                j++;
            }

            while (i < left.Length && IsSpace(left[i]))
            {
                i++;
            }

            while (j < right.Length && IsSpace(right[j]))
            {
                j++;
            }

            return i == left.Length && j == right.Length;
        }

        private static string ExpandTabs(string text)
        {
            if (text.IndexOf('\t') < 0)
            {
                return text;
            }

            var expanded = new StringBuilder(text.Length);
            int column = 0;
            foreach (char character in text)
            {
                if (character == '\t')
                {
                    int width = 8 - (column % 8);
                    expanded.Append(' ', width);
                    column += width;
                }
                else
                {
                    expanded.Append(character);
                    column++;
                }
            }

            return expanded.ToString();
        }

        private static string TrimEndSpace(string text)
        {
            int end = text.Length;
            while (end > 0 && IsSpace(text[end - 1]))
            {
                end--;
            }

            return end == text.Length ? text : text.Substring(0, end);
        }

        private static bool IsSpace(char character) =>
            character == ' ' || character == '\t' || character == '\v' || character == '\f';
    }
}
