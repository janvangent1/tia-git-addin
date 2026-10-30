using System;
using System.Collections.Generic;
using System.Text;
using TiaGitAddIn.Models.Comparison;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Matches SCL blocks by kind and name, declarations by section and name, and statements by
    /// their normalized tokens. Formatting-only whitespace is ignored. A rename is reported only
    /// for one removed and one added declaration whose type is the same.
    /// </summary>
    internal static class SclComparer
    {
        public static SclPresentation Compare(SclDocument left, SclDocument right)
        {
            var changes = new List<SclChange>();
            if (!string.Equals(left.Namespace, right.Namespace, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add(new SclChange(
                    "NAMESPACE " + (right.Namespace.Length > 0 ? right.Namespace : left.Namespace),
                    SclChangeStatus.Changed,
                    Previously(left.Namespace)));
            }

            var pairs = Align(left.Blocks, right.Blocks, BlockKey);
            int leftIndex = 0;
            int rightIndex = 0;
            foreach ((int leftAt, int rightAt) in pairs)
            {
                AddUnmatchedBlocks(changes, left.Blocks, right.Blocks, leftIndex, leftAt, rightIndex, rightAt);
                changes.Add(CompareBlock(left.Blocks[leftAt], right.Blocks[rightAt]));
                leftIndex = leftAt + 1;
                rightIndex = rightAt + 1;
            }

            AddUnmatchedBlocks(changes, left.Blocks, right.Blocks, leftIndex, left.Blocks.Count, rightIndex, right.Blocks.Count);
            return new SclPresentation(changes);
        }

        private static void AddUnmatchedBlocks(List<SclChange> changes, IReadOnlyList<SclBlock> left,
            IReadOnlyList<SclBlock> right, int leftFrom, int leftTo, int rightFrom, int rightTo)
        {
            for (int i = leftFrom; i < leftTo; i++)
            {
                changes.Add(CompareBlock(left[i], null));
            }

            for (int i = rightFrom; i < rightTo; i++)
            {
                changes.Add(CompareBlock(null, right[i]));
            }
        }

        private static SclChange CompareBlock(SclBlock? left, SclBlock? right)
        {
            SclBlock side = (left ?? right)!;
            var children = new List<SclChange>();
            if (left == null || right == null)
            {
                SclChangeStatus only = left == null ? SclChangeStatus.Added : SclChangeStatus.Removed;
                children.AddRange(Members(side.Header, only));
                if (side.ReturnType.Length > 0)
                {
                    children.Insert(0, new SclChange("Return : " + side.ReturnType, only, string.Empty));
                }

                foreach (SclSection section in side.Sections)
                {
                    children.Add(SectionChange(section, only));
                }

                children.AddRange(Nodes(side.Body, only));
                return new SclChange(BlockTitle(side), only, string.Empty, children);
            }

            if (!string.Equals(Norm(left.ReturnType), Norm(right.ReturnType), StringComparison.Ordinal))
            {
                children.Add(new SclChange(
                    "Return : " + (right.ReturnType.Length > 0 ? right.ReturnType : left.ReturnType),
                    SclChangeStatus.Changed,
                    Previously(left.ReturnType)));
            }
            else if (left.ReturnType.Length > 0)
            {
                children.Add(new SclChange("Return : " + right.ReturnType, SclChangeStatus.Unchanged, string.Empty));
            }

            children.AddRange(CompareMembers(left.Header, right.Header));
            children.AddRange(CompareSections(left.Sections, right.Sections));
            children.AddRange(CompareNodes(left.Body, right.Body));
            return new SclChange(BlockTitle(right), Rollup(children), string.Empty, children);
        }

        private static List<SclChange> CompareSections(IReadOnlyList<SclSection> left, IReadOnlyList<SclSection> right)
        {
            var changes = new List<SclChange>();
            var pairs = Align(left, right, section => section.Name.ToUpperInvariant());
            int leftIndex = 0;
            int rightIndex = 0;
            foreach ((int leftAt, int rightAt) in pairs)
            {
                for (int i = leftIndex; i < leftAt; i++)
                {
                    changes.Add(SectionChange(left[i], SclChangeStatus.Removed));
                }

                for (int i = rightIndex; i < rightAt; i++)
                {
                    changes.Add(SectionChange(right[i], SclChangeStatus.Added));
                }

                var children = CompareMembers(left[leftAt].Members, right[rightAt].Members);
                changes.Add(new SclChange(right[rightAt].Name, Rollup(children), string.Empty, children));
                leftIndex = leftAt + 1;
                rightIndex = rightAt + 1;
            }

            for (int i = leftIndex; i < left.Count; i++)
            {
                changes.Add(SectionChange(left[i], SclChangeStatus.Removed));
            }

            for (int i = rightIndex; i < right.Count; i++)
            {
                changes.Add(SectionChange(right[i], SclChangeStatus.Added));
            }

            return changes;
        }

        private static SclChange SectionChange(SclSection section, SclChangeStatus status)
        {
            return new SclChange(section.Name, status, string.Empty, Members(section.Members, status));
        }

        private static List<SclChange> CompareMembers(IReadOnlyList<SclMember> left, IReadOnlyList<SclMember> right)
        {
            var changes = new List<SclChange>();
            var pairs = Align(left, right, MemberKey);
            int leftIndex = 0;
            int rightIndex = 0;
            foreach ((int leftAt, int rightAt) in pairs)
            {
                AddMemberGap(changes, left, right, leftIndex, leftAt, rightIndex, rightAt);
                changes.Add(CompareMember(left[leftAt], right[rightAt]));
                leftIndex = leftAt + 1;
                rightIndex = rightAt + 1;
            }

            AddMemberGap(changes, left, right, leftIndex, left.Count, rightIndex, right.Count);
            return changes;
        }

        private static void AddMemberGap(List<SclChange> changes, IReadOnlyList<SclMember> left,
            IReadOnlyList<SclMember> right, int leftFrom, int leftTo, int rightFrom, int rightTo)
        {
            int leftCount = leftTo - leftFrom;
            int rightCount = rightTo - rightFrom;
            if (leftCount == 1 && rightCount == 1
                && left[leftFrom].Name.Length > 0 && right[rightFrom].Name.Length > 0
                && SameFingerprint(left[leftFrom], right[rightFrom])
                && !string.Equals(MemberKey(left[leftFrom]), MemberKey(right[rightFrom]), StringComparison.Ordinal))
            {
                changes.Add(new SclChange(
                    "Renamed " + DisplayName(left[leftFrom]) + " to " + DisplayName(right[rightFrom]),
                    SclChangeStatus.Changed,
                    string.Empty));
                return;
            }

            for (int i = leftFrom; i < leftTo; i++)
            {
                changes.Add(MemberChange(left[i], SclChangeStatus.Removed));
            }

            for (int i = rightFrom; i < rightTo; i++)
            {
                changes.Add(MemberChange(right[i], SclChangeStatus.Added));
            }
        }

        private static SclChange CompareMember(SclMember left, SclMember right)
        {
            bool typeSame = string.Equals(Norm(left.TypeText), Norm(right.TypeText), StringComparison.Ordinal);
            bool commentSame = string.Equals(Norm(left.Comment), Norm(right.Comment), StringComparison.Ordinal);
            bool attributesSame = string.Equals(Norm(left.Attributes), Norm(right.Attributes), StringComparison.Ordinal);
            if (typeSame && commentSame && attributesSame)
            {
                return MemberChange(right, SclChangeStatus.Unchanged);
            }

            string detail = string.Empty;
            if (!typeSame || !attributesSame)
            {
                detail = Previously(MemberTitle(left));
            }
            else if (!commentSame)
            {
                detail = Previously(left.Comment);
            }

            return new SclChange(MemberTitle(right), SclChangeStatus.Changed, detail);
        }

        private static List<SclChange> CompareNodes(IReadOnlyList<SclNode> left, IReadOnlyList<SclNode> right)
        {
            var changes = new List<SclChange>();
            var pairs = Align(left, right, NodeKey);
            int leftIndex = 0;
            int rightIndex = 0;
            foreach ((int leftAt, int rightAt) in pairs)
            {
                AddNodeGap(changes, left, right, leftIndex, leftAt, rightIndex, rightAt);
                changes.Add(CompareMatchedNode(left[leftAt], right[rightAt]));
                leftIndex = leftAt + 1;
                rightIndex = rightAt + 1;
            }

            AddNodeGap(changes, left, right, leftIndex, left.Count, rightIndex, right.Count);
            return changes;
        }

        private static void AddNodeGap(List<SclChange> changes, IReadOnlyList<SclNode> left, IReadOnlyList<SclNode> right,
            int leftFrom, int leftTo, int rightFrom, int rightTo)
        {
            int leftCount = leftTo - leftFrom;
            int rightCount = rightTo - rightFrom;
            if (leftCount > 0 && leftCount == rightCount && SameKind(left, leftFrom, right, rightFrom, leftCount)
                && !AllRegions(left, leftFrom, leftCount))
            {
                for (int i = 0; i < leftCount; i++)
                {
                    changes.Add(ChangedNode(left[leftFrom + i], right[rightFrom + i]));
                }

                return;
            }

            for (int i = leftFrom; i < leftTo; i++)
            {
                changes.Add(NodeChange(left[i], SclChangeStatus.Removed));
            }

            for (int i = rightFrom; i < rightTo; i++)
            {
                changes.Add(NodeChange(right[i], SclChangeStatus.Added));
            }
        }

        private static SclChange CompareMatchedNode(SclNode left, SclNode right)
        {
            if (left is SclRegion leftRegion && right is SclRegion rightRegion)
            {
                var children = CompareNodes(leftRegion.Body, rightRegion.Body);
                return new SclChange("REGION " + rightRegion.Name, Rollup(children), string.Empty, children);
            }

            return NodeChange(right, SclChangeStatus.Unchanged);
        }

        private static SclChange ChangedNode(SclNode left, SclNode right)
        {
            if (left is SclRegion && right is SclRegion)
            {
                return CompareMatchedNode(left, right);
            }

            string previous = left is SclComment comment ? comment.Text : left is SclStatement statement ? statement.Text : string.Empty;
            return new SclChange(NodeTitle(right), SclChangeStatus.Changed, Previously(previous));
        }

        private static IEnumerable<SclChange> Members(IReadOnlyList<SclMember> members, SclChangeStatus status)
        {
            foreach (SclMember member in members)
            {
                yield return MemberChange(member, status);
            }
        }

        private static IEnumerable<SclChange> Nodes(IReadOnlyList<SclNode> nodes, SclChangeStatus status)
        {
            foreach (SclNode node in nodes)
            {
                yield return NodeChange(node, status);
            }
        }

        private static SclChange MemberChange(SclMember member, SclChangeStatus status)
        {
            return new SclChange(MemberTitle(member), status, member.Comment);
        }

        private static SclChange NodeChange(SclNode node, SclChangeStatus status)
        {
            if (node is SclRegion region)
            {
                return new SclChange("REGION " + region.Name, status, string.Empty, Nodes(region.Body, status));
            }

            return new SclChange(NodeTitle(node), status, string.Empty);
        }

        private static string MemberTitle(SclMember member)
        {
            if (member.Name.Length == 0)
            {
                if (member.Attributes.Length > 0)
                {
                    return member.Attributes;
                }

                return member.Comment.Length > 0 ? "// " + member.Comment : member.TypeText;
            }

            string attributes = member.Attributes.Length > 0 ? " " + member.Attributes : string.Empty;
            return member.TypeText.Length > 0
                ? member.Name + attributes + " : " + member.TypeText
                : member.Name + attributes;
        }

        private static string NodeTitle(SclNode node)
        {
            if (node is SclComment comment)
            {
                return "// " + comment.Text;
            }

            if (node is SclStatement statement)
            {
                return statement.Text;
            }

            if (node is SclRegion region)
            {
                return "REGION " + region.Name;
            }

            return string.Empty;
        }

        private static string MemberKey(SclMember member)
        {
            if (member.Name.Length == 0)
            {
                if (member.Attributes.Length > 0 && member.Comment.Length == 0 && member.TypeText.Length == 0)
                {
                    return "attributes";
                }

                return "comment:" + Norm(member.Comment);
            }

            return "name:" + Norm(member.Name.Trim().Trim('"'));
        }

        private static string NodeKey(SclNode node)
        {
            if (node is SclRegion region)
            {
                return "region:" + Norm(region.Name);
            }

            if (node is SclComment comment)
            {
                return "comment:" + Norm(comment.Text);
            }

            if (node is SclStatement statement)
            {
                return "statement:" + statement.Normalized;
            }

            return string.Empty;
        }

        private static string BlockKey(SclBlock block)
        {
            return block.Kind.ToUpperInvariant() + ":" + Norm(block.Name.Trim().Trim('"'));
        }

        private static string BlockTitle(SclBlock block)
        {
            return block.Name.Length > 0 ? block.Kind + " " + block.Name : block.Kind;
        }

        private static bool SameFingerprint(SclMember left, SclMember right)
        {
            return string.Equals(Norm(left.TypeText), Norm(right.TypeText), StringComparison.Ordinal);
        }

        private static string DisplayName(SclMember member)
        {
            return member.Name.Length > 0 ? member.Name : MemberTitle(member);
        }

        private static bool SameKind(IReadOnlyList<SclNode> left, int leftFrom, IReadOnlyList<SclNode> right,
            int rightFrom, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (left[leftFrom + i].GetType() != right[rightFrom + i].GetType())
                {
                    return false;
                }
            }

            return true;
        }

        private static bool AllRegions(IReadOnlyList<SclNode> nodes, int from, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!(nodes[from + i] is SclRegion))
                {
                    return false;
                }
            }

            return true;
        }

        private static SclChangeStatus Rollup(IReadOnlyList<SclChange> children)
        {
            foreach (SclChange child in children)
            {
                if (child.Status != SclChangeStatus.Unchanged)
                {
                    return SclChangeStatus.Changed;
                }
            }

            return SclChangeStatus.Unchanged;
        }

        private static string Previously(string text)
        {
            return text.Length == 0 ? string.Empty : "Previously: " + text;
        }

        private static string Norm(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(text.Length);
            bool space = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    space = builder.Length > 0;
                    continue;
                }

                if (space)
                {
                    builder.Append(' ');
                    space = false;
                }

                builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        private static List<(int Left, int Right)> Align<T>(IReadOnlyList<T> left, IReadOnlyList<T> right, Func<T, string> key)
        {
            var matches = new List<(int Left, int Right)>();
            int n = left.Count;
            int m = right.Count;
            if (n == 0 || m == 0 || (long)n * m > 250_000)
            {
                int count = Math.Min(n, m);
                for (int i = 0; i < count; i++)
                {
                    if (string.Equals(key(left[i]), key(right[i]), StringComparison.Ordinal))
                    {
                        matches.Add((i, i));
                    }
                }

                return matches;
            }

            var leftKeys = new string[n];
            var rightKeys = new string[m];
            for (int i = 0; i < n; i++)
            {
                leftKeys[i] = key(left[i]);
            }

            for (int i = 0; i < m; i++)
            {
                rightKeys[i] = key(right[i]);
            }

            int width = m + 1;
            var scores = new int[(n + 1) * width];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    scores[i * width + j] = leftKeys[i] == rightKeys[j]
                        ? scores[(i + 1) * width + j + 1] + 1
                        : Math.Max(scores[(i + 1) * width + j], scores[i * width + j + 1]);
                }
            }

            int a = 0;
            int b = 0;
            while (a < n && b < m)
            {
                if (leftKeys[a] == rightKeys[b])
                {
                    matches.Add((a, b));
                    a++;
                    b++;
                }
                else if (scores[(a + 1) * width + b] >= scores[a * width + b + 1])
                {
                    a++;
                }
                else
                {
                    b++;
                }
            }

            return matches;
        }
    }
}
