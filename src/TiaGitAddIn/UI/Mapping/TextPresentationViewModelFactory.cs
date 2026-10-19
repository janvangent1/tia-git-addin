using System;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.Models.Hmi;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.Mapping
{
    /// <summary>Maps a <see cref="TextPresentation"/> (Text mode) to <see cref="TextComparisonViewModel"/>.</summary>
    public sealed class TextPresentationViewModelFactory : IComparisonPresentationViewModelFactory
    {
        private readonly Func<bool> showScreenPreview;

        public TextPresentationViewModelFactory(Func<bool>? showScreenPreview = null)
        {
            this.showScreenPreview = showScreenPreview ?? (() => true);
        }

        public bool CanMap(ComparisonPresentation presentation) => presentation is TextPresentation;

        public ComparisonPresentationViewModel Map(PlcComparisonResult result, ComparisonViewModelMetadata metadata)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            HmiFilePreview? preview = null;
            if (metadata.RawText != null)
            {
                HmiFilePreview.TryCreate(metadata.RawText.LeftText, metadata.RawText.RightText, out preview);
                preview = HmiFilePreview.ApplyScreenChoice(preview, showScreenPreview());
            }

            return new TextComparisonViewModel((TextPresentation)result.Presentation, metadata, preview);
        }
    }
}
