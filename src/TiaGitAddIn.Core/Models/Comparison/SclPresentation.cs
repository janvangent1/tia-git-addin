using System;
using System.Collections.Generic;

namespace TiaGitAddIn.Models.Comparison
{
    public enum SclChangeStatus
    {
        Unchanged,
        Added,
        Removed,
        Changed,
    }

    /// <summary>
    /// One block, section, declaration, region, comment, or statement in source order.
    /// </summary>
    public sealed class SclChange
    {
        public SclChange(string title, SclChangeStatus status, string detail, IEnumerable<SclChange>? children = null)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Status = status;
            Detail = detail ?? string.Empty;
            Children = ImmutableCopy.Of(children ?? Array.Empty<SclChange>(), nameof(children));
        }

        public string Title { get; }
        public SclChangeStatus Status { get; }
        public string Detail { get; }
        public IReadOnlyList<SclChange> Children { get; }
    }

    /// <summary>
    /// Structured SCL comparison. The raw text stays on the result beside this tree.
    /// </summary>
    public sealed class SclPresentation : ComparisonPresentation
    {
        public SclPresentation(IEnumerable<SclChange> changes)
            : base(ComparisonPresentationKind.Scl)
        {
            Changes = ImmutableCopy.Of(changes, nameof(changes));
        }

        public IReadOnlyList<SclChange> Changes { get; }
    }
}
