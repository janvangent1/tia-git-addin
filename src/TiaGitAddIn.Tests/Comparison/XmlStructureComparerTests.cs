using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Services.Comparison;
using Xunit;

namespace TiaGitAddIn.Tests.Comparison
{
    public sealed class XmlStructureComparerTests
    {
        [Fact]
        public void AttributeOrderAndWhitespaceAreNotChanges()
        {
            const string left = "<Screen name=\"Overview\" width=\"800\">\r\n  <Item name=\"Start\" text=\"Run\" />\r\n</Screen>";
            const string right = "<Screen width=\"800\" name=\"Overview\"><Item text=\"Run\" name=\"Start\"></Item></Screen>";

            Assert.True(XmlStructureComparer.TryCompare(new ComparisonRawText(left, right, false, false), out TextPresentation presentation));

            Assert.Contains(presentation.Lines, line => line.Text == "No element or attribute changes.");
        }

        [Fact]
        public void AChangedAttributeIsShownByNameInsteadOfTheWholeLine()
        {
            const string left = "<Screen name=\"Overview\"><Item name=\"Start\" text=\"Run\" width=\"40\" /></Screen>";
            const string right = "<Screen name=\"Overview\"><Item name=\"Start\" text=\"Halt\" width=\"40\" /></Screen>";

            Assert.True(XmlStructureComparer.TryCompare(new ComparisonRawText(left, right, false, false), out TextPresentation presentation));

            Assert.Contains(presentation.Lines, line => line.Kind == TextDiffLineKind.Removed && line.Text.Contains("Item[@name=\"Start\"]/@text = Run"));
            Assert.Contains(presentation.Lines, line => line.Kind == TextDiffLineKind.Added && line.Text.Contains("Item[@name=\"Start\"]/@text = Halt"));
            Assert.DoesNotContain(presentation.Lines, line => line.Text.Contains("width"));
        }

        [Fact]
        public void AnAddedNamedElementIsOneLine()
        {
            const string left = "<Items><Item name=\"Start\" /></Items>";
            const string right = "<Items><Item name=\"Start\" /><Item name=\"Lamp\"><Bit name=\"On\" /></Item></Items>";

            Assert.True(XmlStructureComparer.TryCompare(new ComparisonRawText(left, right, false, false), out TextPresentation presentation));

            Assert.Contains(presentation.Lines, line => line.Kind == TextDiffLineKind.Added && line.Text.Contains("Added Items/Item[@name=\"Lamp\"]"));
            Assert.DoesNotContain(presentation.Lines, line => line.Text.Contains("Bit"));
        }

        [Fact]
        public void PlainTextStaysOnTheLineDiff()
        {
            Assert.False(XmlStructureComparer.TryCompare(new ComparisonRawText("left", "right", false, false), out _));
        }

        [Fact]
        public async Task GenericXmlUsesTheStructuralComparison()
        {
            const string left = "<Screen name=\"Overview\" width=\"800\" />";
            const string right = "<Screen name=\"Overview\" width=\"1024\" />";
            var descriptor = new PlcArtifactDescriptor(PlcArtifactKind.GenericXml, PlcComparisonMode.Text, new[] { "xml" });
            var pair = new PlcArtifactPairDescriptor(
                descriptor, descriptor, PlcArtifactKind.GenericXml, PlcComparisonMode.Text, PlcPairChangeKind.Modified, string.Empty);
            var context = new PlcComparisonContext(
                new PlcComparisonRequest(
                    ComparisonTestData.TextRevision(PlcRevisionSide.Left, left),
                    ComparisonTestData.TextRevision(PlcRevisionSide.Right, right),
                    pair),
                new ComparisonRawText(left, right, false, false));

            PlcComparisonResult result = await new TextFallbackStrategy(new PlcComparisonResultFactory(new LineTextComparer(TextComparisonLimits.Default)))
                .CompareAsync(context, CancellationToken.None);

            Assert.Equal(XmlStructureComparer.Limitation, result.Limitation);
            TextPresentation presentation = Assert.IsType<TextPresentation>(result.Presentation);
            Assert.Contains(presentation.Lines, line => line.Text.Contains("@width = 1024"));
        }

        [Fact]
        public async Task GenericXmlCanStayOnTheLineDiff()
        {
            const string left = "<Screen name=\"Overview\" width=\"800\" />";
            const string right = "<Screen name=\"Overview\" width=\"1024\" />";
            var descriptor = new PlcArtifactDescriptor(PlcArtifactKind.GenericXml, PlcComparisonMode.Text, new[] { "xml" });
            var pair = new PlcArtifactPairDescriptor(
                descriptor, descriptor, PlcArtifactKind.GenericXml, PlcComparisonMode.Text, PlcPairChangeKind.Modified, string.Empty);
            var context = new PlcComparisonContext(
                new PlcComparisonRequest(
                    ComparisonTestData.TextRevision(PlcRevisionSide.Left, left),
                    ComparisonTestData.TextRevision(PlcRevisionSide.Right, right),
                    pair),
                new ComparisonRawText(left, right, false, false));

            PlcComparisonResult result = await new TextFallbackStrategy(
                    new PlcComparisonResultFactory(new LineTextComparer(TextComparisonLimits.Default)),
                    () => false)
                .CompareAsync(context, CancellationToken.None);

            Assert.Equal("GenericXml semantic comparison is unavailable.", result.Limitation);
            TextPresentation presentation = Assert.IsType<TextPresentation>(result.Presentation);
            Assert.Contains(presentation.Lines, line => line.Text.Contains("1024"));
            Assert.DoesNotContain(presentation.Lines, line => line.Text.Contains("@width"));
        }
    }
}
