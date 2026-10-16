using System;
using TiaGitAddIn.Logging;

namespace TiaGitAddIn.Services
{
    internal static class HmiExportTrace
    {
        private static readonly FileLogger Logger = new FileLogger();

        public static void Info(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Logger.Info("HMI export: " + message);
        }

        public static void Error(Exception exception)
        {
            Logger.Error("HMI export failed.", exception);
        }
    }
}
