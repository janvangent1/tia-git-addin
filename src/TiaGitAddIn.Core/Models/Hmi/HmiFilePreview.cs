using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace TiaGitAddIn.Models.Hmi
{
    public sealed class HmiScreenItemPreview
    {
        public HmiScreenItemPreview(string kind, string name, string label, int left, int top, int width, int height, string change)
        {
            Kind = kind ?? string.Empty;
            Name = name ?? string.Empty;
            Label = label ?? string.Empty;
            Left = left;
            Top = top;
            Width = width;
            Height = height;
            Change = change ?? "Unchanged";
        }

        public string Kind { get; }
        public string Name { get; }
        public string Label { get; }
        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public string Change { get; }
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
        public HmiTagPreviewRow(string name, string address, string dataType, string comment, string change)
        {
            Name = name ?? string.Empty;
            Address = address ?? string.Empty;
            DataType = dataType ?? string.Empty;
            Comment = comment ?? string.Empty;
            Change = change ?? "Unchanged";
        }

        public string Name { get; }
        public string Address { get; }
        public string DataType { get; }
        public string Comment { get; }
        public string Change { get; }
    }

    public sealed class HmiFilePreview
    {
        private HmiFilePreview(
            HmiScreenSidePreview? leftScreen,
            HmiScreenSidePreview? rightScreen,
            IReadOnlyList<HmiTagPreviewRow> tags)
        {
            LeftScreen = leftScreen;
            RightScreen = rightScreen;
            Tags = tags ?? Array.Empty<HmiTagPreviewRow>();
        }

        public HmiScreenSidePreview? LeftScreen { get; }
        public HmiScreenSidePreview? RightScreen { get; }
        public IReadOnlyList<HmiTagPreviewRow> Tags { get; }
        public bool IsScreen => LeftScreen != null || RightScreen != null;
        public bool IsTagTable => Tags.Count > 0;

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
            if (tags.Count == 0)
            {
                preview = null;
                return false;
            }

            preview = new HmiFilePreview(null, null, tags);
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
            return new HmiScreenItemPreview(
                Attribute(item, "kind"),
                Attribute(item, "name"),
                label.Length == 0 ? Attribute(item, "processValue") : label,
                Number(item, "left"),
                Number(item, "top"),
                Number(item, "width"),
                Number(item, "height"),
                "Unchanged");
        }

        private static HmiScreenItemPreview ReadWidget(XElement element, string localName)
        {
            string kind = localName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0 ? "Button" : "IOField";
            string name = TextOf(element, "ObjectName");
            if (name.Length == 0)
            {
                name = TextOf(element, "Name");
            }

            return new HmiScreenItemPreview(
                kind,
                name,
                TextOf(element, "Text"),
                Number(element, "Left"),
                Number(element, "Top"),
                Number(element, "Width"),
                Number(element, "Height"),
                "Unchanged");
        }

        private static bool IsButtonOrIoField(string localName)
        {
            return localName.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("IOField", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("IO-Field", StringComparison.OrdinalIgnoreCase) >= 0
                || localName.IndexOf("I/O field", StringComparison.OrdinalIgnoreCase) >= 0;
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
                    rows.Add(new HmiTagPreviewRow(tag.Name, tag.Address, tag.DataType, tag.Comment, "Removed"));
                    continue;
                }

                bool addressSame = tag.Address == match.Address;
                bool typeSame = tag.DataType == match.DataType;
                bool commentSame = tag.Comment == match.Comment;
                bool same = addressSame && typeSame && commentSame;
                rows.Add(new HmiTagPreviewRow(
                    tag.Name,
                    addressSame ? tag.Address : tag.Address + " → " + match.Address,
                    typeSame ? tag.DataType : tag.DataType + " → " + match.DataType,
                    commentSame ? tag.Comment : tag.Comment + " → " + match.Comment,
                    same ? "Unchanged" : "Changed"));
            }

            foreach (TagDraft tag in right)
            {
                if (!seen.Contains(tag.Name))
                {
                    rows.Add(new HmiTagPreviewRow(tag.Name, tag.Address, tag.DataType, tag.Comment, "Added"));
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
                if (!other.TryGetValue(key, out HmiScreenItemPreview? match) || match == null)
                {
                    change = markRight ? "Added" : "Removed";
                }
                else if (item.Left != match.Left || item.Top != match.Top || item.Width != match.Width || item.Height != match.Height || item.Label != match.Label)
                {
                    change = "Changed";
                }
                else
                {
                    change = "Unchanged";
                }

                result.Add(new HmiScreenItemPreview(item.Kind, item.Name, item.Label, item.Left, item.Top, item.Width, item.Height, change));
            }

            return result;
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
            public TagDraft(string name, string address, string dataType, string comment)
            {
                Name = name ?? string.Empty;
                Address = address ?? string.Empty;
                DataType = dataType ?? string.Empty;
                Comment = comment ?? string.Empty;
            }

            public string Name { get; }
            public string Address { get; }
            public string DataType { get; }
            public string Comment { get; }
        }
    }
}
