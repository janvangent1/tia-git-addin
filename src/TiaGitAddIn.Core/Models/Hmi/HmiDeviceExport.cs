using System.Collections.Generic;
using System.Xml.Linq;

namespace TiaGitAddIn.Models.Hmi
{
    public sealed class HmiTextExport
    {
        public string Language { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }

    public sealed class HmiItemExport
    {
        public string Kind { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int Left { get; set; }

        public int Top { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public string ProcessValue { get; set; } = string.Empty;

        public List<HmiTextExport> Texts { get; set; } = new();

        public XElement Content { get; set; } = null!;
    }

    public sealed class HmiScreenExport
    {
        public string Name { get; set; } = string.Empty;

        public string Folder { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        public List<HmiItemExport> Items { get; set; } = new();

        public XElement Content { get; set; } = null!;
    }

    public sealed class HmiDeviceExport
    {
        public const string UnifiedKind = "Unified";

        public const string ComfortKind = "Comfort";

        public string Name { get; set; } = string.Empty;

        public string Kind { get; set; } = UnifiedKind;

        public List<HmiScreenExport> Screens { get; set; } = new();
    }
}
