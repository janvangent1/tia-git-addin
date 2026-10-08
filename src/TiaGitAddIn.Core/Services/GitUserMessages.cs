using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services
{
    public static class GitUserMessages
    {
        public const string GitNotFound =
            "Git was not found. Open Settings, click Browse, and select git.exe. It is usually at C:\\Program Files\\Git\\cmd\\git.exe.";

        public const string IdentityRequired =
            "Git needs your name and email. Open Settings, enter them, and click Save Settings.";

        public const string RepositoryInitDeclined =
            "The Git panel was not opened. This VCI workspace is not a Git repository yet.";

        public const string GitNotFoundBeforeRepository =
            "Git was not found, so no repository was created. Install Git for Windows. git.exe is usually at C:\\Program Files\\Git\\cmd\\git.exe.";

        public static string Describe(OperationResult result)
        {
            if (result.Success)
            {
                return result.DisplayMessage;
            }

            return Explain(result.Message + " " + result.Detail) ?? result.DisplayMessage;
        }

        public static string DescribeException(string? message)
        {
            return Explain(message) ?? "Error: " + message;
        }

        public static string? Explain(string? text)
        {
            if (text == null || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            string value = text;
            if (Contains(value, "cannot find the file specified") || Contains(value, "not recognized as an internal or external command"))
            {
                return GitNotFound;
            }

            if (Contains(value, "tell me who you are") || Contains(value, "unable to auto-detect email"))
            {
                return IdentityRequired;
            }

            return null;
        }

        private static bool Contains(string text, string value) =>
            text.IndexOf(value, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
