using System;
using System.Collections.Generic;
using TiaGitAddIn.Models.Comparison;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Bounded line-based text comparer. Below <see cref="TextComparisonLimits.MaximumMatrixCells"/> it
    /// builds a full Wagner-Fischer style alignment (match/substitute/delete/insert, cost 0/1/1/1) and
    /// backtracks it into a diff. Above that limit it still aligns a small edit, so one inserted line
    /// does not mark every line below it, and falls back to a positional comparison only when the edit
    /// is too large. Whitespace rules come from <see cref="DiffWhitespace"/>. Display output is bounded
    /// on line count and per-line character length; the underlying <see cref="ComparisonRawText"/> passed
    /// in is never mutated or truncated.
    /// </summary>
    public sealed class LineTextComparer : ITextComparer
    {
        private const int MaximumAlignedEdits = 2000;

        private readonly TextComparisonLimits _limits;
        private readonly Func<DiffWhitespace> _whitespace;

        public LineTextComparer(TextComparisonLimits limits, Func<DiffWhitespace>? whitespace = null)
        {
            _limits = limits ?? throw new ArgumentNullException(nameof(limits));
            _whitespace = whitespace ?? (() => DiffWhitespace.Exact);
        }

        public TextPresentation Compare(ComparisonRawText rawText)
        {
            if (rawText == null) throw new ArgumentNullException(nameof(rawText));

            DiffWhitespace whitespace = _whitespace();
            string[] leftLines = NormalizeAndSplitLines(rawText.LeftText);
            string[] rightLines = NormalizeAndSplitLines(rawText.RightText);

            (string[] boundedLeft, bool leftLinesCut) = BoundLineCount(leftLines);
            (string[] boundedRight, bool rightLinesCut) = BoundLineCount(rightLines);

            bool usedLinearFallback = (long)boundedLeft.Length * boundedRight.Length > _limits.MaximumMatrixCells;
            string[] leftKey = boundedLeft;
            string[] rightKey = boundedRight;
            bool[] leftCommentOnly = Array.Empty<bool>();
            bool[] rightCommentOnly = Array.Empty<bool>();
            if (whitespace.IgnoreComments)
            {
                CommentFilter.Strip(boundedLeft, out leftKey, out leftCommentOnly);
                CommentFilter.Strip(boundedRight, out rightKey, out rightCommentOnly);
            }

            List<TextDiffLine> rawDiff = usedLinearFallback
                ? BuildMyersOrLinear(leftKey, rightKey, boundedLeft, boundedRight, whitespace)
                : BuildBoundedDiff(leftKey, rightKey, boundedLeft, boundedRight, whitespace);
            if (whitespace.IgnoreComments)
            {
                rawDiff = DropCommentChanges(rawDiff, leftCommentOnly, rightCommentOnly);
            }

            if (whitespace.IgnoreBlankLines)
            {
                rawDiff = DropBlankChanges(rawDiff, whitespace);
            }

            (List<TextDiffLine> lengthCappedDiff, bool anyLineLengthTruncated) = BoundLineLength(rawDiff);

            bool isTruncated = leftLinesCut || rightLinesCut || anyLineLengthTruncated;
            if (isTruncated)
            {
                lengthCappedDiff.Add(BuildOmittedRow(
                    leftLines.Length - boundedLeft.Length, rightLines.Length - boundedRight.Length, anyLineLengthTruncated));
            }

            return new TextPresentation(lengthCappedDiff, isTruncated, usedLinearFallback);
        }

        private static string[] NormalizeAndSplitLines(string? text)
        {
            if (text == null || text.Length == 0) return Array.Empty<string>();
            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return normalized.Split('\n');
        }

        private (string[] lines, bool wasTruncated) BoundLineCount(string[] lines)
        {
            if (lines.Length <= _limits.MaximumLinesPerSide) return (lines, false);
            var bounded = new string[_limits.MaximumLinesPerSide];
            Array.Copy(lines, bounded, _limits.MaximumLinesPerSide);
            return (bounded, true);
        }

        private (List<TextDiffLine> lines, bool anyTruncated) BoundLineLength(List<TextDiffLine> lines)
        {
            bool anyTruncated = false;
            var result = new List<TextDiffLine>(lines.Count);
            foreach (TextDiffLine line in lines)
            {
                if (line.Text.Length > _limits.MaximumLineLength)
                {
                    anyTruncated = true;
                    result.Add(new TextDiffLine(line.Kind, line.LeftLineNumber, line.RightLineNumber,
                        line.Text.Substring(0, _limits.MaximumLineLength)));
                }
                else
                {
                    result.Add(line);
                }
            }

            return (result, anyTruncated);
        }

        private static TextDiffLine BuildOmittedRow(int leftOmittedCount, int rightOmittedCount, bool anyLineLengthTruncated)
        {
            var parts = new List<string>();
            if (leftOmittedCount > 0) parts.Add($"{leftOmittedCount} left line(s)");
            if (rightOmittedCount > 0) parts.Add($"{rightOmittedCount} right line(s)");

            string lineText = parts.Count > 0 ? $"{string.Join(" and ", parts)} omitted" : "Some content omitted";
            string lengthText = anyLineLengthTruncated ? " (some lines truncated to the display limit)" : string.Empty;
            return new TextDiffLine(TextDiffLineKind.Omitted, null, null, $"{lineText} due to display limits{lengthText}.");
        }

        /// <summary>Compares by position only, in O(n+m); an unequal position emits removal then addition.</summary>
        private static List<TextDiffLine> BuildLinearDiff(
            string[] leftKey,
            string[] rightKey,
            string[] leftDisplay,
            string[] rightDisplay,
            DiffWhitespace whitespace)
        {
            var result = new List<TextDiffLine>();
            int max = Math.Max(leftKey.Length, rightKey.Length);

            for (int k = 0; k < max; k++)
            {
                bool hasLeft = k < leftKey.Length;
                bool hasRight = k < rightKey.Length;

                if (hasLeft && hasRight)
                {
                    AppendAlignedPair(result, leftKey[k], leftDisplay[k], k + 1, rightKey[k], rightDisplay[k], k + 1, whitespace);
                }
                else if (hasLeft)
                {
                    result.Add(new TextDiffLine(TextDiffLineKind.Removed, k + 1, null, leftDisplay[k]));
                }
                else
                {
                    result.Add(new TextDiffLine(TextDiffLineKind.Added, null, k + 1, rightDisplay[k]));
                }
            }

            return result;
        }

        private static void AppendAlignedPair(
            List<TextDiffLine> result,
            string leftKey,
            string leftDisplay,
            int leftLineNumber,
            string rightKey,
            string rightDisplay,
            int rightLineNumber,
            DiffWhitespace whitespace)
        {
            if (whitespace.LinesMatch(leftKey, rightKey))
            {
                result.Add(new TextDiffLine(TextDiffLineKind.Unchanged, leftLineNumber, rightLineNumber, leftDisplay));
            }
            else
            {
                result.Add(new TextDiffLine(TextDiffLineKind.Removed, leftLineNumber, null, leftDisplay));
                result.Add(new TextDiffLine(TextDiffLineKind.Added, null, rightLineNumber, rightDisplay));
            }
        }

        private static List<TextDiffLine> DropBlankChanges(List<TextDiffLine> lines, DiffWhitespace whitespace)
        {
            var kept = new List<TextDiffLine>(lines.Count);
            foreach (TextDiffLine line in lines)
            {
                bool blankChange = line.Kind is TextDiffLineKind.Added or TextDiffLineKind.Removed
                    && whitespace.IsBlankLine(line.Text);
                if (!blankChange)
                {
                    kept.Add(line);
                }
            }

            return kept;
        }

        private static List<TextDiffLine> DropCommentChanges(
            List<TextDiffLine> lines,
            bool[] leftCommentOnly,
            bool[] rightCommentOnly)
        {
            var kept = new List<TextDiffLine>(lines.Count);
            foreach (TextDiffLine line in lines)
            {
                if (line.Kind == TextDiffLineKind.Removed
                    && line.LeftLineNumber is int left
                    && left > 0
                    && left <= leftCommentOnly.Length
                    && leftCommentOnly[left - 1])
                {
                    continue;
                }

                if (line.Kind == TextDiffLineKind.Added
                    && line.RightLineNumber is int right
                    && right > 0
                    && right <= rightCommentOnly.Length
                    && rightCommentOnly[right - 1])
                {
                    continue;
                }

                kept.Add(line);
            }

            return kept;
        }

        private static List<TextDiffLine> BuildBoundedDiff(
            string[] leftKey,
            string[] rightKey,
            string[] leftDisplay,
            string[] rightDisplay,
            DiffWhitespace whitespace)
        {
            int[,] dp = BuildAlignmentMatrix(leftKey, rightKey, whitespace);
            List<DiffOp> ops = Backtrack(leftKey, rightKey, dp, whitespace);
            ops.Reverse();
            return ExpandOps(ops, leftDisplay, rightDisplay);
        }

        /// <summary>
        /// Aligns a large file when the edit is small. A one-line insertion then stays one added line.
        /// Past <see cref="MaximumAlignedEdits"/> it uses the positional comparison.
        /// </summary>
        private static List<TextDiffLine> BuildMyersOrLinear(
            string[] leftKey,
            string[] rightKey,
            string[] leftDisplay,
            string[] rightDisplay,
            DiffWhitespace whitespace)
        {
            List<DiffOp>? ops = TryMyers(leftKey, rightKey, whitespace);
            if (ops == null)
            {
                return BuildLinearDiff(leftKey, rightKey, leftDisplay, rightDisplay, whitespace);
            }

            return ExpandOps(ops, leftDisplay, rightDisplay);
        }

        private static List<DiffOp>? TryMyers(string[] left, string[] right, DiffWhitespace whitespace)
        {
            int n = left.Length;
            int m = right.Length;
            int limit = Math.Min(n + m, MaximumAlignedEdits);
            int offset = limit;
            var frontier = new int[(2 * limit) + 1];
            var trace = new List<int[]>(limit + 1);

            for (int distance = 0; distance <= limit; distance++)
            {
                var snapshot = new int[frontier.Length];
                Array.Copy(frontier, snapshot, frontier.Length);
                trace.Add(snapshot);

                for (int k = -distance; k <= distance; k += 2)
                {
                    int index = k + offset;
                    int x;
                    if (k == -distance || (k != distance && frontier[index - 1] < frontier[index + 1]))
                    {
                        x = frontier[index + 1];
                    }
                    else
                    {
                        x = frontier[index - 1] + 1;
                    }

                    int y = x - k;
                    while (x < n && y < m && whitespace.LinesMatch(left[x], right[y]))
                    {
                        x++;
                        y++;
                    }

                    frontier[index] = x;
                    if (x >= n && y >= m)
                    {
                        return BacktrackMyers(left, right, trace, offset);
                    }
                }
            }

            return null;
        }

        private static List<DiffOp> BacktrackMyers(string[] left, string[] right, List<int[]> trace, int offset)
        {
            var ops = new List<DiffOp>();
            int x = left.Length;
            int y = right.Length;
            for (int distance = trace.Count - 1; distance >= 0; distance--)
            {
                int[] frontier = trace[distance];
                int k = x - y;
                int previousK = k == -distance || (k != distance && frontier[k - 1 + offset] < frontier[k + 1 + offset])
                    ? k + 1
                    : k - 1;
                int previousX = frontier[previousK + offset];
                int previousY = previousX - previousK;

                while (x > previousX && y > previousY)
                {
                    x--;
                    y--;
                    ops.Add(new DiffOp(DiffOpKind.Match, x, y));
                }

                if (distance > 0)
                {
                    if (x == previousX)
                    {
                        y--;
                        ops.Add(new DiffOp(DiffOpKind.Insert, -1, y));
                    }
                    else
                    {
                        x--;
                        ops.Add(new DiffOp(DiffOpKind.Delete, x, -1));
                    }
                }
            }

            ops.Reverse();
            return ops;
        }

        private static int[,] BuildAlignmentMatrix(string[] left, string[] right, DiffWhitespace whitespace)
        {
            int m = left.Length;
            int n = right.Length;
            var dp = new int[m + 1, n + 1];
            for (int i = 0; i <= m; i++) dp[i, 0] = i;
            for (int j = 0; j <= n; j++) dp[0, j] = j;

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    int diagCost = dp[i - 1, j - 1] + (whitespace.LinesMatch(left[i - 1], right[j - 1]) ? 0 : 1);
                    int deleteCost = dp[i - 1, j] + 1;
                    int insertCost = dp[i, j - 1] + 1;
                    dp[i, j] = Math.Min(diagCost, Math.Min(deleteCost, insertCost));
                }
            }

            return dp;
        }

        /// <summary>
        /// Walks the alignment matrix from (m, n) back to (0, 0). An exact line match always takes the
        /// diagonal for free; that is always optimal for this cost model, so no tie-break is needed there.
        /// Otherwise, when multiple operations tie for the minimal cost, deletion (removal) is preferred
        /// over insertion (addition), and both are preferred over a diagonal substitution -- the
        /// deterministic tie-break this class documents for callers, since matched lines are unambiguous
        /// but a three-way cost tie is not resolved by the brief's own example tests.
        /// </summary>
        private static List<DiffOp> Backtrack(string[] left, string[] right, int[,] dp, DiffWhitespace whitespace)
        {
            var ops = new List<DiffOp>();
            int i = left.Length;
            int j = right.Length;

            while (i > 0 || j > 0)
            {
                if (i > 0 && j > 0 && whitespace.LinesMatch(left[i - 1], right[j - 1]))
                {
                    ops.Add(new DiffOp(DiffOpKind.Match, i - 1, j - 1));
                    i--; j--;
                    continue;
                }

                int current = dp[i, j];
                if (i > 0 && dp[i - 1, j] + 1 == current)
                {
                    ops.Add(new DiffOp(DiffOpKind.Delete, i - 1, -1));
                    i--;
                }
                else if (j > 0 && dp[i, j - 1] + 1 == current)
                {
                    ops.Add(new DiffOp(DiffOpKind.Insert, -1, j - 1));
                    j--;
                }
                else
                {
                    ops.Add(new DiffOp(DiffOpKind.Substitute, i - 1, j - 1));
                    i--; j--;
                }
            }

            return ops;
        }

        private static List<TextDiffLine> ExpandOps(List<DiffOp> ops, string[] left, string[] right)
        {
            var result = new List<TextDiffLine>(ops.Count);
            foreach (DiffOp op in ops)
            {
                switch (op.Kind)
                {
                    case DiffOpKind.Match:
                        result.Add(new TextDiffLine(TextDiffLineKind.Unchanged, op.LeftIndex + 1, op.RightIndex + 1, left[op.LeftIndex]));
                        break;
                    case DiffOpKind.Delete:
                        result.Add(new TextDiffLine(TextDiffLineKind.Removed, op.LeftIndex + 1, null, left[op.LeftIndex]));
                        break;
                    case DiffOpKind.Insert:
                        result.Add(new TextDiffLine(TextDiffLineKind.Added, null, op.RightIndex + 1, right[op.RightIndex]));
                        break;
                    case DiffOpKind.Substitute:
                        result.Add(new TextDiffLine(TextDiffLineKind.Removed, op.LeftIndex + 1, null, left[op.LeftIndex]));
                        result.Add(new TextDiffLine(TextDiffLineKind.Added, null, op.RightIndex + 1, right[op.RightIndex]));
                        break;
                }
            }

            return result;
        }

        private enum DiffOpKind { Match, Delete, Insert, Substitute }

        private readonly struct DiffOp
        {
            public DiffOp(DiffOpKind kind, int leftIndex, int rightIndex)
            {
                Kind = kind; LeftIndex = leftIndex; RightIndex = rightIndex;
            }

            public DiffOpKind Kind { get; }
            public int LeftIndex { get; }
            public int RightIndex { get; }
        }
    }
}
