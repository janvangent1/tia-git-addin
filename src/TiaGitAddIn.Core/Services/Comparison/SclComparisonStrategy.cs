using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models.Comparison;

namespace TiaGitAddIn.Services.Comparison
{
    /// <summary>
    /// Structured comparison for SCL. A file that has no block falls back to the colored text diff.
    /// </summary>
    public sealed class SclComparisonStrategy : IPlcComparisonStrategy
    {
        private readonly PlcComparisonResultFactory resultFactory;
        private readonly ComparisonDiagnosticSanitizer sanitizer;

        public SclComparisonStrategy(PlcComparisonResultFactory resultFactory, ComparisonDiagnosticSanitizer sanitizer)
        {
            this.resultFactory = resultFactory ?? throw new ArgumentNullException(nameof(resultFactory));
            this.sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        }

        public IReadOnlyCollection<PlcArtifactKind> SupportedKinds { get; } = new[] { PlcArtifactKind.Scl };

        public Task<PlcComparisonResult> CompareAsync(PlcComparisonContext context, CancellationToken cancellationToken)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            cancellationToken.ThrowIfCancellationRequested();
            string leftText = context.Request.Left.IsMissing ? string.Empty : context.Request.Left.Text ?? string.Empty;
            string rightText = context.Request.Right.IsMissing ? string.Empty : context.Request.Right.Text ?? string.Empty;
            SclDocument left = SclParser.Parse(leftText);
            SclDocument right = SclParser.Parse(rightText);
            bool leftHasBlock = left.Blocks.Count > 0;
            bool rightHasBlock = right.Blocks.Count > 0;
            if (!leftHasBlock && !rightHasBlock)
            {
                throw new RecoverableComparisonException(
                    "This file has no SCL block, so it is shown as text.",
                    sanitizer.ForUser(
                        "CMP-SCL-PARSE",
                        PlcDiagnosticSeverity.Warning,
                        "No FUNCTION, FUNCTION_BLOCK, ORGANIZATION_BLOCK, DATA_BLOCK, or TYPE was found."));
            }

            if ((leftText.Length > 0 && !leftHasBlock) || (rightText.Length > 0 && !rightHasBlock))
            {
                left = leftHasBlock ? left : new SclDocument(left.Namespace, left.Blocks, true, "One side has no SCL block.");
                right = rightHasBlock ? right : new SclDocument(right.Namespace, right.Blocks, true, "One side has no SCL block.");
            }

            SclPresentation presentation = SclComparer.Compare(left, right);
            bool partial = left.IsPartial || right.IsPartial;
            string limitation = partial
                ? FirstReason(left, right)
                : string.Empty;
            IReadOnlyList<PlcComparisonDiagnostic> diagnostics = context.Request.Pair.Diagnostics;
            if (partial)
            {
                var combined = new List<PlcComparisonDiagnostic>(diagnostics)
                {
                    sanitizer.ForUser("CMP-SCL-PARTIAL", PlcDiagnosticSeverity.Warning, limitation),
                };
                diagnostics = combined;
            }

            return Task.FromResult(resultFactory.CreateSemantic(
                context,
                PlcComparisonMode.Structured,
                partial ? PlcSupportLevel.Partial : PlcSupportLevel.Full,
                limitation,
                diagnostics,
                presentation));
        }

        private static string FirstReason(SclDocument left, SclDocument right)
        {
            if (left.PartialReason.Length > 0)
            {
                return left.PartialReason;
            }

            if (right.PartialReason.Length > 0)
            {
                return right.PartialReason;
            }

            return "Part of this SCL file could not be read. The parts that were read are listed below.";
        }
    }
}
