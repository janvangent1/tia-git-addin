using System.Collections.Generic;
using System.Text;

namespace TiaGitAddIn.Services.Comparison
{
    internal enum SclTokenKind
    {
        Newline,
        Word,
        String,
        Symbol,
        Comment,
    }

    internal readonly struct SclToken
    {
        public SclToken(SclTokenKind kind, string text, int line)
        {
            Kind = kind;
            Text = text;
            Line = line;
        }

        public SclTokenKind Kind { get; }
        public string Text { get; }
        public int Line { get; }
    }

    /// <summary>
    /// SCL tokenizer that keeps comments and strings. It is not a compiler: identifiers, numbers,
    /// and operators are enough to rebuild declarations and statements.
    /// </summary>
    internal static class SclLexer
    {
        public static List<SclToken> Tokenize(string text)
        {
            var tokens = new List<SclToken>();
            if (string.IsNullOrEmpty(text))
            {
                return tokens;
            }

            int i = 0;
            int line = 1;
            int length = text.Length;
            while (i < length)
            {
                char c = text[i];
                if (c == '\r')
                {
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    tokens.Add(new SclToken(SclTokenKind.Newline, "\n", line));
                    line++;
                    i++;
                    continue;
                }

                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }

                if (c == '/' && i + 1 < length && text[i + 1] == '/')
                {
                    int start = i + 2;
                    i += 2;
                    while (i < length && text[i] != '\n' && text[i] != '\r')
                    {
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.Comment, text.Substring(start, i - start).Trim(), line));
                    continue;
                }

                if (c == '(' && i + 1 < length && text[i + 1] == '*')
                {
                    int startLine = line;
                    int start = i + 2;
                    i += 2;
                    while (i + 1 < length && !(text[i] == '*' && text[i + 1] == ')'))
                    {
                        if (text[i] == '\n')
                        {
                            line++;
                        }

                        i++;
                    }

                    int end = i;
                    i = i + 1 < length ? i + 2 : length;
                    tokens.Add(new SclToken(SclTokenKind.Comment, text.Substring(start, end - start).Trim(), startLine));
                    continue;
                }

                if (c == '\'')
                {
                    var literal = new StringBuilder();
                    literal.Append('\'');
                    i++;
                    while (i < length)
                    {
                        if (text[i] == '\n')
                        {
                            line++;
                        }

                        if (text[i] == '\'')
                        {
                            literal.Append('\'');
                            if (i + 1 < length && text[i + 1] == '\'')
                            {
                                literal.Append('\'');
                                i += 2;
                                continue;
                            }

                            i++;
                            break;
                        }

                        literal.Append(text[i]);
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.String, literal.ToString(), line));
                    continue;
                }

                if (c == '"')
                {
                    int start = i;
                    i++;
                    while (i < length && text[i] != '"')
                    {
                        if (text[i] == '\n')
                        {
                            line++;
                        }

                        i++;
                    }

                    if (i < length)
                    {
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.Word, text.Substring(start, i - start), line));
                    continue;
                }

                if (c == '#')
                {
                    int start = i;
                    i++;
                    while (i < length && IsWordChar(text[i]))
                    {
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.Word, text.Substring(start, i - start), line));
                    continue;
                }

                if (c == '%')
                {
                    int start = i;
                    i++;
                    while (i < length && (IsWordChar(text[i]) || text[i] == '.'))
                    {
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.Word, text.Substring(start, i - start), line));
                    continue;
                }

                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    i++;
                    while (i < length && IsWordChar(text[i]))
                    {
                        i++;
                    }

                    while (i + 1 < length && text[i] == '#' && IsWordChar(text[i + 1]))
                    {
                        i++;
                        while (i < length && IsWordChar(text[i]))
                        {
                            i++;
                        }
                    }

                    tokens.Add(new SclToken(SclTokenKind.Word, text.Substring(start, i - start), line));
                    continue;
                }

                if (char.IsDigit(c))
                {
                    int start = i;
                    i++;
                    while (i < length && char.IsDigit(text[i]))
                    {
                        i++;
                    }

                    if (i + 1 < length && text[i] == '.' && char.IsDigit(text[i + 1]))
                    {
                        i++;
                        while (i < length && char.IsDigit(text[i]))
                        {
                            i++;
                        }
                    }

                    while (i < length && char.IsLetter(text[i]))
                    {
                        i++;
                    }

                    tokens.Add(new SclToken(SclTokenKind.Word, text.Substring(start, i - start), line));
                    continue;
                }

                if (c == ':' && i + 1 < length && text[i + 1] == '=')
                {
                    tokens.Add(new SclToken(SclTokenKind.Symbol, ":=", line));
                    i += 2;
                    continue;
                }

                if (c == '<' && i + 1 < length && (text[i + 1] == '>' || text[i + 1] == '='))
                {
                    tokens.Add(new SclToken(SclTokenKind.Symbol, text.Substring(i, 2), line));
                    i += 2;
                    continue;
                }

                if (c == '>' && i + 1 < length && text[i + 1] == '=')
                {
                    tokens.Add(new SclToken(SclTokenKind.Symbol, ">=", line));
                    i += 2;
                    continue;
                }

                if (c == '.' && i + 1 < length && text[i + 1] == '.')
                {
                    tokens.Add(new SclToken(SclTokenKind.Symbol, "..", line));
                    i += 2;
                    continue;
                }

                tokens.Add(new SclToken(SclTokenKind.Symbol, c.ToString(), line));
                i++;
            }

            return tokens;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
