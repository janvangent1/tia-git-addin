using System;
using System.Collections.Generic;
using System.Text;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Shallow SCL parser. It recovers a block, its declaration sections, regions, comments, and
    /// statements. It does not check types or control-flow nesting.
    /// </summary>
    internal static class SclParser
    {
        private static readonly HashSet<string> BlockOpeners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "FUNCTION_BLOCK", "FUNCTION", "ORGANIZATION_BLOCK", "DATA_BLOCK", "TYPE",
        };

        private static readonly HashSet<string> BoundaryWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "IF", "ELSIF", "ELSE", "END_IF", "CASE", "END_CASE", "FOR", "END_FOR", "WHILE", "END_WHILE",
            "REPEAT", "END_REPEAT", "UNTIL", "RETURN", "EXIT", "CONTINUE", "REGION", "END_REGION",
            "VAR", "VAR_INPUT", "VAR_OUTPUT", "VAR_IN_OUT", "VAR_TEMP", "VAR_STAT", "VAR_CONSTANT",
            "CONST", "STRUCT", "END_VAR", "END_CONST", "END_STRUCT", "BEGIN",
            "END_FUNCTION", "END_FUNCTION_BLOCK", "END_ORGANIZATION_BLOCK", "END_DATA_BLOCK", "END_TYPE",
        };

        public static SclDocument Parse(string? text)
        {
            var tokens = SclLexer.Tokenize(text ?? string.Empty);
            var parser = new Parser(tokens);
            return parser.ParseDocument();
        }

        private sealed class Parser
        {
            private readonly List<SclToken> tokens;
            private int index;
            private bool partial;
            private string partialReason = string.Empty;

            public Parser(List<SclToken> tokens)
            {
                this.tokens = tokens;
            }

            public SclDocument ParseDocument()
            {
                string ns = string.Empty;
                var blocks = new List<SclBlock>();
                SkipNewlines();
                if (IsWord("NAMESPACE"))
                {
                    index++;
                    ns = ReadUntilNewline();
                    SkipNewlines();
                }

                while (!Eof)
                {
                    SkipNewlinesAndComments();
                    if (Eof)
                    {
                        break;
                    }

                    if (IsBlockOpener())
                    {
                        blocks.Add(ParseBlock());
                        continue;
                    }

                    MarkPartial("Some text before the SCL block was not read.");
                    SkipLine();
                }

                return new SclDocument(ns, blocks, partial, partialReason);
            }

            private SclBlock ParseBlock()
            {
                string kind = tokens[index].Text;
                index++;
                SkipNewlines();
                string name = IsWord() ? tokens[index++].Text : string.Empty;
                SkipNewlines();
                string returnType = string.Empty;
                if (IsSymbol(":"))
                {
                    index++;
                    returnType = ReadUntilNewline();
                }

                var header = new List<SclMember>();
                var sections = new List<SclSection>();
                var body = new List<SclNode>();
                bool inBody = false;
                string endWord = EndWordFor(kind);

                while (!Eof)
                {
                    SkipNewlines();
                    if (Eof)
                    {
                        MarkPartial("The SCL block has no " + endWord + ".");
                        break;
                    }

                    if (IsWord(endWord))
                    {
                        index++;
                        break;
                    }

                    if (IsComment())
                    {
                        AddComment(inBody ? body : null, header);
                        continue;
                    }

                    if (TryReadSectionName(out string sectionName))
                    {
                        sections.Add(ParseSection(sectionName));
                        continue;
                    }

                    if (IsWord("BEGIN"))
                    {
                        index++;
                        inBody = true;
                        continue;
                    }

                    if (IsWord("REGION"))
                    {
                        body.Add(ParseRegion());
                        inBody = true;
                        continue;
                    }

                    if (!inBody)
                    {
                        SclMember? member = ReadHeaderLine();
                        if (member != null)
                        {
                            header.Add(member);
                        }

                        continue;
                    }

                    SclStatement? statement = ReadStatement();
                    if (statement != null)
                    {
                        body.Add(statement);
                    }
                }

                return new SclBlock(kind, name, returnType, header, sections, body);
            }

            private SclRegion ParseRegion()
            {
                index++;
                string name = ReadUntilNewline();
                var body = new List<SclNode>();
                while (!Eof)
                {
                    SkipNewlines();
                    if (Eof)
                    {
                        MarkPartial("A REGION has no END_REGION.");
                        break;
                    }

                    if (IsWord("END_REGION") || IsBlockEnd())
                    {
                        if (IsWord("END_REGION"))
                        {
                            index++;
                        }

                        break;
                    }

                    if (IsComment())
                    {
                        AddComment(body, null);
                        continue;
                    }

                    if (IsWord("REGION"))
                    {
                        body.Add(ParseRegion());
                        continue;
                    }

                    if (IsSectionStart())
                    {
                        break;
                    }

                    SclStatement? statement = ReadStatement();
                    if (statement != null)
                    {
                        body.Add(statement);
                    }
                }

                return new SclRegion(name, body);
            }

            private SclSection ParseSection(string name)
            {
                var section = new SclSection(name);
                string pendingComment = string.Empty;
                while (!Eof)
                {
                    SkipNewlines();
                    if (Eof || IsSectionEnd() || IsBlockEnd() || IsWord("BEGIN") || IsWord("REGION"))
                    {
                        if (IsSectionEnd())
                        {
                            index++;
                        }

                        break;
                    }

                    if (IsComment())
                    {
                        pendingComment = AppendComment(pendingComment, tokens[index].Text);
                        index++;
                        continue;
                    }

                    if (TryReadSectionName(out _))
                    {
                        break;
                    }

                    SclMember? member = ReadDeclaration(pendingComment);
                    pendingComment = string.Empty;
                    if (member != null)
                    {
                        section.Members.Add(member);
                    }
                }

                return section;
            }

            private SclMember? ReadDeclaration(string comment)
            {
                var names = new List<string>();
                var attributes = new List<string>();
                int depth = 0;
                while (!Eof && !IsNewline())
                {
                    if (IsSymbol("{"))
                    {
                        attributes.Add(ReadBraceText());
                        continue;
                    }

                    if (IsSymbol("(") || IsSymbol("["))
                    {
                        depth++;
                        index++;
                        continue;
                    }

                    if (IsSymbol(")") || IsSymbol("]"))
                    {
                        depth = Math.Max(0, depth - 1);
                        index++;
                        continue;
                    }

                    if (depth == 0 && IsSymbol(":"))
                    {
                        break;
                    }

                    if (depth == 0 && IsSymbol(","))
                    {
                        index++;
                        continue;
                    }

                    if (IsWord() || IsSymbol())
                    {
                        if (depth == 0 && IsWord())
                        {
                            names.Add(tokens[index].Text);
                        }

                        index++;
                        continue;
                    }

                    index++;
                }

                string attributeText = string.Join(" ", attributes);
                if (!IsSymbol(":"))
                {
                    return names.Count == 0
                        ? null
                        : new SclMember(string.Join(", ", names), string.Empty, comment, attributeText);
                }

                index++;
                string typeText = ReadTypeText(attributes);
                attributeText = string.Join(" ", attributes);
                if (names.Count == 0)
                {
                    return null;
                }

                if (names.Count == 1)
                {
                    return new SclMember(names[0], typeText, comment, attributeText);
                }

                // A, B : Int is two members. The caller adds one; extra names are joined so the
                // change is still visible as a single declaration line.
                return new SclMember(string.Join(", ", names), typeText, comment, attributeText);
            }

            private string ReadTypeText(List<string> attributes)
            {
                var parts = new List<string>();
                int depth = 0;
                while (!Eof)
                {
                    if (IsSymbol("{"))
                    {
                        attributes.Add(ReadBraceText());
                        continue;
                    }

                    if (IsNewline() && depth == 0)
                    {
                        break;
                    }

                    if (depth == 0 && IsSymbol(";"))
                    {
                        index++;
                        break;
                    }

                    if (IsSymbol("(") || IsSymbol("["))
                    {
                        depth++;
                    }
                    else if (IsSymbol(")") || IsSymbol("]"))
                    {
                        depth = Math.Max(0, depth - 1);
                    }

                    if (!IsNewline() && !IsComment())
                    {
                        parts.Add(tokens[index].Text);
                    }

                    index++;
                }

                return string.Join(" ", parts).Trim();
            }

            private SclMember? ReadHeaderLine()
            {
                if (IsSymbol("{"))
                {
                    return new SclMember(string.Empty, string.Empty, string.Empty, ReadBraceText());
                }

                string line = ReadUntilNewline();
                if (line.Length == 0)
                {
                    return null;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    return new SclMember(line, string.Empty, string.Empty);
                }

                string name = line.Substring(0, colon).Trim();
                string typeText = line.Substring(colon + 1).Trim().TrimEnd(';').Trim();
                return new SclMember(name, typeText, string.Empty);
            }

            private SclStatement? ReadStatement()
            {
                var words = new List<SclToken>();
                int depth = 0;
                while (!Eof)
                {
                    SclToken token = tokens[index];
                    if (token.Kind == SclTokenKind.Comment)
                    {
                        break;
                    }

                    if (token.Kind == SclTokenKind.Newline && depth == 0 && ShouldFlush(words))
                    {
                        index++;
                        break;
                    }

                    if (token.Kind == SclTokenKind.Newline && depth == 0 && words.Count == 0)
                    {
                        index++;
                        continue;
                    }

                    if (depth == 0 && words.Count > 0 && IsBoundary(token))
                    {
                        break;
                    }

                    if (token.Kind == SclTokenKind.Symbol && (token.Text == "(" || token.Text == "[" || token.Text == "{"))
                    {
                        depth++;
                    }
                    else if (token.Kind == SclTokenKind.Symbol && (token.Text == ")" || token.Text == "]" || token.Text == "}"))
                    {
                        depth = Math.Max(0, depth - 1);
                    }

                    if (token.Kind != SclTokenKind.Newline)
                    {
                        words.Add(token);
                    }

                    index++;
                    if (depth == 0 && token.Kind == SclTokenKind.Symbol && token.Text == ";")
                    {
                        break;
                    }

                    if (depth == 0 && IsBlockEnd())
                    {
                        break;
                    }
                }

                if (words.Count == 0)
                {
                    return null;
                }

                return new SclStatement(Display(words), Normalize(words));
            }

            private static bool ShouldFlush(List<SclToken> words)
            {
                if (words.Count == 0)
                {
                    return false;
                }

                string last = words[words.Count - 1].Text;
                if (last.Equals("THEN", StringComparison.OrdinalIgnoreCase)
                    || last.Equals("OF", StringComparison.OrdinalIgnoreCase)
                    || last.Equals("DO", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (words.Count == 1 && BoundaryWords.Contains(words[0].Text))
                {
                    return true;
                }

                return false;
            }

            private static bool IsBoundary(SclToken token)
            {
                return token.Kind == SclTokenKind.Word && BoundaryWords.Contains(token.Text);
            }

            private bool IsSectionStart()
            {
                if (!IsWord())
                {
                    return false;
                }

                string word = tokens[index].Text;
                return word.Equals("VAR", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_INPUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_OUTPUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_IN_OUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_TEMP", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_STAT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_CONSTANT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("CONST", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("STRUCT", StringComparison.OrdinalIgnoreCase);
            }

            private bool TryReadSectionName(out string name)
            {
                name = string.Empty;
                if (!IsWord())
                {
                    return false;
                }

                string word = tokens[index].Text;
                if (word.Equals("VAR_INPUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_OUTPUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_IN_OUT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_TEMP", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_STAT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("VAR_CONSTANT", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("CONST", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("STRUCT", StringComparison.OrdinalIgnoreCase))
                {
                    name = word.ToUpperInvariant();
                    index++;
                    return true;
                }

                if (!word.Equals("VAR", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                int look = index + 1;
                while (look < tokens.Count && tokens[look].Kind == SclTokenKind.Newline)
                {
                    look++;
                }

                name = "VAR";
                index++;
                if (look < tokens.Count && tokens[look].Kind == SclTokenKind.Word)
                {
                    string modifier = tokens[look].Text;
                    if (modifier.Equals("CONSTANT", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("RETAIN", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("NON_RETAIN", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("INPUT", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("OUTPUT", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("IN_OUT", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("TEMP", StringComparison.OrdinalIgnoreCase)
                        || modifier.Equals("STAT", StringComparison.OrdinalIgnoreCase))
                    {
                        name = "VAR " + modifier.ToUpperInvariant();
                        index = look + 1;
                    }
                }

                return true;
            }

            private void AddComment(List<SclNode>? body, List<SclMember>? header)
            {
                string text = tokens[index].Text;
                index++;
                if (body != null)
                {
                    body.Add(new SclComment(text));
                }
                else if (header != null && text.Length > 0)
                {
                    header.Add(new SclMember(string.Empty, string.Empty, text));
                }
            }

            private static string AppendComment(string pending, string text)
            {
                if (pending.Length == 0)
                {
                    return text.Trim();
                }

                return pending + " " + text.Trim();
            }

            private string ReadUntilNewline()
            {
                var parts = new List<string>();
                while (!Eof && !IsNewline())
                {
                    if (IsSymbol("{"))
                    {
                        break;
                    }

                    if (!IsComment())
                    {
                        parts.Add(tokens[index].Text);
                    }

                    index++;
                }

                if (IsNewline())
                {
                    index++;
                }

                return string.Join(" ", parts).Trim();
            }

            private void SkipLine()
            {
                while (!Eof && !IsNewline())
                {
                    index++;
                }

                if (IsNewline())
                {
                    index++;
                }
            }

            private void SkipNewlines()
            {
                while (IsNewline())
                {
                    index++;
                }
            }

            private void SkipNewlinesAndComments()
            {
                while (IsNewline() || IsComment())
                {
                    index++;
                }
            }

            private string ReadBraceText()
            {
                if (!IsSymbol("{"))
                {
                    return string.Empty;
                }

                var parts = new List<string>();
                int depth = 0;
                while (!Eof)
                {
                    string text = tokens[index].Text;
                    if (IsSymbol("{"))
                    {
                        depth++;
                    }
                    else if (IsSymbol("}"))
                    {
                        depth--;
                        parts.Add(text);
                        index++;
                        if (depth == 0)
                        {
                            break;
                        }

                        continue;
                    }

                    if (!IsNewline() && !IsComment())
                    {
                        parts.Add(text);
                    }

                    index++;
                }

                return JoinCode(parts);
            }

            private static string JoinCode(List<string> parts)
            {
                var builder = new StringBuilder();
                foreach (string part in parts)
                {
                    if (part.Length == 0)
                    {
                        continue;
                    }

                    if (builder.Length > 0 && NeedsSpace(builder[builder.Length - 1], part))
                    {
                        builder.Append(' ');
                    }

                    builder.Append(part);
                }

                return builder.ToString();
            }

            private static bool NeedsSpace(char previous, string next)
            {
                if (previous == '{' || previous == '(' || previous == '[')
                {
                    return false;
                }

                return next != ";" && next != "," && next != "}" && next != ")" && next != "]";
            }

            private bool IsSectionEnd()
            {
                return IsWord("END_VAR") || IsWord("END_CONST") || IsWord("END_STRUCT");
            }

            private bool IsBlockEnd()
            {
                return IsWord("END_FUNCTION") || IsWord("END_FUNCTION_BLOCK") || IsWord("END_ORGANIZATION_BLOCK")
                    || IsWord("END_DATA_BLOCK") || IsWord("END_TYPE");
            }

            private bool IsBlockOpener()
            {
                return IsWord() && BlockOpeners.Contains(tokens[index].Text);
            }

            private static string EndWordFor(string kind)
            {
                return "END_" + kind.ToUpperInvariant();
            }

            private void MarkPartial(string reason)
            {
                if (!partial)
                {
                    partial = true;
                    partialReason = reason;
                }
            }

            private bool Eof => index >= tokens.Count;

            private bool IsNewline() => !Eof && tokens[index].Kind == SclTokenKind.Newline;

            private bool IsComment() => !Eof && tokens[index].Kind == SclTokenKind.Comment;

            private bool IsWord() => !Eof && tokens[index].Kind == SclTokenKind.Word;

            private bool IsWord(string word)
            {
                return IsWord() && tokens[index].Text.Equals(word, StringComparison.OrdinalIgnoreCase);
            }

            private bool IsSymbol() => !Eof && tokens[index].Kind == SclTokenKind.Symbol;

            private bool IsSymbol(string symbol)
            {
                return IsSymbol() && tokens[index].Text == symbol;
            }

            private static string Display(List<SclToken> words)
            {
                var builder = new StringBuilder();
                foreach (SclToken token in words)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(token.Text);
                }

                return builder.ToString();
            }

            private static string Normalize(List<SclToken> words)
            {
                var builder = new StringBuilder();
                foreach (SclToken token in words)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    builder.Append(token.Kind == SclTokenKind.String
                        ? token.Text
                        : token.Text.ToLowerInvariant());
                }

                return builder.ToString();
            }
        }
    }
}
