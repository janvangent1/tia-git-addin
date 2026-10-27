using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;
using Siemens.Engineering;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Copies the attributes, events, dynamizations, and nested objects Openness exposes for a Unified screen or widget.
    /// </summary>
    internal static class UnifiedObjectSnapshot
    {
        private const int MaxDepth = 5;
        private const int MaxObjects = 800;
        private const int MaxChildren = 300;
        private const int MaxText = 100000;

        public static XElement Capture(object target, bool skipScreenItems)
        {
            var details = new XElement("Details");
            if (target == null)
            {
                return details;
            }

            details.SetAttributeValue("type", target.GetType().Name);
            var state = new State();
            try
            {
                Fill(details, target, MaxDepth, skipScreenItems, state);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            if (state.Truncated)
            {
                details.SetAttributeValue("truncated", "true");
            }

            return details;
        }

        private static void Fill(XElement parent, object target, int depth, bool skipScreenItems, State state)
        {
            if (target == null)
            {
                return;
            }

            if (!state.TryEnter(target, out bool cycle))
            {
                if (cycle)
                {
                    parent.SetAttributeValue("cycle", "true");
                }

                return;
            }

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (target is IEngineeringObject engineering)
            {
                WriteAttributes(parent, engineering, written);
                WriteCompositions(parent, engineering, depth, skipScreenItems, written, state);
            }

            WriteProperties(parent, target, depth, skipScreenItems, written, state);
        }

        private static void WriteAttributes(XElement parent, IEngineeringObject engineering, ISet<string> written)
        {
            WriteAccess(parent, engineering, written, AttributeAccessOptions.ReadWrite);
            WriteAccess(parent, engineering, written, AttributeAccessOptions.ReadOnly);
        }

        private static void WriteAccess(XElement parent, IEngineeringObject engineering, ISet<string> written, AttributeAccessOptions access)
        {
            IEnumerable attributes;
            try
            {
                attributes = engineering.GetAttributes(access);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (attributes == null)
            {
                return;
            }

            foreach (object entry in attributes)
            {
                if (!(entry is KeyValuePair<string, object> attribute) || string.IsNullOrWhiteSpace(attribute.Key) || !written.Add(attribute.Key))
                {
                    continue;
                }

                if (SkipValue(attribute.Key, attribute.Value))
                {
                    continue;
                }

                parent.Add(AttributeElement(attribute.Key, attribute.Value));
            }
        }

        private static void WriteCompositions(
            XElement parent,
            IEngineeringObject engineering,
            int depth,
            bool skipScreenItems,
            ISet<string> written,
            State state)
        {
            if (depth <= 0)
            {
                return;
            }

            IEnumerable<EngineeringCompositionInfo> infos;
            try
            {
                infos = engineering.GetCompositionInfos();
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            if (infos == null)
            {
                return;
            }

            foreach (EngineeringCompositionInfo info in infos)
            {
                if (info == null || string.IsNullOrWhiteSpace(info.Name) || !written.Add(info.Name))
                {
                    continue;
                }

                if (skipScreenItems && info.Name.Equals("ScreenItems", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IEngineeringCompositionOrObject composition;
                try
                {
                    composition = engineering.GetComposition(info.Name);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    continue;
                }

                if (composition is IEnumerable items)
                {
                    AddChildren(parent, info.Name, items, depth, state);
                }
            }
        }

        private static void WriteProperties(
            XElement parent,
            object target,
            int depth,
            bool skipScreenItems,
            ISet<string> written,
            State state)
        {
            PropertyInfo[] properties;
            try
            {
                properties = target.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return;
            }

            foreach (PropertyInfo property in properties)
            {
                if (property == null || property.GetIndexParameters().Length > 0 || string.IsNullOrWhiteSpace(property.Name))
                {
                    continue;
                }

                if (property.Name.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                    || property.Name.Equals("SyncRoot", StringComparison.OrdinalIgnoreCase)
                    || (skipScreenItems && property.Name.Equals("ScreenItems", StringComparison.OrdinalIgnoreCase))
                    || !written.Add(property.Name))
                {
                    continue;
                }

                object value;
                try
                {
                    value = property.GetValue(target);
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                    continue;
                }

                if (value == null || value is byte[])
                {
                    continue;
                }

                if (value is string)
                {
                    if (!SkipValue(property.Name, value))
                    {
                        parent.Add(AttributeElement(property.Name, value));
                    }

                    continue;
                }

                if (value is IEnumerable items)
                {
                    AddChildren(parent, property.Name, items, depth, state);
                    continue;
                }

                if (!WalkObject(value))
                {
                    if (!SkipValue(property.Name, value))
                    {
                        parent.Add(AttributeElement(property.Name, value));
                    }

                    continue;
                }

                if (depth <= 0)
                {
                    continue;
                }

                var child = new XElement(
                    "Object",
                    new XAttribute("type", value.GetType().Name),
                    new XAttribute("property", property.Name));
                string name = ReadName(value);
                if (name.Length > 0)
                {
                    child.SetAttributeValue("name", name);
                }

                Fill(child, value, depth - 1, false, state);
                parent.Add(child);
            }
        }

        private static void AddChildren(XElement parent, string name, IEnumerable items, int depth, State state)
        {
            if (items == null || depth <= 0)
            {
                return;
            }

            var list = new XElement("Composition", new XAttribute("name", name));
            int count = 0;
            foreach (object child in items)
            {
                if (child == null || child is string)
                {
                    continue;
                }

                if (count >= MaxChildren || state.Remaining <= 0)
                {
                    list.SetAttributeValue("truncated", "true");
                    state.Truncated = true;
                    break;
                }

                count++;
                var childElement = new XElement("Object", new XAttribute("type", child.GetType().Name));
                string childName = ReadName(child);
                if (childName.Length > 0)
                {
                    childElement.SetAttributeValue("name", childName);
                }

                if (!WalkObject(child))
                {
                    childElement.SetAttributeValue("value", Text(child));
                }
                else
                {
                    Fill(childElement, child, depth - 1, false, state);
                }

                list.Add(childElement);
            }

            if (list.HasElements || list.Attribute("truncated") != null)
            {
                parent.Add(list);
            }
        }

        private static XElement AttributeElement(string name, object value)
        {
            return new XElement(
                "Attribute",
                new XAttribute("name", name),
                new XAttribute("value", Text(value)));
        }

        private static bool SkipValue(string name, object value)
        {
            if (name.Equals("Parent", StringComparison.OrdinalIgnoreCase)
                || name.Equals("ScreenItems", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (value == null || value is byte[])
            {
                return true;
            }

            if (value is string text)
            {
                return text.Length == 0;
            }

            return value is IEnumerable;
        }

        private static bool IsSimple(Type type)
        {
            type = Nullable.GetUnderlyingType(type) ?? type;
            return type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime);
        }

        private static bool IsSystemLeaf(object value)
        {
            string space = value.GetType().Namespace ?? string.Empty;
            return space.Equals("System", StringComparison.Ordinal)
                || space.StartsWith("System.", StringComparison.Ordinal);
        }

        private static bool StayInside(object value)
        {
            string space = value.GetType().Namespace ?? string.Empty;
            return space.StartsWith("Siemens.Engineering.Hmi", StringComparison.Ordinal);
        }

        private static bool WalkObject(object value)
        {
            if (value == null || IsSimple(value.GetType()) || IsSystemLeaf(value) || !StayInside(value))
            {
                return false;
            }

            string name = value.GetType().Name;
            return name.IndexOf("Software", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Project", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("Device", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("ScreenGroup", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("TagTable", StringComparison.OrdinalIgnoreCase) < 0
                && !name.Equals("HmiScreen", StringComparison.Ordinal);
        }

        private static string ReadName(object target)
        {
            if (target is IEngineeringObject engineering)
            {
                try
                {
                    object named = engineering.GetAttribute("Name");
                    if (named is string text && text.Length > 0)
                    {
                        return Clean(text);
                    }
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            try
            {
                PropertyInfo property = target.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                if (property != null && property.PropertyType == typeof(string))
                {
                    return Clean(property.GetValue(target) as string ?? string.Empty);
                }
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }

            return string.Empty;
        }

        private static string Text(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string text)
            {
                return Cap(Clean(text));
            }

            string rendered;
            try
            {
                rendered = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                rendered = value.ToString() ?? string.Empty;
            }

            return Cap(Clean(rendered));
        }

        private static string Cap(string value)
        {
            if (value.Length <= MaxText)
            {
                return value;
            }

            return value.Substring(0, MaxText);
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                if (character == '\t' || character == '\n' || character == '\r' || (character >= ' ' && character != '\uFFFE' && character != '\uFFFF'))
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }

        private static bool IsSessionDead(Exception ex)
        {
            return ExportIssues.MustStop(ex);
        }

        private sealed class State
        {
            private readonly HashSet<object> seen = new HashSet<object>(ReferenceComparer.Instance);

            public int Remaining { get; private set; } = MaxObjects;

            public bool Truncated { get; set; }

            public bool TryEnter(object target, out bool cycle)
            {
                cycle = false;
                if (!seen.Add(target))
                {
                    cycle = true;
                    return false;
                }

                if (Remaining <= 0)
                {
                    Truncated = true;
                    return false;
                }

                Remaining--;
                return true;
            }
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            bool IEqualityComparer<object>.Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            int IEqualityComparer<object>.GetHashCode(object obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
