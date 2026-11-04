using System;
using System.Collections.ObjectModel;
using System.Linq;
using TiaGitAddIn.Models.Sact;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI.ViewModels.Comparison;

namespace TiaGitAddIn.UI.ViewModels
{
    /// <summary>
    /// Result-only LAD block view model, built by <see cref="Mapping.LadPresentationViewModelFactory"/>
    /// directly from an already-produced <c>SactCompareResult</c> clone and
    /// <see cref="InterfaceComparisonViewModel"/>. It has no git/SACT dependency: the network layout and
    /// interface comparison are computed once, in the constructor, from data the coordinator/mapper pipeline
    /// already loaded and compared.
    /// </summary>
    public class LadDiffViewModel : ViewModelBase
    {
        private readonly IAddInLogger logger;

        private bool isLadDiffLoaded;
        private string ladDiffError = string.Empty;
        private double zoom = 1;
        private LadNetworkPairViewModel? selectedNetwork;

        public LadDiffViewModel(SactCompareResult result, InterfaceComparisonViewModel interfaceComparison,
            IAddInLogger logger, IUiDispatcher? uiDispatcher)
            : base(uiDispatcher)
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            Networks = new ObservableCollection<LadNetworkPairViewModel>(
                LadLayoutEngine.LayoutAll(result).Select(layout => new LadNetworkPairViewModel(layout)));
            InterfaceComparison = interfaceComparison ?? throw new ArgumentNullException(nameof(interfaceComparison));
            IsLadDiffLoaded = true;
            if (Networks.Count > 0)
            {
                selectedNetwork = Networks[0];
            }

            ZoomInCommand = new RelayCommand(_ => Zoom = Math.Min(2.5, Zoom + 0.25));
            ZoomOutCommand = new RelayCommand(_ => Zoom = Math.Max(0.25, Zoom - 0.25));
            ResetZoomCommand = new RelayCommand(_ => Zoom = 1);
        }

        public RelayCommand ZoomInCommand { get; }

        public RelayCommand ZoomOutCommand { get; }

        public RelayCommand ResetZoomCommand { get; }

        public double Zoom
        {
            get => zoom;
            set
            {
                if (SetProperty(ref zoom, value))
                {
                    OnPropertyChanged(nameof(ZoomText));
                }
            }
        }

        public LadNetworkPairViewModel? SelectedNetwork
        {
            get => selectedNetwork;
            set => SetProperty(ref selectedNetwork, value);
        }

        public string ZoomText => Math.Round(Zoom * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        public ObservableCollection<LadNetworkPairViewModel> Networks { get; }

        /// <summary>The deep interface comparison for this LAD block.</summary>
        public InterfaceComparisonViewModel InterfaceComparison { get; }

        public bool IsLadDiffLoaded
        {
            get => isLadDiffLoaded;
            set => SetProperty(ref isLadDiffLoaded, value);
        }

        public string LadDiffError
        {
            get => ladDiffError;
            set => SetProperty(ref ladDiffError, value);
        }
    }
}
