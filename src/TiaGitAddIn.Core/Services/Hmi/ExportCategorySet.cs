using System;
using System.Collections.Generic;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Categories the user can leave out of an export. An empty disabled list exports everything.
    /// Comfort categories that are not in the HMI list stay on.
    /// </summary>
    public sealed class ExportCategorySet
    {
        public static ExportCategorySet All { get; } = new ExportCategorySet(null, null);

        /// <summary>Set on the UI thread for the export that is about to run.</summary>
        public static ExportCategorySet? Pending { get; set; }

        private readonly HashSet<string> disabledHmi;
        private readonly HashSet<string> disabledHardware;

        public ExportCategorySet(IEnumerable<string>? disabledHmi, IEnumerable<string>? disabledHardware)
        {
            this.disabledHmi = Names(disabledHmi);
            this.disabledHardware = Names(disabledHardware);
        }

        public IReadOnlyCollection<string> DisabledHmi => disabledHmi;

        public IReadOnlyCollection<string> DisabledHardware => disabledHardware;

        public bool AllowsHmi(string category) => !disabledHmi.Contains(category ?? string.Empty);

        public bool AllowsHardware(string category) => !disabledHardware.Contains(category ?? string.Empty);

        public static ExportCategorySet From(GitConfiguration? configuration)
        {
            if (configuration == null)
            {
                return All;
            }

            return new ExportCategorySet(configuration.DisabledHmiCategories, configuration.DisabledHardwareCategories);
        }

        public static ExportCategorySet CurrentOr(ExportCategorySet fallback) => Pending ?? fallback ?? All;

        private static HashSet<string> Names(IEnumerable<string>? names)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (names == null)
            {
                return set;
            }

            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    set.Add(name.Trim());
                }
            }

            return set;
        }
    }
}
