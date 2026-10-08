using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;

namespace TiaGitAddIn.Services
{
    public sealed class GitService(
        IGitProcessRunner runner,
        OperationSerializer serializer,
        string gitExecutablePath,
        string repositoryRoot) : IGitService
    {
        private const string PrettyCommitFormat = "%H%x1f%an%x1f%aI%x1f%s%x1f%P";
        // git branch --format uses for-each-ref placeholders. %09 is a tab; %x1f is git-log
        // pretty syntax and is printed literally, which makes the branch list look empty.
        private const string BranchListFormat = "%(HEAD)%09%(refname:short)%09%(upstream:short)%09%(upstream:track)";
        private static readonly Regex CommitHashPattern = new Regex("^[0-9a-fA-F]{7,64}$", RegexOptions.Compiled);

        public async Task<GitStatus> GetStatusAsync(CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "status", "--porcelain=v1", "-b" },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read Git status.");
            return GitOutputParser.ParseStatus(result.StandardOutput);
        }

        public async Task<OperationResult> StageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                return OperationResult.Ok("No files to stage.");
            }

            foreach (string path in filePaths)
            {
                ValidationResult validation = PathValidator.Validate(path);
                if (!validation.IsValid)
                {
                    return OperationResult.Fail(validation.ErrorMessage);
                }
            }

            List<string> args = new() { "add", "--" };
            args.AddRange(filePaths);

            GitProcessResult result = await RunExclusiveAsync(args, ct).ConfigureAwait(false);

            return ToOperationResult(result, "Files staged.", "Unable to stage files.");
        }

        public async Task<OperationResult> UnstageAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                return OperationResult.Ok("No files to unstage.");
            }

            foreach (string path in filePaths)
            {
                ValidationResult validation = PathValidator.Validate(path);
                if (!validation.IsValid)
                {
                    return OperationResult.Fail(validation.ErrorMessage);
                }
            }

            List<string> args = new() { "restore", "--staged", "--" };
            args.AddRange(filePaths);

            GitProcessResult result = await RunExclusiveAsync(args, ct).ConfigureAwait(false);

            return ToOperationResult(result, "Files unstaged.", "Unable to unstage files.");
        }

        public async Task<OperationResult> StageAllAsync(CancellationToken ct = default)
        {
            GitProcessResult result = await RunExclusiveAsync(
                new[] { "add", "-A" },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "All changes staged.", "Unable to stage all files.");
        }

        public async Task<OperationResult> CommitAsync(string message, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return OperationResult.Fail("Commit message is required.");
            }

            GitProcessResult result = await RunExclusiveAsync(
                new[] { "commit", "-m", message },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "Commit created.", "Unable to create commit.");
        }

        public async Task<OperationResult> FetchAsync(string? remote = null, CancellationToken ct = default)
        {
            GitProcessResult result = await RunExclusiveAsync(
                new[] { "fetch", remote ?? "origin" },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "Fetch completed.", "Unable to fetch from remote.");
        }

        public async Task<OperationResult> PullAsync(string? remote = null, string? branch = null, CancellationToken ct = default)
        {
            List<string> args = new() { "pull", remote ?? "origin" };
            if (!string.IsNullOrWhiteSpace(branch))
            {
                args.Add(branch!);
            }

            GitProcessResult result = await RunExclusiveAsync(args, ct).ConfigureAwait(false);

            return ToOperationResult(result, "Pull completed.", "Unable to pull from remote.");
        }

        public async Task<OperationResult> PushAsync(string? remote = null, string? branch = null, CancellationToken ct = default)
        {
            List<string> args = new() { "push", remote ?? "origin" };
            if (!string.IsNullOrWhiteSpace(branch))
            {
                args.Add(branch!);
            }

            GitProcessResult result = await RunExclusiveAsync(args, ct).ConfigureAwait(false);

            return ToOperationResult(result, "Push completed.", "Unable to push to remote.");
        }

        public async Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "branch", "-a", "--format=" + BranchListFormat },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read branches.");
            return GitOutputParser.ParseBranches(result.StandardOutput).ToList();
        }

        public async Task<OperationResult> CreateBranchAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return OperationResult.Fail("Branch name is required.");
            }

            if (name.StartsWith("-", StringComparison.Ordinal))
            {
                return OperationResult.Fail("Branch name cannot start with '-'.");
            }

            GitProcessResult result = await RunExclusiveAsync(
                new[] { "branch", name },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "Branch created.", "Unable to create branch.");
        }

        public async Task<OperationResult> SwitchBranchAsync(string name, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return OperationResult.Fail("Branch name is required.");
            }

            if (name.StartsWith("-", StringComparison.Ordinal))
            {
                return OperationResult.Fail("Branch name cannot start with '-'.");
            }

            GitProcessResult result = await RunExclusiveAsync(
                new[] { "switch", name },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "Branch switched.", "Unable to switch branch.");
        }

        public async Task<OperationResult> CheckoutBranchAsync(string branchName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return OperationResult.Fail("Branch name is required.");
            }

            if (branchName.StartsWith("-", StringComparison.Ordinal))
            {
                return OperationResult.Fail("Branch name cannot start with '-'.");
            }

            GitProcessResult result = await RunExclusiveAsync(
                new[] { "checkout", branchName },
                ct).ConfigureAwait(false);

            return ToOperationResult(result, "Branch checked out.", "Unable to check out branch.");
        }

        public async Task<OperationResult> RestoreCommitAsync(string commitHash, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(commitHash) || !CommitHashPattern.IsMatch(commitHash))
            {
                return OperationResult.Fail("Select a valid commit to restore.");
            }

            using (await serializer.AcquireAsync(ct).ConfigureAwait(false))
            {
                GitProcessResult statusResult = await RunAsync(
                    new[] { "status", "--porcelain=v1", "-b" },
                    ct).ConfigureAwait(false);
                if (!statusResult.IsSuccess)
                {
                    return ToOperationResult(statusResult, string.Empty, "Unable to read Git status.");
                }

                if (!GitOutputParser.ParseStatus(statusResult.StandardOutput).IsClean)
                {
                    return OperationResult.Fail(
                        "Commit or discard the current workspace changes before restoring an older commit.");
                }

                GitProcessResult branchResult = await RunAsync(
                    new[] { "branch", "-a", "--format=" + BranchListFormat },
                    ct).ConfigureAwait(false);
                if (!branchResult.IsSuccess)
                {
                    return ToOperationResult(branchResult, string.Empty, "Unable to read branches.");
                }

                string branchName = ChooseRestoreBranchName(
                    commitHash,
                    GitOutputParser.ParseBranches(branchResult.StandardOutput).Select(branch => branch.Name));

                GitProcessResult switchResult = await RunAsync(
                    new[] { "switch", "-c", branchName, commitHash },
                    ct).ConfigureAwait(false);

                string shortHash = commitHash.Substring(0, 7);
                return ToOperationResult(
                    switchResult,
                    $"Restored {shortHash} on branch {branchName}. Synchronize the workspace into the TIA project to update the blocks.",
                    "Unable to restore the selected commit.");
            }
        }

        public static string ChooseRestoreBranchName(string commitHash, IEnumerable<string> existingBranchNames)
        {
            if (string.IsNullOrWhiteSpace(commitHash) || commitHash.Length < 7)
            {
                throw new ArgumentException("Commit hash must be at least 7 characters.", nameof(commitHash));
            }

            string baseName = "restore-" + commitHash.Substring(0, 7).ToLowerInvariant();
            var existing = new HashSet<string>(existingBranchNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName))
            {
                return baseName;
            }

            for (int suffix = 2; suffix <= 100; suffix++)
            {
                string candidate = baseName + "-" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!existing.Contains(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Unable to choose a restore branch name.");
        }

        public async Task<OperationResult> DiscardAsync(IReadOnlyList<string> filePaths, CancellationToken ct = default)
        {
            if (filePaths == null || filePaths.Count == 0)
            {
                return OperationResult.Ok("No files to discard.");
            }

            foreach (string path in filePaths)
            {
                ValidationResult validation = PathValidator.Validate(path);
                if (!validation.IsValid)
                {
                    return OperationResult.Fail(validation.ErrorMessage);
                }
            }

            List<string> args = new() { "restore", "--source=HEAD", "--worktree", "--staged", "--" };
            args.AddRange(filePaths);
            GitProcessResult result = await RunExclusiveAsync(args, ct).ConfigureAwait(false);
            return ToOperationResult(result, "Discarded changes.", "Unable to discard changes.");
        }

        public async Task<OperationResult> SetLocalIdentityAsync(string name, string email, CancellationToken ct = default)
        {
            if (!IsSafeIdentityValue(name))
            {
                return OperationResult.Fail("Enter a name that does not start with '-' or contain a new line.");
            }

            if (!IsSafeIdentityValue(email) || email.IndexOf('@') < 1)
            {
                return OperationResult.Fail("Enter an email address.");
            }

            using (await serializer.AcquireAsync(ct).ConfigureAwait(false))
            {
                GitProcessResult nameResult = await RunAsync(
                    new[] { "config", "user.name", name },
                    ct).ConfigureAwait(false);
                if (!nameResult.IsSuccess)
                {
                    return ToOperationResult(nameResult, string.Empty, "Unable to save the Git name for this repository.");
                }

                GitProcessResult emailResult = await RunAsync(
                    new[] { "config", "user.email", email },
                    ct).ConfigureAwait(false);
                return ToOperationResult(
                    emailResult,
                    "Name and email saved for this repository.",
                    "Unable to save the Git email for this repository.");
            }
        }

        private static bool IsSafeIdentityValue(string? value)
        {
            if (value == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(value) || value.Length > 200 || value.StartsWith("-", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (char character in value)
            {
                if (char.IsControl(character))
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<IReadOnlyList<CommitInfo>> GetCommitLogAsync(
            int maxCount,
            CancellationToken ct = default)
        {
            if (maxCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), "Commit count must be positive.");
            }

            GitProcessResult result = await RunAsync(
                new[] { "log", "--date=iso-strict", "--pretty=format:" + PrettyCommitFormat, "-n", maxCount.ToString() },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read commit log.");
            return GitOutputParser.ParseCommitLog(result.StandardOutput).ToList();
        }

        public async Task<DiffResult> GetWorkingTreeDiffAsync(CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "diff", "HEAD" },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read working tree diff.");
            return GitOutputParser.ParseDiff(result.StandardOutput);
        }

        public async Task<DiffResult> GetCommitDiffAsync(string commitHash, CancellationToken ct = default)
        {
            // diff-tree with --root yields the patch for any commit, including the initial
            // (parentless) commit where "{hash}^..{hash}" would fail with "unknown revision".
            GitProcessResult result = await RunAsync(
                new[] { "diff-tree", "--no-commit-id", "-p", "--root", commitHash },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read commit diff.");
            return GitOutputParser.ParseDiff(result.StandardOutput);
        }

        public async Task<IReadOnlyList<string>> GetCommitFilesAsync(string commitHash, CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "diff-tree", "--no-commit-id", "--name-status", "-r", "--root", commitHash },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read commit files.");
            return GitOutputParser.ParseDiffTree(result.StandardOutput);
        }

        public async Task<IReadOnlyList<RemoteInfo>> GetRemotesAsync(CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "remote", "-v" },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read remotes.");
            return GitOutputParser.ParseRemotes(result.StandardOutput).ToList();
        }

        private async Task<GitProcessResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            return await runner.RunAsync(
                gitExecutablePath,
                repositoryRoot,
                arguments,
                ct).ConfigureAwait(false);
        }

        private async Task<GitProcessResult> RunExclusiveAsync(
            IReadOnlyList<string> arguments,
            CancellationToken ct)
        {
            using (await serializer.AcquireAsync(ct).ConfigureAwait(false))
            {
                return await RunAsync(arguments, ct).ConfigureAwait(false);
            }
        }

        private static OperationResult ToOperationResult(
            GitProcessResult result,
            string successMessage,
            string failureMessage)
        {
            return result.IsSuccess
                ? OperationResult.Ok(successMessage)
                : OperationResult.Fail(failureMessage, result.StandardError);
        }

        private static void EnsureSuccess(GitProcessResult result, string message)
        {
            if (!result.IsSuccess)
            {
                throw new InvalidOperationException(message + " " + result.StandardError);
            }
        }
    }
}
