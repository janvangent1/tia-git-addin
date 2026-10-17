using System;
using System.Globalization;

namespace TiaGitAddIn.Services.Hmi
{
    public sealed class HmiExportUpdate
    {
        public string Device { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string State { get; set; } = "pending";

        public int Count { get; set; }

        public string Detail { get; set; } = string.Empty;

        public string Key => (Device ?? string.Empty) + "\n" + (Category ?? string.Empty);

        public string BusyText
        {
            get
            {
                string title = string.IsNullOrEmpty(Device) ? Category : Device + " — " + Category;
                if (Count > 0)
                {
                    title += " (" + Count.ToString(CultureInfo.InvariantCulture) + ")";
                }

                if (!string.IsNullOrEmpty(Detail) && (State == "running" || State == "skipped" || State == "failed"))
                {
                    title += " — " + Detail;
                }

                return title;
            }
        }
    }

    public static class HmiExportProgress
    {
        public static readonly string[] HardwareCategories =
        {
            "Devices",
            "Modules",
            "Networks",
            "Drives",
            "Parameters",
            "Telegrams"
        };

        public static readonly string[] UnifiedCategories =
        {
            "Screens",
            "Tags",
            "System tags",
            "Scripts",
            "Text lists",
            "Graphic lists",
            "System text lists",
            "Alarms",
            "Connections",
            "Logs",
            "Other",
            "Runtime settings"
        };

        public static readonly string[] ComfortCategories =
        {
            "Screens",
            "Popups",
            "Templates",
            "Slide-ins",
            "Tags",
            "Scripts",
            "Text lists",
            "Connections",
            "Cycles",
            "Permanent area",
            "Overview",
            "Graphic lists",
            "Alarms",
            "Graphics",
            "Other"
        };

        public static bool IsChecklist(string message)
        {
            return message != null && message.StartsWith("@hmi\t", StringComparison.Ordinal);
        }

        public static string Format(string device, string category, string state, int count, string detail)
        {
            return "@hmi\t" + Clean(device) + "\t" + Clean(category) + "\t" + Clean(state) + "\t" +
                count.ToString(CultureInfo.InvariantCulture) + "\t" + Clean(detail);
        }

        public static bool TryParse(string message, out HmiExportUpdate update)
        {
            update = new HmiExportUpdate();
            if (!IsChecklist(message))
            {
                return false;
            }

            string[] parts = message.Split('\t');
            if (parts.Length < 6 || string.IsNullOrWhiteSpace(parts[2]))
            {
                return false;
            }

            int count = 0;
            int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out count);
            update = new HmiExportUpdate
            {
                Device = parts[1],
                Category = parts[2],
                State = string.IsNullOrWhiteSpace(parts[3]) ? "running" : parts[3],
                Count = count,
                Detail = string.Join("\t", parts, 5, parts.Length - 5)
            };
            return true;
        }

        private static string Clean(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
