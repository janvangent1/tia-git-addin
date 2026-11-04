using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TiaGitAddIn.Models.Lad;
using TiaGitAddIn.Models.Sact;
using TiaGitAddIn.Reporting;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.ViewModels
{
    /// <summary>
    /// Turns the open comparison into PDF lines. A ladder becomes one line per rung,
    /// and a screen lists the highlighted buttons and I/O fields.
    /// </summary>
    internal static class DiffPdfContent
    {
        public static IReadOnlyList<DiffPdfTextLine> Ladder(IEnumerable<LadNetworkPairViewModel> networks)
        {
            var lines = new List<DiffPdfTextLine>();
            List<LadNetworkPairViewModel> list = networks?.ToList() ?? new List<LadNetworkPairViewModel>();
            if (list.Count == 0)
            {
                lines.Add(new DiffPdfTextLine(" ", "This ladder has no networks."));
                return lines;
            }

            foreach (LadNetworkPairViewModel network in list)
            {
                string title = "Network " + network.NetworkNumber.ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(network.Title))
                {
                    title += "  " + network.Title.Trim();
                }

                title += "  " + Word(network.DiffState);
                lines.Add(new DiffPdfTextLine("#", title));
                if (!string.IsNullOrWhiteSpace(network.Comment))
                {
                    lines.Add(new DiffPdfTextLine(" ", network.Comment.Trim()));
                }

                AppendSide(lines, "Previous", network.Left);
                AppendSide(lines, "Current", network.Right);
                lines.Add(new DiffPdfTextLine(" ", string.Empty));
            }

            return lines;
        }

        public static IReadOnlyList<DiffPdfTextLine> Screen(TextComparisonViewModel text)
        {
            HmiScreenPaneViewModel pane = text.ScreenPanes[text.ScreenPanes.Count - 1];
            var lines = new List<DiffPdfTextLine>
            {
                new DiffPdfTextLine("#", pane.Title)
            };
            bool any = false;
            foreach (HmiScreenBoxViewModel box in pane.Boxes)
            {
                if (box.Change == "Unchanged")
                {
                    continue;
                }

                any = true;
                string marker = box.Change == "Added" ? "+" : box.Change == "Removed" ? "-" : "~";
                lines.Add(new DiffPdfTextLine(marker, box.ToolTipText));
            }

            if (!any)
            {
                lines.Add(new DiffPdfTextLine(" ", "No button or I/O field is highlighted."));
            }

            return lines;
        }

        private static void AppendSide(List<DiffPdfTextLine> lines, string side, LadNetworkViewModel? network)
        {
            if (network == null)
            {
                lines.Add(new DiffPdfTextLine(" ", side + " side is not in this revision."));
                return;
            }

            IEnumerable<IGrouping<int, LadElementViewModel>> rows = network.Elements
                .Where(element => element.ElementType != LadElementType.Powerrail)
                .GroupBy(element => (int)(element.Y))
                .OrderBy(group => group.Key);
            bool any = false;
            foreach (IGrouping<int, LadElementViewModel> row in rows)
            {
                any = true;
                List<LadElementViewModel> elements = row.OrderBy(element => element.X).ToList();
                string marker = " ";
                if (elements.Any(element => element.DiffState == CompareState.MissingOnRight))
                {
                    marker = "-";
                }
                else if (elements.Any(element => element.DiffState == CompareState.MissingOnLeft))
                {
                    marker = "+";
                }
                else if (elements.Any(element => element.DiffState == CompareState.Changed))
                {
                    marker = "~";
                }

                string text = side + "  " + string.Join("  ", elements.Select(Describe));
                lines.Add(new DiffPdfTextLine(marker, text));
            }

            if (!any)
            {
                lines.Add(new DiffPdfTextLine(" ", side + " side has no instructions."));
            }
        }

        private static string Describe(LadElementViewModel element)
        {
            string name = !string.IsNullOrWhiteSpace(element.Operand)
                ? element.Operand.Trim()
                : !string.IsNullOrWhiteSpace(element.DisplayName)
                    ? element.DisplayName.Trim()
                    : element.ElementType.ToString();
            switch (element.ElementType)
            {
                case LadElementType.NegatedContact:
                    name = "/" + name;
                    break;
                case LadElementType.PEdgeContact:
                    name = "P " + name;
                    break;
                case LadElementType.NEdgeContact:
                    name = "N " + name;
                    break;
                case LadElementType.Coil:
                    name = "(" + name + ")";
                    break;
                case LadElementType.NegatedCoil:
                    name = "(/" + name + ")";
                    break;
                case LadElementType.OrBranch:
                    name = "OR";
                    break;
                case LadElementType.Call:
                case LadElementType.Box:
                case LadElementType.ComparatorBox:
                case LadElementType.TemplatedContact:
                case LadElementType.TemplatedCoil:
                    if (!string.IsNullOrWhiteSpace(element.Equation))
                    {
                        name = name + " " + element.Equation.Trim();
                    }

                    break;
            }

            if (element.DiffState != CompareState.Equal)
            {
                name += " [" + Word(element.DiffState) + "]";
            }

            return name;
        }

        private static string Word(CompareState state)
        {
            switch (state)
            {
                case CompareState.Changed:
                    return "changed";
                case CompareState.MissingOnLeft:
                    return "added";
                case CompareState.MissingOnRight:
                    return "removed";
                default:
                    return "unchanged";
            }
        }
    }
}
