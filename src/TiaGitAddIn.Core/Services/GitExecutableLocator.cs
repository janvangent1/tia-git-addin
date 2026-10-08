using System;
using System.Collections.Generic;
using System.IO;

namespace TiaGitAddIn.Services
{
    public static class GitExecutableLocator
    {
        public static string Resolve() =>
            Resolve(Environment.GetEnvironmentVariable("PATH"), DefaultInstallCandidates());

        public static string Resolve(string? pathVariable, IEnumerable<string> installCandidates)
        {
            string? fromPath = FindOnPath(pathVariable);
            if (fromPath != null)
            {
                return fromPath;
            }

            if (installCandidates != null)
            {
                foreach (string candidate in installCandidates)
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return "git";
        }

        private static IEnumerable<string> DefaultInstallCandidates()
        {
            foreach (Environment.SpecialFolder folder in new[]
            {
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            })
            {
                string root = Environment.GetFolderPath(folder);
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                yield return Path.Combine(root, "Git", "cmd", "git.exe");
            }
        }

        private static string? FindOnPath(string? pathVariable)
        {
            if (pathVariable == null || pathVariable.Trim().Length == 0)
            {
                return null;
            }

            string searchPath = pathVariable;
            foreach (string entry in searchPath.Split(Path.PathSeparator))
            {
                string directory = entry.Trim().Trim('"');
                if (directory.Length == 0)
                {
                    continue;
                }

                string candidate = Path.Combine(directory, "git.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
