using System.Collections.Generic;

namespace TiaGitAddIn.Services.Comparison
{
    internal sealed class SclDocument
    {
        public SclDocument(string ns, IReadOnlyList<SclBlock> blocks, bool isPartial, string partialReason)
        {
            Namespace = ns ?? string.Empty;
            Blocks = blocks;
            IsPartial = isPartial;
            PartialReason = partialReason ?? string.Empty;
        }

        public string Namespace { get; }
        public IReadOnlyList<SclBlock> Blocks { get; }
        public bool IsPartial { get; }
        public string PartialReason { get; }
    }

    internal sealed class SclBlock
    {
        public SclBlock(string kind, string name, string returnType, List<SclMember> header,
            List<SclSection> sections, List<SclNode> body)
        {
            Kind = kind;
            Name = name;
            ReturnType = returnType ?? string.Empty;
            Header = header;
            Sections = sections;
            Body = body;
        }

        public string Kind { get; }
        public string Name { get; }
        public string ReturnType { get; }
        public List<SclMember> Header { get; }
        public List<SclSection> Sections { get; }
        public List<SclNode> Body { get; }
    }

    internal sealed class SclSection
    {
        public SclSection(string name)
        {
            Name = name;
        }

        public string Name { get; }
        public List<SclMember> Members { get; } = new List<SclMember>();
    }

    internal sealed class SclMember
    {
        public SclMember(string name, string typeText, string comment, string attributes = "")
        {
            Name = name ?? string.Empty;
            TypeText = typeText ?? string.Empty;
            Comment = comment ?? string.Empty;
            Attributes = attributes ?? string.Empty;
        }

        public string Name { get; }
        public string TypeText { get; }
        public string Comment { get; }
        public string Attributes { get; }
    }

    internal abstract class SclNode
    {
    }

    internal sealed class SclComment : SclNode
    {
        public SclComment(string text)
        {
            Text = text ?? string.Empty;
        }

        public string Text { get; }
    }

    internal sealed class SclStatement : SclNode
    {
        public SclStatement(string text, string normalized)
        {
            Text = text ?? string.Empty;
            Normalized = normalized ?? string.Empty;
        }

        public string Text { get; }
        public string Normalized { get; }
    }

    internal sealed class SclRegion : SclNode
    {
        public SclRegion(string name, List<SclNode> body)
        {
            Name = name ?? string.Empty;
            Body = body;
        }

        public string Name { get; }
        public List<SclNode> Body { get; }
    }
}
