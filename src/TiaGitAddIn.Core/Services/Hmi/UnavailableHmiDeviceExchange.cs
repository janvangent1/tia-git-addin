using System;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services.Hmi
{
    public sealed class UnavailableHmiDeviceExchange : IHmiDeviceExchange
    {
        public const string Message =
            "Export and import of an HMI device are available when this panel is opened from TIA Portal.";

        public void RememberCallerContext()
        {
        }

        public Task<OperationResult> ExportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Fail(Message));

        public Task<OperationResult> ImportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default) =>
            Task.FromResult(OperationResult.Fail(Message));
    }
}
