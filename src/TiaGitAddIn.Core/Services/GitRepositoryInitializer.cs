using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services
{
    public sealed class GitRepositoryInitializer(IGitProcessRunner runner) : IGitRepositoryInitializer
    {
        private readonly IGitProcessRunner runner = runner ?? throw new ArgumentNullException(nameof(runner));

        public async Task<OperationResult> InitializeAsync(
            string workspacePath,
            string gitExecutablePath,
            CancellationToken cancellationToken = default)
        {
            ValidationResult validation = PathValidator.Validate(workspacePath);
            if (!validation.IsValid)
            {
                return OperationResult.Fail(validation.ErrorMessage);
            }

            if (!Directory.Exists(workspacePath))
            {
                return OperationResult.Fail("The VCI workspace folder does not exist.");
            }

            string executable = string.IsNullOrWhiteSpace(gitExecutablePath) ? "git" : gitExecutablePath;
            GitProcessResult result = await runner.RunAsync(
                executable,
                workspacePath,
                new[] { "init" },
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                return OperationResult.Ok("Git repository created.");
            }

            string? explained = GitUserMessages.Explain(result.StandardError + " " + result.StandardOutput);
            if (explained == GitUserMessages.GitNotFound)
            {
                return OperationResult.Fail(GitUserMessages.GitNotFoundBeforeRepository);
            }

            string detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            return OperationResult.Fail("Unable to create the Git repository.", detail);
        }
    }
}
