using System;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.VersionControl;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.Entry
{
    public sealed class GitVciEditorProvider(TiaPortal tiaPortal) : VciEditorAddInProvider
    {
        private readonly TiaPortal tiaPortal = tiaPortal ?? throw new ArgumentNullException(nameof(tiaPortal));

        public override VciWorkspaceViewAddInProvider GetVciWorkspaceViewAddInProvider()
        {
            FileLogger logger = new();
            var launchService = new GitPanelLaunchService
            {
                HmiExchange = new PortalHmiDeviceExchange(tiaPortal)
            };
            return new GitVciWorkspaceViewProvider(launchService, logger);
        }
    }
}
