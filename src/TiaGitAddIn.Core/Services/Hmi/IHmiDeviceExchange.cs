using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services.Hmi
{
    /// <summary>
    /// Copies Unified and WinCC Comfort panels between the open TIA project and the Git working tree.
    /// This is separate from the VCI workspace, which cannot contain an HMI device.
    /// </summary>
    public interface IHmiDeviceExchange
    {
        /// <summary>
        /// Remembers the thread that opened the panel so later export and import calls
        /// can return to the TIA Portal thread that owns the project.
        /// </summary>
        void RememberCallerContext();

        Task<OperationResult> ExportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default);

        Task<OperationResult> ExportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default);

        Task<OperationResult> ImportAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default);

        /// <summary>
        /// Screens and tag tables in the working tree that differ from the open project.
        /// An empty list means those files match. Other HMI and hardware files are not included.
        /// </summary>
        Task<IReadOnlyList<string>> ListImportDifferencesAsync(string repositoryPath, CancellationToken ct = default);

        Task<OperationResult> ImportHardwareAsync(string repositoryPath, IProgress<string> progress, CancellationToken ct = default);
    }
}
