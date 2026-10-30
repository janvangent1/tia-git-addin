using System;
using TiaGitAddIn.Models.Comparison;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.Mapping
{
    /// <summary>Maps a <see cref="SclPresentation"/> to <see cref="SclComparisonViewModel"/>.</summary>
    public sealed class SclPresentationViewModelFactory : IComparisonPresentationViewModelFactory
    {
        private readonly Func<bool> showStructure;

        public SclPresentationViewModelFactory(Func<bool>? showStructure = null)
        {
            this.showStructure = showStructure ?? (() => true);
        }

        public bool CanMap(ComparisonPresentation presentation) => presentation is SclPresentation;

        public ComparisonPresentationViewModel Map(PlcComparisonResult result, ComparisonViewModelMetadata metadata)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            return new SclComparisonViewModel((SclPresentation)result.Presentation, metadata, showStructure());
        }
    }
}
