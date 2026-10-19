using System;
using System.Threading;
using Siemens.Engineering;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services.Hmi;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Stand-in used only when Siemens.Engineering.Startdrive.dll is not installed.
    /// The real <c>HardwareExchange.cs</c> is compiled instead when that library is present.
    /// </summary>
    internal static class HardwareExchange
    {
        private const string Missing =
            "SINAMICS Startdrive Openness is not installed, so hardware export and import are unavailable.";

        public static OperationResult Export(
            TiaPortal portal,
            string repositoryPath,
            CancellationToken ct,
            Action<string> report,
            ExportCategorySet? categories = null)
        {
            return OperationResult.Fail(Missing);
        }

        public static OperationResult Import(
            TiaPortal portal,
            string repositoryPath,
            CancellationToken ct,
            Action<string> report)
        {
            return OperationResult.Fail(Missing);
        }
    }
}
