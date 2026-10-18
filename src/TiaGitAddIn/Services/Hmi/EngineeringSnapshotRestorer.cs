using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using Siemens.Engineering;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Writes an exported attribute snapshot back onto the matching Openness objects.
    /// </summary>
    internal static class EngineeringSnapshotRestorer
    {
        public static int ApplyElement(IEngineeringObject target, XElement element)
        {
            if (target == null || element == null)
            {
                return 0;
            }

            int applied = 0;
            foreach (XElement attribute in element.Elements("Attribute"))
            {
                applied += ApplyAttribute(target, attribute);
            }

            foreach (XElement composition in element.Elements("Composition"))
            {
                applied += ApplyComposition(target, AttributeValue(composition, "name"), composition.Elements().Where(IsChild), false);
            }

            foreach (XElement items in element.Elements("Items"))
            {
                applied += ApplyComposition(target, AttributeValue(items, "composition"), items.Elements("Item"), false);
            }

            foreach (XElement child in element.Elements("Object"))
            {
                string property = AttributeValue(child, "property");
                if (property.Length == 0)
                {
                    continue;
                }

                object current = ReadAttribute(target, property);
                if (current is IEngineeringObject nested)
                {
                    applied += ApplyElement(nested, child);
                }
            }

            return applied;
        }

        public static int ApplyComposition(IEngineeringObject owner, string compositionName, IEnumerable<XElement> children, bool deleteMissing)
        {
            if (owner == null || string.IsNullOrWhiteSpace(compositionName))
            {
                return 0;
            }

            object composition;
            try
            {
                composition = owner.GetComposition(compositionName);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return 0;
            }

            return ApplyMembers(composition, children, deleteMissing);
        }

        public static int ApplyMembers(object composition, IEnumerable<XElement> children, bool deleteMissing)
        {
            if (composition == null || children == null || !(composition is IEnumerable existingItems))
            {
                return 0;
            }

            var existing = new List<IEngineeringObject>();
            foreach (object entry in existingItems)
            {
                if (entry is IEngineeringObject engineering)
                {
                    existing.Add(engineering);
                }
            }

            var exported = children as IList<XElement> ?? children.ToList();
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int applied = 0;
            for (int index = 0; index < exported.Count; index++)
            {
                XElement child = exported[index];
                if (child == null)
                {
                    continue;
                }

                string name = AttributeValue(child, "name");
                if (name.Length > 0)
                {
                    keep.Add(name);
                }

                IEngineeringObject match = FindNamed(existing, name);
                if (match == null && name.Length == 0)
                {
                    int position = ReadIndex(child, index);
                    if (position >= 0 && position < existing.Count)
                    {
                        match = existing[position];
                    }
                }

                if (match == null && name.Length > 0)
                {
                    match = CreateMember(composition, name);
                    if (match != null)
                    {
                        existing.Add(match);
                    }
                }

                if (match != null)
                {
                    applied += ApplyElement(match, child);
                }
            }

            if (deleteMissing && keep.Count > 0)
            {
                foreach (IEngineeringObject item in existing)
                {
                    string name = ReadName(item);
                    if (name.Length > 0 && !keep.Contains(name))
                    {
                        DeleteMember(item);
                    }
                }
            }

            return applied;
        }

        public static int ApplyFile(object composition, string path, string childName, bool deleteMissing)
        {
            XElement root = LoadRoot(path);
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return 0;
            }

            return ApplyMembers(composition, root.Elements(childName), deleteMissing);
        }

        public static int ApplyOwnedFile(IEngineeringObject owner, string path, bool deleteMissing)
        {
            XElement root = LoadRoot(path);
            if (owner == null || root == null)
            {
                return 0;
            }

            string composition = AttributeValue(root, "composition");
            if (composition.Length == 0)
            {
                composition = AttributeValue(root, "kind");
            }

            string childName = root.Name.LocalName == "AlarmList" ? "Alarm" : "Item";
            return ApplyComposition(owner, composition, root.Elements(childName), deleteMissing);
        }

        internal static bool TryCoerce(object current, string text, out object value)
        {
            value = text ?? string.Empty;
            if (current == null)
            {
                return true;
            }

            Type type = Nullable.GetUnderlyingType(current.GetType()) ?? current.GetType();
            if (type == typeof(string))
            {
                return true;
            }

            if (type == typeof(bool))
            {
                if (bool.TryParse(text, out bool flag))
                {
                    value = flag;
                    return true;
                }

                return false;
            }

            if (type.IsEnum)
            {
                try
                {
                    value = Enum.Parse(type, text, ignoreCase: true);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }

            if (type == typeof(int) || type == typeof(short) || type == typeof(long) || type == typeof(uint) || type == typeof(ushort))
            {
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole))
                {
                    value = Convert.ChangeType(whole, type, CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            }

            if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            }

            return true;
        }

        private static int ApplyAttribute(IEngineeringObject target, XElement attribute)
        {
            string name = AttributeValue(attribute, "name");
            if (name.Length == 0 || name.Equals("Parent", StringComparison.OrdinalIgnoreCase) || name.Equals("Name", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            object current = ReadAttribute(target, name);
            if (attribute.Elements("Text").Any())
            {
                return WriteTexts(current, attribute) ? 1 : 0;
            }

            if (attribute.Attribute("value") == null)
            {
                if (current is IEngineeringObject nested && attribute.Elements("Attribute").Any())
                {
                    return ApplyElement(nested, attribute);
                }

                return 0;
            }

            string wanted = attribute.Attribute("value").Value ?? string.Empty;
            if (TextOf(current) == wanted)
            {
                return 0;
            }

            if (TryCoerce(current, wanted, out object typed) && TrySet(target, name, typed))
            {
                return 1;
            }

            if (bool.TryParse(wanted, out bool flag) && TrySet(target, name, flag))
            {
                return 1;
            }

            if (long.TryParse(wanted, NumberStyles.Integer, CultureInfo.InvariantCulture, out long whole) && TrySet(target, name, whole))
            {
                return 1;
            }

            return TrySet(target, name, wanted) ? 1 : 0;
        }

        private static bool WriteTexts(object value, XElement attribute)
        {
            if (value == null)
            {
                return false;
            }

            PropertyInfo itemsProperty = value.GetType().GetProperty("Items", BindingFlags.Instance | BindingFlags.Public);
            object items = itemsProperty == null ? null : itemsProperty.GetValue(value);
            if (!(items is IEnumerable enumerable))
            {
                return false;
            }

            bool wrote = false;
            foreach (object item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                string language = LanguageOf(item);
                XElement match = attribute.Elements("Text").FirstOrDefault(text =>
                    string.Equals(AttributeValue(text, "language"), language, StringComparison.OrdinalIgnoreCase));
                if (match == null)
                {
                    continue;
                }

                PropertyInfo textProperty = item.GetType().GetProperty("Text", BindingFlags.Instance | BindingFlags.Public);
                if (textProperty == null || !textProperty.CanWrite)
                {
                    continue;
                }

                string current = textProperty.GetValue(item) as string ?? string.Empty;
                string next = match.Value ?? string.Empty;
                if (current == next)
                {
                    continue;
                }

                try
                {
                    textProperty.SetValue(item, next);
                    wrote = true;
                }
                catch (Exception ex) when (!IsSessionDead(ex))
                {
                }
            }

            return wrote;
        }

        private static string LanguageOf(object item)
        {
            try
            {
                PropertyInfo language = item.GetType().GetProperty("Language", BindingFlags.Instance | BindingFlags.Public);
                object value = language == null ? null : language.GetValue(item);
                if (value == null)
                {
                    return string.Empty;
                }

                PropertyInfo culture = value.GetType().GetProperty("Culture", BindingFlags.Instance | BindingFlags.Public);
                object cultureValue = culture == null ? null : culture.GetValue(value);
                if (cultureValue != null)
                {
                    PropertyInfo name = cultureValue.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
                    if (name != null && name.PropertyType == typeof(string))
                    {
                        return name.GetValue(cultureValue) as string ?? string.Empty;
                    }
                }

                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return string.Empty;
            }
        }

        private static IEngineeringObject FindNamed(IEnumerable<IEngineeringObject> items, string name)
        {
            if (name.Length == 0)
            {
                return null;
            }

            foreach (IEngineeringObject item in items)
            {
                if (string.Equals(ReadName(item), name, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        private static IEngineeringObject CreateMember(object composition, string name)
        {
            MethodInfo method = composition.GetType().GetMethod("Create", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(composition, new object[] { name }) as IEngineeringObject;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return null;
            }
        }

        private static void DeleteMember(IEngineeringObject item)
        {
            MethodInfo method = item.GetType().GetMethod("Delete", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            if (method == null)
            {
                return;
            }

            try
            {
                method.Invoke(item, null);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
            }
        }

        private static object ReadAttribute(IEngineeringObject target, string name)
        {
            try
            {
                return target.GetAttribute(name);
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return null;
            }
        }

        private static bool TrySet(IEngineeringObject target, string name, object value)
        {
            try
            {
                target.SetAttribute(name, value);
                return true;
            }
            catch (Exception ex) when (!IsSessionDead(ex))
            {
                return false;
            }
        }

        private static string ReadName(IEngineeringObject item)
        {
            object value = ReadAttribute(item, "Name");
            return value as string ?? string.Empty;
        }

        private static int ReadIndex(XElement element, int fallback)
        {
            string text = AttributeValue(element, "index");
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) && index > 0)
            {
                return index - 1;
            }

            return fallback;
        }

        private static bool IsChild(XElement element)
        {
            return element.Name.LocalName == "Object" || element.Name.LocalName == "Item";
        }

        private static XElement LoadRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                return XDocument.Load(path).Root;
            }
            catch (Exception ex) when (ex is IOException || ex is XmlException)
            {
                return null;
            }
        }

        private static string AttributeValue(XElement element, string name)
        {
            XAttribute attribute = element == null ? null : element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value ?? string.Empty;
        }

        private static string TextOf(object value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            if (value is string text)
            {
                return text;
            }

            try
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch (Exception)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        private static bool IsSessionDead(Exception ex)
        {
            Exception current = ex is TargetInvocationException invocation ? invocation.InnerException : ex;
            if (current is NonRecoverableException || current is EngineeringObjectDisposedException)
            {
                throw new InvalidOperationException(
                    "TIA Portal closed the Openness session while importing a snapshot. " + current.Message,
                    current);
            }

            return false;
        }
    }
}
