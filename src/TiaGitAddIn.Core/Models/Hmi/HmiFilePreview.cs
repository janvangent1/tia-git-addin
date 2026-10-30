using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TiaGitAddIn.Models.Hmi
{
    public sealed class HmiScreenItemPreview
    {
        public HmiScreenItemPreview(
            string kind,
            string name,
            string label,
            int left,
            int top,
            int width,
            int height,
            string change,
            string links = "",
            string detail = "")
        {
            Kind = kind ?? string.Empty;
            Name = name ?? string.Empty;
            Label = label ?? string.Empty;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Change = change ?? "Unchanged";
            Links = links ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public string Kind { get; }
        public string Name { get; }
        public string Label { get; }
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public string Change { get; }

        /// <summary>Tag names and screen links used by this button or I/O field, one per line.</summary>
        public string Links { get; }

        /// <summary>Why a changed item was marked, when the visible text and position stayed the same.</summary>
        public string Detail { get; }
    }

    public sealed class HmiScreenSidePreview
    {
        public HmiScreenSidePreview(string name, int width, int height, IReadOnlyList<HmiScreenItemPreview> items)
        {
            Name = name ?? string.Empty;
            Width = width;
            Height = height;
            Items = items ?? Array.Empty<HmiScreenItemPreview>();
        }

        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<HmiScreenItemPreview> Items { get; }
    }

    public sealed class HmiTagPreviewRow
    {
        public HmiTagPreviewRow(string name, string address, string dataType, string comment, string change,
            string retain = "", string defaultValue = "")
        {
            Name = name ?? string.Empty;
            Address = address ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Comment = comment ?? string.Empty;
            Change = change ?? "Unchanged";
            Retain = retain ?? string.Empty;
            DefaultValue = defaultValue ?? string.Empty;
        }

        public string Name { get; }
        public string Address { get; }
        public string DataType { get; }
        public string Comment { get; }
        public string Change { get; }
        public string Retain { get; }
        public string DefaultValue { get; }
    }

    public sealed class HmiFilePreview
    {
        private HmiFilePreview(
            HmiScreenSidePreview? leftScreen,
            HmiScreenSidePreview? rightScreen,
            IReadOnlyList<HmiTagPreviewRow> tags,
            bool isPlcTable = false)
        {
            LeftScreen = leftScreen;
            RightScreen = rightScreen;
            Tags = tags ?? Array.Empty<HmiTagPreviewRow>();
            IsPlcTable = isPlcTable;
        }

        public HmiScreenSidePreview? LeftScreen { get; }
        public HmiScreenSidePreview? RightScreen { get; }
        public IReadOnlyList<HmiTagPreviewRow> Tags { get; }
        public bool IsScreen => LeftScreen != null || RightScreen != null;
        public bool IsTagTable => Tags.Count > 0;
        public bool IsPlcTable { get; }

        /// <summary>
        /// Drops a PLC tag table or data type grid when Settings asks for the XML text.
        /// </summary>
        public static HmiFilePreview? ApplyPlcChoice(HmiFilePreview? preview, bool showPlcTable)
        {
            if (preview == null || showPlcTable || !preview.IsPlcTable)
            {
                return preview;
            }

            return null;
        }

        /// <summary>
        /// Drops a screen drawing when Settings asks for the XML text. A tag table is left as it is.
        /// </summary>
        public static HmiFilePreview? ApplyScreenChoice(HmiFilePreview? preview, bool showScreenPreview)
        {
            if (preview == null || showScreenPreview || !preview.IsScreen)
            {
                return preview;
            }

            return null;
        }

        public static bool TryCreate(string leftText, string rightText, out HmiFilePreview? preview)
        {
            HmiScreenSidePreview? leftScreen = ReadScreen(leftText);
            HmiScreenSidePreview? rightScreen = ReadScreen(rightText);
            if (leftScreen != null || rightScreen != null)
            {
                IReadOnlyList<HmiScreenItemPreview> leftItems = leftScreen == null
                    ? Array.Empty<HmiScreenItemPreview>()
                    : CompareItems(leftScreen.Items, rightScreen == null ? null : rightScreen.Items, false);
                IReadOnlyList<HmiScreenItemPreview> rightItems = rightScreen == null
                    ? Array.Empty<HmiScreenItemPreview>()
                    : CompareItems(leftScreen == null ? null : leftScreen.Items, rightScreen.Items, true);
                preview = new HmiFilePreview(
                    leftScreen == null ? null : new HmiScreenSidePreview(leftScreen.Name, leftScreen.Width, leftScreen.Height, leftItems),
                    rightScreen == null ? null : new HmiScreenSidePreview(rightScreen.Name, rightScreen.Width, rightScreen.Height, rightItems),
                    Array.Empty<HmiTagPreviewRow>());
                return true;
            }

            IReadOnlyList<HmiTagPreviewRow> tags = CompareTags(ReadTags(leftText), ReadTags(rightText));
            if (tags.Count > 0)
            {
                preview = new HmiFilePreview(null, null, tags);
                return true;
            }

            IReadOnlyList<HmiTagPreviewRow> plc = CompareTags(ReadPlcTable(leftText), ReadPlcTable(rightText));
            if (plc.Count == 0)
            {
                preview = null;
                return false;
            }

            preview = new HmiFilePreview(null, null, plc, true);
            return true;
        }

        private static HmiScreenSidePreview? ReadScreen(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf('<') < 0)
            {
                return null;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }

            XElement root = document.Root;
            if (root == null)
            {
                return null;
            }

            if (root.Name.LocalName.Equals("Screen", StringComparison.OrdinalIgnoreCase))
            {
                var items = new List<HmiScreenItemPreview>();
                foreach (XElement item in root.Elements())
                {
                    if (!item.Name.LocalName.Equals("Item", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(ReadUnifiedItem(item));
                }

                return new HmiScreenSidePreview(
                    Attribute(root, "name"),
                    Number(root, "width"),
                    Number(root, "height"),
                    items);
            }

            var widgets = new List<HmiScreenItemPreview>();
            int width = 0;
            int height = 0;
            string name = string.Empty;
            foreach (XElement element in root.DescendantsAndSelf())
            {
                string local = element.Name.LocalName;
                if (local.IndexOf("Screen", StringComparison.OrdinalIgnoreCase) >= 0 && width == 0)
                {
                    width = Number(element, "Width");
                    height = Number(element, "Height");
                    string screenName = TextOf(element, "Name");
                    if (screenName.Length > 0)
                    {
                        name = screenName;
                    }
                }

                if (!IsButtonOrIoField(local))
                {
                    continue;
                }

                widgets.Add(ReadWidget(element, local));
            }

            if (widgets.Count == 0)
            {
                return null;
            }

            if (width <= 0)
            {
                width = widgets.Max(item => item.Left + item.Width) + 20;
            }

            if (height <= 0)
            {
                height = widgets.Max(item => item.Top + item.Height) + 20;
            }

            return new HmiScreenSidePreview(name, width, height, widgets);
        }

        private static HmiScreenItemPreview ReadUnifiedItem(XElement item)
        {
            string label = FirstText(item);
            string name = Attribute(item, "name");
            string shown = label.Length == 0 ? Attribute(item, "processValue") : label;
            return new HmiScreenItemPreview(
                Attribute(item, "kind"),
                name,
                shown,
                Number(item, "left"),
                Number(item, "top"),
                Number(item, "width"),
                Number(item, "height"),
                "Unchanged",
                CollectLinks(item, name, shown));
        }

        private static HmiScreenItemPreview ReadWidget(XElement element, string localName)
        {
            string kind = localName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0 ? "Button" : "IOField";
            string name = TextOf(element, "ObjectName");
            if (name.Length == 0)
            {
                name = TextOf(element, "Name");
            }

            string label = TextOf(element, "Text");
            return new HmiScreenItemPreview(
                kind,
                name,
                label,
                Number(element, "Left"),
                Number(element, "Top"),
                Number(element, "Width"),
                Number(element, "Height"),
                "Unchanged",
                CollectLinks(element, name, label));
        }

        // Names stored on a link (the tag a button sets, the screen it opens) are not the button text.
        // A change there still belongs to that button.
        private static string CollectLinks(XElement element, string ownName, string ownLabel)
        {
            var links = new List<string>();
            foreach (XElement node in element.DescendantsAndSelf())
            {
                foreach (XAttribute attribute in node.Attributes())
                {
                    TryAddLink(links, attribute.Name.LocalName, attribute.Value, ownName, ownLabel);
                }

                if (node.HasElements)
                {
                    continue;
                }

                string local = node.Name.LocalName;
                bool linkedName = local.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    && node.Parent != null
                    && IsLinkParent(node.Parent.Name.LocalName);
                if (linkedName)
                {
                    TryAddLink(links, "Name", node.Value, ownName, ownLabel);
                    continue;
                }

                TryAddLink(links, local, node.Value, ownName, ownLabel);
            }

            links.Sort(StringComparer.Ordinal);
            return string.Join("\n", links);
        }

        private static bool IsLinkParent(string localName)
        {
            return localName.Equals("Value", StringComparison.OrdinalIgnoreCase)
                || localName.IndexOf("Link", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("Process", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void TryAddLink(List<string> links, string localName, string value, string ownName, string ownLabel)
        {
            if (!IsLinkField(localName))
            {
                return;
            }

            string text = (value ?? string.Empty).Trim();
            if (text.Length == 0
                || text.Equals(ownName, StringComparison.Ordinal)
                || text.Equals(ownLabel, StringComparison.Ordinal))
            {
                return;
            }

            string line = text;
            if (!links.Contains(line))
            {
                links.Add(line);
            }
        }

        private static bool IsLinkField(string localName)
        {
            return localName.Equals("Name", StringComparison.OrdinalIgnoreCase)
                || localName.IndexOf("Tag", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("Process", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("Variable", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.Equals("ScreenName", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsButtonOrIoField(string localName)
        {
            return localName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("IOField", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("IO-Field", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("I/O field", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static List<TagDraft> ReadPlcTable(string text)
        {
            var rows = new List<TagDraft>();
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("<", StringComparison.Ordinal) < 0)
            {
                return rows;
            }

            if (text.IndexOf("PlcTag", StringComparison.OrdinalIgnoreCase) < 0
                && text.IndexOf("PlcStruct", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return rows;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(text);
            }
            catch (System.Xml.XmlException)
            {
                return rows;
            }

            if (document.Root == null)
            {
                return rows;
            }

            foreach (XElement element in document.Root.Descendants())
            {
                string local = element.Name.LocalName;
                if (local.Equals("PlcTag", StringComparison.OrdinalIgnoreCase)
                    || local.EndsWith(".PlcTag", StringComparison.OrdinalIgnoreCase))
                {
                    string name = TextOf(element, "Name");
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    rows.Add(new TagDraft(
                        name,
                        TextOf(element, "LogicalAddress"),
                        FirstNonEmpty(TextOf(element, "DataTypeName"), TextOf(element, "DataType")),
                        CommentOf(element),
                        RetainOf(element),
                        TextOf(element, "StartValue")));
                    continue;
                }

                if (!local.Equals("Member", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string memberName = Attribute(element, "Name");
                string dataType = Attribute(element, "Datatype");
                if (memberName.Length == 0 || dataType.Length == 0)
                {
                    continue;
                }

                rows.Add(new TagDraft(
                    memberName,
                    string.Empty,
                    dataType,
                    CommentOf(element),
                    string.Empty,
                    FirstNonEmpty(Attribute(element, "StartValue"), TextOf(element, "StartValue"))));
            }

            return rows;
        }

        private static string CommentOf(XElement element)
        {
            string direct = TextOf(element, "Comment");
            if (direct.Length > 0)
            {
                return direct;
            }

            foreach (XElement child in element.Descendants())
            {
                string local = child.Name.LocalName;
                if (!local.Equals("MultiLanguageText", StringComparison.OrdinalIgnoreCase)
                    && !local.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (child.HasElements)
                {
                    continue;
                }

                string value = (child.Value ?? string.Empty).Trim();
                if (value.Length > 0)
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string RetainOf(XElement element)
        {
            string value = TextOf(element, "Retain");
            if (value.Length == 0)
            {
                foreach (XElement child in element.Elements())
                {
                    if (child.Name.LocalName.IndexOf("Attribute", StringComparison.OrdinalIgnoreCase) >= 0
                        && Attribute(child, "Name").Equals("Retain", StringComparison.OrdinalIgnoreCase))
                    {
                        value = (child.Value ?? string.Empty).Trim();
                        break;
                    }
                }
            }

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return "Yes";
            }

            if (value.Equals("false", StringComparison.OrdinalIgnoreCase) || value.Length == 0)
            {
                return string.Empty;
            }

            return value;
        }

        private static List<TagDraft> ReadTags(string text)
        {
            var tags = new List<TagDraft>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return tags;
            }

            if (text.IndexOf('<') >= 0)
            {
                try
                {
                    XDocument document = XDocument.Parse(text);
                    if (document.Root != null)
                    {
                        foreach (XElement element in document.Root.DescendantsAndSelf())
                        {
                            string local = element.Name.LocalName;
                            if (!local.Equals("Tag", StringComparison.OrdinalIgnoreCase)
                                && !local.EndsWith(".Tag", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            string name = TextOf(element, "Name");
                            if (name.Length == 0)
                            {
                                continue;
                            }

                            tags.Add(new TagDraft(
                                name,
                                FirstNonEmpty(TextOf(element, "LogicalAddress"), TextOf(element, "Address")),
                                FirstNonEmpty(TextOf(element, "DataType"), TextOf(element, "DataTypeName")),
                                TextOf(element, "Comment")));
                        }
                    }
                }
                catch (System.Xml.XmlException)
                {
                }
            }

            if (tags.Count == 0)
            {
                ReadYamlTags(text, tags);
            }

            return tags;
        }

        private static void ReadYamlTags(string text, List<TagDraft> tags)
        {
            if (text.IndexOf("Name:", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            string name = string.Empty;
            string address = string.Empty;
            string dataType = string.Empty;
            string comment = string.Empty;
            bool seen = false;
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.Trim().TrimStart('-').Trim();
                if (line.Length == 0)
                {
                    FlushTag(tags, ref name, ref address, ref dataType, ref comment, ref seen);
                    continue;
                }

                int colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, colon).Trim();
                string value = line.Substring(colon + 1).Trim().Trim('"');
                if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                {
                    FlushTag(tags, ref name, ref address, ref dataType, ref comment, ref seen);
                    name = value;
                    seen = true;
                }
                else if (key.Equals("LogicalAddress", StringComparison.OrdinalIgnoreCase) || key.Equals("Address", StringComparison.OrdinalIgnoreCase))
                {
                    address = value;
                }
                else if (key.Equals("DataType", StringComparison.OrdinalIgnoreCase) || key.Equals("DataTypeName", StringComparison.OrdinalIgnoreCase))
                {
                    dataType = value;
                }
                else if (key.Equals("Comment", StringComparison.OrdinalIgnoreCase))
                {
                    comment = value;
                }
            }

            FlushTag(tags, ref name, ref address, ref dataType, ref comment, ref seen);
        }

        private static void FlushTag(List<TagDraft> tags, ref string name, ref string address, ref string dataType, ref string comment, ref bool seen)
        {
            if (seen && name.Length > 0)
            {
                tags.Add(new TagDraft(name, address, dataType, comment));
            }

            name = string.Empty;
            address = string.Empty;
            dataType = string.Empty;
            comment = string.Empty;
            seen = false;
        }

        private static IReadOnlyList<HmiTagPreviewRow> CompareTags(List<TagDraft> left, List<TagDraft> right)
        {
            var rows = new List<HmiTagPreviewRow>();
            var rightByName = new Dictionary<string, TagDraft>(StringComparer.OrdinalIgnoreCase);
            foreach (TagDraft tag in right)
            {
                if (!rightByName.ContainsKey(tag.Name))
                {
                    rightByName.Add(tag.Name, tag);
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (TagDraft tag in left)
            {
                seen.Add(tag.Name);
                if (!rightByName.TryGetValue(tag.Name, out TagDraft match))
                {
                    rows.Add(new HmiTagPreviewRow(tag.Name, tag.Address, tag.DataType, tag.Comment, "Removed", tag.Retain, tag.DefaultValue));
                    continue;
                }

                bool addressSame = tag.Address == match.Address;
                bool typeSame = tag.DataType == match.DataType;
                bool commentSame = tag.Comment == match.Comment;
                bool retainSame = tag.Retain == match.Retain;
                bool defaultSame = tag.DefaultValue == match.DefaultValue;
                bool same = addressSame && typeSame && commentSame && retainSame && defaultSame;
                rows.Add(new HmiTagPreviewRow(
                    tag.Name,
                    addressSame ? tag.Address : tag.Address + " → " + match.Address,
                    typeSame ? tag.DataType : tag.DataType + " → " + match.DataType,
                    commentSame ? tag.Comment : tag.Comment + " → " + match.Comment,
                    same ? "Unchanged" : "Changed",
                    retainSame ? tag.Retain : tag.Retain + " → " + match.Retain,
                    defaultSame ? tag.DefaultValue : tag.DefaultValue + " → " + match.DefaultValue));
            }

            foreach (TagDraft tag in right)
            {
                if (!seen.Contains(tag.Name))
                {
                    rows.Add(new HmiTagPreviewRow(tag.Name, tag.Address, tag.DataType, tag.Comment, "Added", tag.Retain, tag.DefaultValue));
                }
            }

            return rows;
        }

        internal static IReadOnlyList<HmiScreenItemPreview> CompareItems(
            IReadOnlyList<HmiScreenItemPreview>? left,
            IReadOnlyList<HmiScreenItemPreview>? right,
            bool markRight)
        {
            var rightByKey = new Dictionary<string, HmiScreenItemPreview>(StringComparer.OrdinalIgnoreCase);
            foreach (HmiScreenItemPreview item in right ?? Array.Empty<HmiScreenItemPreview>())
            {
                string key = ItemKey(item);
                if (!rightByKey.ContainsKey(key))
                {
                    rightByKey.Add(key, item);
                }
            }

            var leftByKey = new Dictionary<string, HmiScreenItemPreview>(StringComparer.OrdinalIgnoreCase);
            foreach (HmiScreenItemPreview item in left ?? Array.Empty<HmiScreenItemPreview>())
            {
                string key = ItemKey(item);
                if (!leftByKey.ContainsKey(key))
                {
                    leftByKey.Add(key, item);
                }
            }

            IReadOnlyList<HmiScreenItemPreview>? source = markRight ? right : left;
            var result = new List<HmiScreenItemPreview>();
            foreach (HmiScreenItemPreview item in source ?? Array.Empty<HmiScreenItemPreview>())
            {
                string key = ItemKey(item);
                Dictionary<string, HmiScreenItemPreview> other = markRight ? leftByKey : rightByKey;
                string change;
                string detail = string.Empty;
                if (!other.TryGetValue(key, out HmiScreenItemPreview? match) || match == null)
                {
                    change = markRight ? "Added" : "Removed";
                }
                else
                {
                    HmiScreenItemPreview previous = markRight ? match : item;
                    HmiScreenItemPreview current = markRight ? item : match;
                    detail = DescribeChange(previous, current);
                    change = detail.Length == 0 ? "Unchanged" : "Changed";
                }

                result.Add(new HmiScreenItemPreview(
                    item.Kind,
                    item.Name,
                    item.Label,
                    item.Left,
                    item.Top,
                    item.Width,
                    item.Height,
                    change,
                    item.Links,
                    detail));
            }

            return result;
        }

        private static string DescribeChange(HmiScreenItemPreview previous, HmiScreenItemPreview current)
        {
            var notes = new List<string>();
            if (previous.Label != current.Label)
            {
                notes.Add("Caption: " + Show(previous.Label) + " → " + Show(current.Label));
            }

            if (previous.Left != current.Left || previous.Top != current.Top)
            {
                notes.Add("Position: " + previous.Left + ", " + previous.Top + " → " + current.Left + ", " + current.Top);
            }

            if (previous.Width != current.Width || previous.Height != current.Height)
            {
                notes.Add("Size: " + previous.Width + " × " + previous.Height + " → " + current.Width + " × " + current.Height);
            }

            if (!string.Equals(previous.Links, current.Links, StringComparison.Ordinal))
            {
                DescribeLinks(notes, previous.Links, current.Links);
            }

            return string.Join(Environment.NewLine, notes);
        }

        private static void DescribeLinks(List<string> notes, string previous, string current)
        {
            string[] before = SplitLinks(previous);
            string[] after = SplitLinks(current);
            var afterSet = new HashSet<string>(after, StringComparer.Ordinal);
            var beforeSet = new HashSet<string>(before, StringComparer.Ordinal);
            foreach (string link in before)
            {
                if (!afterSet.Contains(link))
                {
                    notes.Add("Tag or link removed: " + link);
                }
            }

            foreach (string link in after)
            {
                if (!beforeSet.Contains(link))
                {
                    notes.Add("Tag or link added: " + link);
                }
            }
        }

        private static string[] SplitLinks(string text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Show(string text)
        {
            return string.IsNullOrWhiteSpace(text) ? "(none)" : text.Trim();
        }

        private static string ItemKey(HmiScreenItemPreview item)
        {
            if (!string.IsNullOrWhiteSpace(item.Name))
            {
                return item.Name;
            }

            return item.Kind + ":" + item.Left.ToString(CultureInfo.InvariantCulture) + ":" + item.Top.ToString(CultureInfo.InvariantCulture);
        }

        private static string Attribute(XElement element, string name)
        {
            foreach (XAttribute attribute in element.Attributes())
            {
                if (attribute.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return attribute.Value ?? string.Empty;
                }
            }

            return string.Empty;
        }

        private static int Number(XElement element, string name)
        {
            string text = Attribute(element, name);
            if (text.Length == 0)
            {
                text = TextOf(element, name);
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }

        private static string TextOf(XElement element, string name)
        {
            string attribute = Attribute(element, name);
            if (attribute.Length > 0)
            {
                return attribute;
            }

            foreach (XElement child in element.DescendantsAndSelf())
            {
                if (child.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase) && !child.HasElements)
                {
                    return (child.Value ?? string.Empty).Trim();
                }
            }

            return string.Empty;
        }

        private static string FirstText(XElement item)
        {
            foreach (XElement child in item.Elements())
            {
                if (child.Name.LocalName.Equals("Text", StringComparison.OrdinalIgnoreCase))
                {
                    return (child.Value ?? string.Empty).Trim();
                }
            }

            return string.Empty;
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return first.Length > 0 ? first : second;
        }

        private sealed class TagDraft
        {
            public TagDraft(string name, string address, string dataType, string comment, string retain = "", string defaultValue = "")
            {
                Name = name ?? string.Empty;
                Address = address ?? string.Empty;
                DataType = dataType ?? string.Empty;
                Comment = comment ?? string.Empty;
                Retain = retain ?? string.Empty;
                DefaultValue = defaultValue ?? string.Empty;
            }

            public string Name { get; }
            public string Address { get; }
            public string DataType { get; }
            public string Comment { get; }
            public string Retain { get; }
            public string DefaultValue { get; }
        }
    }
}
