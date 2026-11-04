using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using TiaGitAddIn.Models.Comparison;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Compares two XML documents by element and attribute. Attribute order and insignificant
    /// whitespace do not count as changes. A document that does not parse stays on the line diff.
    /// </summary>
    public static class XmlStructureComparer
    {
        public const string Limitation =
            "XML is compared by element and attribute. Line breaks and the order of attributes are ignored.";

        private const int MaximumChanges = 400;

        private static readonly string[] IdentityAttributes =
        {
            "name", "number", "id", "index", "type", "kind"
        };

        public static bool TryCompare(ComparisonRawText rawText, out TextPresentation presentation)
        {
            presentation = null!;
            if (rawText == null || rawText.IsLeftMissing || rawText.IsRightMissing)
            {
                return false;
            }

            if (!TryParse(rawText.LeftText, out XElement? left) || !TryParse(rawText.RightText, out XElement? right))
            {
                return false;
            }

            var lines = new List<TextDiffLine>();
            var budget = new Budget();
            if (!SameName(left!, right!))
            {
                Emit(lines, budget, TextDiffLineKind.Removed, Describe(string.Empty, left!));
                Emit(lines, budget, TextDiffLineKind.Added, Describe(string.Empty, right!));
            }
            else
            {
                CompareElements(left!, right!, Label(left!, null), lines, budget);
            }

            if (lines.Count == 0)
            {
                lines.Add(new TextDiffLine(TextDiffLineKind.Unchanged, null, null, "No element or attribute changes."));
            }
            else if (budget.Stopped)
            {
                lines.Add(new TextDiffLine(TextDiffLineKind.Omitted, null, null, "Further XML changes were left out."));
            }

            presentation = new TextPresentation(lines, budget.Stopped, false);
            return true;
        }

        private static void CompareElements(XElement left, XElement right, string path, List<TextDiffLine> lines, Budget budget)
        {
            if (budget.Stopped)
            {
                return;
            }

            CompareAttributes(left, right, path, lines, budget);
            CompareText(left, right, path, lines, budget);
            CompareChildren(left, right, path, lines, budget);
        }

        private static void CompareAttributes(XElement left, XElement right, string path, List<TextDiffLine> lines, Budget budget)
        {
            Dictionary<string, string> leftValues = Attributes(left);
            Dictionary<string, string> rightValues = Attributes(right);
            foreach (KeyValuePair<string, string> attribute in leftValues)
            {
                if (budget.Stopped)
                {
                    return;
                }

                if (!rightValues.TryGetValue(attribute.Key, out string? newer))
                {
                    Emit(lines, budget, TextDiffLineKind.Removed, path + "/@" + attribute.Key + " = " + attribute.Value);
                }
                else if (!string.Equals(attribute.Value, newer, StringComparison.Ordinal))
                {
                    Emit(lines, budget, TextDiffLineKind.Removed, path + "/@" + attribute.Key + " = " + attribute.Value);
                    Emit(lines, budget, TextDiffLineKind.Added, path + "/@" + attribute.Key + " = " + newer);
                }
            }

            foreach (KeyValuePair<string, string> attribute in rightValues)
            {
                if (budget.Stopped)
                {
                    return;
                }

                if (!leftValues.ContainsKey(attribute.Key))
                {
                    Emit(lines, budget, TextDiffLineKind.Added, path + "/@" + attribute.Key + " = " + attribute.Value);
                }
            }
        }

        private static void CompareText(XElement left, XElement right, string path, List<TextDiffLine> lines, Budget budget)
        {
            string before = DirectText(left);
            string after = DirectText(right);
            if (string.Equals(before, after, StringComparison.Ordinal) || budget.Stopped)
            {
                return;
            }

            Emit(lines, budget, TextDiffLineKind.Removed, path + " text = " + before);
            Emit(lines, budget, TextDiffLineKind.Added, path + " text = " + after);
        }

        private static void CompareChildren(XElement left, XElement right, string path, List<TextDiffLine> lines, Budget budget)
        {
            List<XElement> leftChildren = left.Elements().ToList();
            List<XElement> rightChildren = right.Elements().ToList();
            var leftMatched = new bool[leftChildren.Count];
            var rightMatched = new bool[rightChildren.Count];
            for (int i = 0; i < leftChildren.Count; i++)
            {
                string? identity = Identity(leftChildren[i]);
                if (identity == null)
                {
                    continue;
                }

                for (int j = 0; j < rightChildren.Count; j++)
                {
                    if (rightMatched[j] || !SameName(leftChildren[i], rightChildren[j]))
                    {
                        continue;
                    }

                    if (string.Equals(identity, Identity(rightChildren[j]), StringComparison.Ordinal))
                    {
                        leftMatched[i] = true;
                        rightMatched[j] = true;
                        CompareElements(leftChildren[i], rightChildren[j], path + "/" + Label(leftChildren[i], null), lines, budget);
                        break;
                    }
                }
            }

            var leftoverLeft = new List<XElement>();
            var leftoverRight = new List<XElement>();
            for (int i = 0; i < leftChildren.Count; i++)
            {
                if (leftMatched[i])
                {
                    continue;
                }

                if (Identity(leftChildren[i]) != null)
                {
                    Emit(lines, budget, TextDiffLineKind.Removed, "Removed " + Describe(path, leftChildren[i]));
                }
                else
                {
                    leftoverLeft.Add(leftChildren[i]);
                }
            }

            for (int j = 0; j < rightChildren.Count; j++)
            {
                if (rightMatched[j])
                {
                    continue;
                }

                if (Identity(rightChildren[j]) != null)
                {
                    Emit(lines, budget, TextDiffLineKind.Added, "Added " + Describe(path, rightChildren[j]));
                }
                else
                {
                    leftoverRight.Add(rightChildren[j]);
                }
            }

            MatchByPosition(leftoverLeft, leftoverRight, path, lines, budget);
        }

        private static void MatchByPosition(
            List<XElement> left,
            List<XElement> right,
            string path,
            List<TextDiffLine> lines,
            Budget budget)
        {
            var names = new List<string>();
            foreach (XElement element in left.Concat(right))
            {
                if (!names.Contains(element.Name.LocalName))
                {
                    names.Add(element.Name.LocalName);
                }
            }

            foreach (string name in names)
            {
                List<XElement> leftNamed = left.Where(element => element.Name.LocalName == name).ToList();
                List<XElement> rightNamed = right.Where(element => element.Name.LocalName == name).ToList();
                int shared = Math.Min(leftNamed.Count, rightNamed.Count);
                for (int i = 0; i < shared; i++)
                {
                    string childPath = path + "/" + Label(leftNamed[i], i + 1);
                    CompareElements(leftNamed[i], rightNamed[i], childPath, lines, budget);
                }

                for (int i = shared; i < leftNamed.Count; i++)
                {
                    Emit(lines, budget, TextDiffLineKind.Removed, "Removed " + Describe(path, leftNamed[i]));
                }

                for (int i = shared; i < rightNamed.Count; i++)
                {
                    Emit(lines, budget, TextDiffLineKind.Added, "Added " + Describe(path, rightNamed[i]));
                }
            }
        }

        private static Dictionary<string, string> Attributes(XElement element)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration)
                {
                    continue;
                }

                values[attribute.Name.LocalName] = attribute.Value ?? string.Empty;
            }

            return values;
        }

        private static string? Identity(XElement element)
        {
            foreach (string candidate in IdentityAttributes)
            {
                XAttribute? attribute = element.Attributes().FirstOrDefault(item =>
                    item.Name.LocalName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
                if (attribute != null && attribute.Value.Length > 0)
                {
                    return candidate + "=" + attribute.Value;
                }
            }

            return null;
        }

        private static string ChildText(XElement element, string name)
        {
            foreach (XElement child in element.Descendants())
            {
                if (child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase) && !child.HasElements)
                {
                    string text = (child.Value ?? string.Empty).Trim();
                    if (text.Length > 0)
                    {
                        return text;
                    }
                }
            }

            return string.Empty;
        }

        private static string DirectText(XElement element)
        {
            return string.Concat(element.Nodes().OfType<XText>().Select(node => node.Value)).Trim();
        }

        private static string Label(XElement element, int? position)
        {
            string label = element.Name.LocalName;
            string? identity = Identity(element);
            if (identity != null)
            {
                int split = identity.IndexOf('=');
                return label + "[@" + identity.Substring(0, split) + "=\"" + identity.Substring(split + 1) + "\"]";
            }

            if (position != null)
            {
                return label + "[" + position.Value.ToString(CultureInfo.InvariantCulture) + "]";
            }

            return label;
        }

        private static string Describe(string parentPath, XElement element)
        {
            string path = parentPath.Length == 0 ? Label(element, null) : parentPath + "/" + Label(element, null);
            string name = ChildText(element, "Name");
            if (name.Length > 0)
            {
                path += " " + name;
            }

            int children = element.Elements().Count();
            if (children > 0)
            {
                path += " (" + children.ToString(CultureInfo.InvariantCulture) + " nested)";
            }

            return path;
        }

        private static bool SameName(XElement left, XElement right)
        {
            return left.Name.LocalName == right.Name.LocalName;
        }

        private static bool TryParse(string? text, out XElement? root)
        {
            root = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text!.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '<')
            {
                return false;
            }

            try
            {
                root = XDocument.Parse(text, LoadOptions.None).Root;
                return root != null;
            }
            catch (System.Xml.XmlException)
            {
                return false;
            }
        }

        private static void Emit(List<TextDiffLine> lines, Budget budget, TextDiffLineKind kind, string text)
        {
            if (budget.Stopped)
            {
                return;
            }

            if (lines.Count >= MaximumChanges)
            {
                budget.Stopped = true;
                return;
            }

            lines.Add(new TextDiffLine(kind, null, null, text));
        }

        private sealed class Budget
        {
            public bool Stopped { get; set; }
        }
    }
}
