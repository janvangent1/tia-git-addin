using System.Collections.Generic;
using System.Linq;
using TiaGitAddIn.Models.Lad;
using TiaGitAddIn.Models.Sact;
using TiaGitAddIn.UI.ViewModels;
using Xunit;

namespace TiaGitAddIn.Tests.UI
{
    public sealed class DiffPdfContentTests
    {
        [Fact]
        public void LadderPdfListsTheChangedContactAndCoil()
        {
            var pair = new LadNetworkPairViewModel(new LadNetworkPairLayout
            {
                NetworkNumber = 1,
                Title = "Start",
                DiffState = CompareState.Changed,
                Right = new LadNetworkLayout
                {
                    Elements = new List<LadElementLayout>
                    {
                        new LadElementLayout { Row = 0, Column = 0, ElementType = LadElementType.Powerrail },
                        new LadElementLayout
                        {
                            Row = 0,
                            Column = 1,
                            ElementType = LadElementType.Contact,
                            Operand = "StartButton",
                            DiffState = CompareState.Changed
                        },
                        new LadElementLayout
                        {
                            Row = 0,
                            Column = 2,
                            ElementType = LadElementType.Coil,
                            Operand = "Motor"
                        }
                    }
                }
            });

            var lines = DiffPdfContent.Ladder(new[] { pair });

            Assert.Contains(lines, line => line.Marker == "#" && line.Text.Contains("Network 1") && line.Text.Contains("Start"));
            Assert.Contains(lines, line => line.Text == "Previous side is not in this revision.");
            Assert.Contains(
                lines,
                line => line.Marker == "~" && line.Text.Contains("StartButton [changed]") && line.Text.Contains("(Motor)"));
            Assert.DoesNotContain(lines, line => line.Text.Contains("Powerrail"));
        }
    }
}
