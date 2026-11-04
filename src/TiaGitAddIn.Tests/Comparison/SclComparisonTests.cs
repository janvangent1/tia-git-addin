using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Services.Comparison;
using TiaGitAddIn.Tests.UI;
using TiaGitAddIn.UI.Mapping;
using TiaGitAddIn.UI.ViewModels.Comparison;
using TiaGitAddIn.UI.Views.Comparison;
using Xunit;

namespace TiaGitAddIn.Tests.Comparison
{
    public sealed class SclComparisonTests
    {
        private const string Block =
            "FUNCTION_BLOCK \"ALARM\"\n" +
            "{ S7_Optimized_Access := 'TRUE' }\n" +
            "VERSION : 0.1\n" +
            "VAR_INPUT\n" +
            "  bo_Reset : Bool;\n" +
            "END_VAR\n" +
            "BEGIN\n" +
            "  REGION ABORT\n" +
            "    // stop\n" +
            "    IF #bo_Reset THEN\n" +
            "        #bo_Alarm := FALSE;\n" +
            "    END_IF;\n" +
            "  END_REGION\n" +
            "END_FUNCTION_BLOCK\n";

        [Fact]
        public async Task DeclarationAndStatementChangesAreListed()
        {
            string right = Block
                .Replace("bo_Reset : Bool;", "bo_Reset : Word;")
                .Replace("#bo_Alarm := FALSE;", "#bo_Alarm := TRUE;");

            SclPresentation presentation = await Compare(Block, right);

            SclChange reset = Find(presentation, "bo_Reset : Word");
            Assert.Equal(SclChangeStatus.Changed, reset.Status);
            Assert.Contains("Bool", reset.Detail);

            SclChange statement = Find(presentation, "#bo_Alarm := TRUE");
            Assert.Equal(SclChangeStatus.Changed, statement.Status);
            Assert.Contains("FALSE", statement.Detail);

            SclChange region = Find(presentation, "REGION ABORT");
            Assert.Equal(SclChangeStatus.Changed, region.Status);
            Assert.Contains(region.Children, child => child.Title.Contains("#bo_Alarm := TRUE"));
        }

        [Fact]
        public async Task FormattingWhitespaceDoesNotCountAsAChange()
        {
            string right = Block.Replace("#bo_Alarm := FALSE;", "#bo_Alarm:=FALSE;");

            SclPresentation presentation = await Compare(Block, right);

            Assert.Equal(SclChangeStatus.Unchanged, presentation.Changes[0].Status);
        }

        [Fact]
        public async Task CommentChangeIsKeptSeparateFromTheStatement()
        {
            string right = Block.Replace("// stop", "// halt");

            SclPresentation presentation = await Compare(Block, right);

            SclChange comment = Find(presentation, "// halt");
            Assert.Equal(SclChangeStatus.Changed, comment.Status);
            Assert.Contains("stop", comment.Detail);
            Assert.Equal(SclChangeStatus.Unchanged, Find(presentation, "#bo_Alarm := FALSE").Status);
        }

        [Fact]
        public async Task UniqueDeclarationRenameIsReported()
        {
            string left = "FUNCTION \"Scale\" : Void\nVAR_INPUT\n  Input : Real;\nEND_VAR\nBEGIN\nEND_FUNCTION\n";
            string right = "FUNCTION \"Scale\" : Void\nVAR_INPUT\n  Value : Real;\nEND_VAR\nBEGIN\nEND_FUNCTION\n";

            SclPresentation presentation = await Compare(left, right);

            SclChange rename = Find(presentation, "Renamed Input to Value");
            Assert.Equal(SclChangeStatus.Changed, rename.Status);
        }

        [Fact]
        public async Task MultiLineConditionStaysOneStatement()
        {
            const string left =
                "FUNCTION_BLOCK \"Run\"\nBEGIN\n" +
                "IF #a AND\n  #b THEN\n" +
                "  #out := TRUE;\n" +
                "END_IF;\n" +
                "END_FUNCTION_BLOCK\n";
            string right = left.Replace("#b THEN", "#c THEN");

            SclPresentation presentation = await Compare(left, right);

            Assert.Contains(Flatten(presentation.Changes), change =>
                change.Status == SclChangeStatus.Changed && change.Title.Contains("#c"));
            Assert.DoesNotContain(Flatten(presentation.Changes), change => change.Title == "#a AND");
        }

        [Fact]
        public async Task FileWithoutABlockFallsBackToText()
        {
            var factory = new PlcComparisonResultFactory(new LineTextComparer(TextComparisonLimits.Default));
            var strategy = new SclComparisonStrategy(factory, new ComparisonDiagnosticSanitizer());
            PlcComparisonContext context = ComparisonTestData.Context(
                PlcArtifactKind.Scl, PlcComparisonMode.Structured, "not a block", "still not", "Program.scl");

            RecoverableComparisonException ex = await Assert.ThrowsAsync<RecoverableComparisonException>(
                () => strategy.CompareAsync(context, CancellationToken.None));

            var coordinator = new PlcComparisonCoordinator(
                new PlcArtifactClassifier(),
                new IPlcComparisonStrategy[] { strategy, new TextFallbackStrategy(factory) },
                factory,
                new ComparisonDiagnosticSanitizer());
            PlcRevision left = ComparisonTestData.TextRevision(PlcRevisionSide.Left, "not a block", "Program.scl");
            PlcRevision right = ComparisonTestData.TextRevision(PlcRevisionSide.Right, "still not", "Program.scl");
            PlcComparisonResult result = await coordinator.CompareAsync(left, right, CancellationToken.None);

            Assert.Contains("text", ex.Limitation, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ComparisonPresentationKind.Text, result.Presentation.Kind);
            Assert.Equal(PlcSupportLevel.Fallback, result.SupportLevel);
        }

        [Fact]
        public void NextChangeMovesToTheFollowingChangedRow()
        {
            string right = Block
                .Replace("bo_Reset : Bool;", "bo_Reset : Word;")
                .Replace("#bo_Alarm := FALSE;", "#bo_Alarm := TRUE;");
            SclPresentation presentation = SclComparer.Compare(SclParser.Parse(Block), SclParser.Parse(right));
            var result = new PlcComparisonResult(
                PlcArtifactKind.Scl, PlcComparisonMode.Structured, PlcComparisonMode.Structured,
                PlcSupportLevel.Full, string.Empty, Array.Empty<PlcComparisonDiagnostic>(), presentation,
                new ComparisonRawText(Block, right, false, false));
            var viewModel = new SclComparisonViewModel(presentation, ComparisonViewModelMetadata.From(result));

            Assert.Contains("bo_Reset", viewModel.SelectedRow!.Title);
            viewModel.NextChangeCommand.Execute(null);
            Assert.Contains("#bo_Alarm", viewModel.SelectedRow.Title);
            viewModel.PreviousChangeCommand.Execute(null);
            Assert.Contains("bo_Reset", viewModel.SelectedRow.Title);
        }

        [Fact]
        public void TextPreferenceOpensTheColoredLines()
        {
            string right = Block.Replace("bo_Reset : Bool;", "bo_Reset : Word;");
            SclPresentation presentation = SclComparer.Compare(SclParser.Parse(Block), SclParser.Parse(right));
            var result = new PlcComparisonResult(
                PlcArtifactKind.Scl, PlcComparisonMode.Structured, PlcComparisonMode.Structured,
                PlcSupportLevel.Full, string.Empty, Array.Empty<PlcComparisonDiagnostic>(), presentation,
                new ComparisonRawText(Block, right, false, false));
            var metadata = ComparisonViewModelMetadata.From(result);

            var structure = new SclComparisonViewModel(presentation, metadata, showStructure: true);
            var text = new SclComparisonViewModel(presentation, metadata, showStructure: false);

            Assert.False(structure.OpenRawText);
            Assert.True(text.OpenRawText);

            WpfTestHost.Run(_ =>
            {
                var host = new ComparisonPresentationHost { DataContext = text };
                host.Measure(new Size(900, 700));
                host.Arrange(new Rect(0, 0, 900, 700));
                host.UpdateLayout();
                var tabs = (TabControl)host.FindName("ComparisonTabs");
                Assert.Equal(1, tabs.SelectedIndex);
            });
        }

        [Fact]
        public void DeclarationAttributesCountAsAChange()
        {
            const string left =
                "FUNCTION_BLOCK \"STD Comm MSP ET200SP\"\n" +
                "VAR_INPUT\n" +
                "  I_SwitchOnCmd : Bool;\n" +
                "END_VAR\n" +
                "BEGIN\n" +
                "END_FUNCTION_BLOCK\n";
            string right = left.Replace(
                "I_SwitchOnCmd : Bool;",
                "I_SwitchOnCmd { ExternalAccessible := 'False'} : Bool;");

            SclPresentation presentation = SclComparer.Compare(SclParser.Parse(left), SclParser.Parse(right));

            SclChange input = Find(presentation, "I_SwitchOnCmd");
            Assert.Equal(SclChangeStatus.Changed, input.Status);
            Assert.Contains("ExternalAccessible", input.Title);
            Assert.Contains("I_SwitchOnCmd : Bool", input.Detail);
        }

        [Fact]
        public void BlockAttributeChangeIsShown()
        {
            const string left =
                "FUNCTION_BLOCK \"Run\"\n{ S7_Optimized_Access := 'TRUE' }\nBEGIN\nEND_FUNCTION_BLOCK\n";
            string right = left.Replace("'TRUE'", "'FALSE'");

            SclPresentation presentation = SclComparer.Compare(SclParser.Parse(left), SclParser.Parse(right));

            SclChange attribute = Find(presentation, "S7_Optimized_Access");
            Assert.Equal(SclChangeStatus.Changed, attribute.Status);
            Assert.Contains("TRUE", attribute.Detail);
        }

        [Fact]
        public void WorkspaceMspBlockKeepsAttributeChanges()
        {
            const string withAttributes = @"C:\TiaGit\HCM1500\PLC\PLC\Program blocks\003. STD Library\STD Comm MSP ET200SP.scl";
            const string withoutAttributes = @"C:\TiaGit\HCM1500\PLC\Program blocks\003. STD Library\STD Comm MSP ET200SP.scl";
            if (!File.Exists(withAttributes) || !File.Exists(withoutAttributes))
            {
                return;
            }

            SclPresentation presentation = SclComparer.Compare(
                SclParser.Parse(File.ReadAllText(withoutAttributes)),
                SclParser.Parse(File.ReadAllText(withAttributes)));

            SclChange input = Find(presentation, "I_MsgFrameInAddr");
            Assert.Equal(SclChangeStatus.Changed, input.Status);
            Assert.Contains("ExternalAccessible", input.Title);
        }

        [Fact]
        public void WorkspaceSclFilesExposeABlock()
        {
            const string root = @"C:\TiaGit\HCM1500\PLC";
            if (!Directory.Exists(root))
            {
                return;
            }

            string[] files = Directory.GetFiles(root, "*.scl", SearchOption.AllDirectories);
            var failures = new List<string>();
            foreach (string file in files)
            {
                SclDocument document = SclParser.Parse(File.ReadAllText(file));
                if (document.Blocks.Count == 0)
                {
                    failures.Add(file + " (" + document.PartialReason + ")");
                }
            }

            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures.Take(12)));
        }

        private static async Task<SclPresentation> Compare(string left, string right)
        {
            var factory = new PlcComparisonResultFactory(new LineTextComparer(TextComparisonLimits.Default));
            var strategy = new SclComparisonStrategy(factory, new ComparisonDiagnosticSanitizer());
            PlcComparisonContext context = ComparisonTestData.Context(
                PlcArtifactKind.Scl, PlcComparisonMode.Structured, left, right, "Program.scl");
            PlcComparisonResult result = await strategy.CompareAsync(context, CancellationToken.None);
            Assert.Equal(PlcComparisonMode.Structured, result.ActualMode);
            Assert.Equal(PlcSupportLevel.Full, result.SupportLevel);
            return (SclPresentation)result.Presentation;
        }

        private static SclChange Find(SclPresentation presentation, string titlePart)
        {
            SclChange? found = Flatten(presentation.Changes)
                .FirstOrDefault(change => change.Title.IndexOf(titlePart, StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.NotNull(found);
            return found!;
        }

        private static IEnumerable<SclChange> Flatten(IEnumerable<SclChange> changes)
        {
            foreach (SclChange change in changes)
            {
                yield return change;
                foreach (SclChange child in Flatten(change.Children))
                {
                    yield return child;
                }
            }
        }
    }
}
