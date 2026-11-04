using System;
using System.Collections.Generic;
using System.IO;
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
        // SHA-1 of an empty tree. Used when the repository has no commit yet, so HEAD does not exist.
        private const string EmptyTreeHash = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";
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

        public async Task<PullPreview> PreviewPullAsync(string? remote = null, CancellationToken ct = default)
        {
            OperationResult fetch = await FetchAsync(remote, ct).ConfigureAwait(false);
            if (!fetch.Success)
            {
                return new PullPreview(Array.Empty<CommitInfo>(), Array.Empty<string>(), fetch.Message);
            }

            GitProcessResult upstream = await RunAsync(
                new[] { "rev-parse", "--abbrev-ref", "@{upstream}" },
                ct).ConfigureAwait(false);
            if (!upstream.IsSuccess)
            {
                return new PullPreview(
                    Array.Empty<CommitInfo>(),
                    Array.Empty<string>(),
                    "This branch does not track a remote branch.");
            }

            string target = (upstream.StandardOutput ?? string.Empty).Trim();
            GitProcessResult log = await RunAsync(
                new[] { "log", "--date=iso-strict", "--pretty=format:" + PrettyCommitFormat, "HEAD.." + target },
                ct).ConfigureAwait(false);
            if (!log.IsSuccess)
            {
                return new PullPreview(Array.Empty<CommitInfo>(), Array.Empty<string>(), "Unable to read the incoming commits.");
            }

            var commits = new List<CommitInfo>(GitOutputParser.ParseCommitLog(log.StandardOutput ?? string.Empty));
            GitProcessResult names = await RunAsync(
                new[] { "diff", "--name-only", "HEAD..." + target },
                ct).ConfigureAwait(false);
            var plcFiles = new List<string>();
            int otherFiles = 0;
            if (names.IsSuccess)
            {
                foreach (string raw in (names.StandardOutput ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string path = raw.Trim();
                    if (path.Length == 0)
                    {
                        continue;
                    }

                    if (IsPlcPath(path))
                    {
                        plcFiles.Add(path);
                    }
                    else
                    {
                        otherFiles++;
                    }
                }
            }

            string message = commits.Count == 0
                ? "Nothing new to pull."
                : commits.Count + " commit(s) will arrive, including " + plcFiles.Count + " PLC file(s)."
                    + (otherFiles > 0 ? " " + otherFiles + " other file(s) will arrive as well." : string.Empty);
            return new PullPreview(commits, plcFiles, message);
        }

        public async Task<(string? Ours, string? Theirs)> GetConflictSidesAsync(string path, CancellationToken ct = default)
        {
            string relative = NormalizeRepositoryPath(path);
            string? ours = await ReadStageAsync(2, relative, ct).ConfigureAwait(false);
            string? theirs = await ReadStageAsync(3, relative, ct).ConfigureAwait(false);
            return (ours, theirs);
        }

        public async Task<DiffResult> GetRangeDiffAsync(string leftCommitHash, string rightCommitHash, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(leftCommitHash) || !CommitHashPattern.IsMatch(leftCommitHash)
                || string.IsNullOrWhiteSpace(rightCommitHash) || !CommitHashPattern.IsMatch(rightCommitHash))
            {
                throw new ArgumentException("Both commits must be selected.");
            }

            GitProcessResult result = await RunAsync(
                new[] { "diff", "--no-color", leftCommitHash, rightCommitHash },
                ct).ConfigureAwait(false);
            EnsureSuccess(result, "Unable to compare those commits.");
            return GitOutputParser.ParseDiff(result.StandardOutput);
        }

        private async Task<string?> ReadStageAsync(int stage, string path, CancellationToken ct)
        {
            GitProcessResult result = await RunAsync(
                new[] { "show", ":" + stage.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + path },
                ct).ConfigureAwait(false);
            if (!result.IsSuccess || result.StandardOutput == null)
            {
                return null;
            }

            if (result.StandardOutput.IndexOf('\0') >= 0)
            {
                return null;
            }

            return result.StandardOutput;
        }

        private static bool IsPlcPath(string path)
        {
            return path.EndsWith(".scl", StringComparison.OrdinalIgnoreCase)
                || path.IndexOf("PLC", StringComparison.OrdinalIgnoreCase) >= 0;
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

        public async Task<(string Name, string Email)> GetLocalIdentityAsync(CancellationToken ct = default)
        {
            string name = await ReadLocalConfigAsync("user.name", ct).ConfigureAwait(false);
            string email = await ReadLocalConfigAsync("user.email", ct).ConfigureAwait(false);
            return (name, email);
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

        private async Task<string> ReadLocalConfigAsync(string key, CancellationToken ct)
        {
            GitProcessResult result = await RunAsync(
                new[] { "config", "--local", "--get", key },
                ct).ConfigureAwait(false);
            if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                return string.Empty;
            }

            return result.StandardOutput.Trim();
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

        public async Task<OperationResult> SetRemoteUrlAsync(string remoteName, string url, CancellationToken ct = default)
        {
            string? nameProblem = DescribeRemoteNameProblem(remoteName);
            if (nameProblem != null)
            {
                return OperationResult.Fail(nameProblem);
            }

            string? urlProblem = DescribeRemoteUrlProblem(url);
            if (urlProblem != null)
            {
                return OperationResult.Fail(urlProblem);
            }

            string name = remoteName.Trim();
            string address = url.Trim();
            using (await serializer.AcquireAsync(ct).ConfigureAwait(false))
            {
                GitProcessResult listed = await RunAsync(new[] { "remote", "-v" }, ct).ConfigureAwait(false);
                if (!listed.IsSuccess)
                {
                    return ToOperationResult(listed, string.Empty, "Unable to read remotes.");
                }

                bool exists = GitOutputParser.ParseRemotes(listed.StandardOutput)
                    .Any(remote => string.Equals(remote.Name, name, StringComparison.Ordinal));
                string[] arguments = exists
                    ? new[] { "remote", "set-url", name, address }
                    : new[] { "remote", "add", name, address };
                GitProcessResult result = await RunAsync(arguments, ct).ConfigureAwait(false);
                return ToOperationResult(
                    result,
                    "Remote " + name + " now points at " + address + ".",
                    "Unable to save the remote repository.");
            }
        }

        public static string? DescribeRemoteNameProblem(string? remoteName)
        {
            if (remoteName == null || remoteName.Trim().Length == 0)
            {
                return "Enter a remote name such as origin.";
            }

            string name = remoteName.Trim();
            if (name.Length > 64 || !char.IsLetterOrDigit(name[0]))
            {
                return "Enter a remote name such as origin.";
            }

            for (int index = 0; index < name.Length; index++)
            {
                char character = name[index];
                bool allowed = char.IsLetterOrDigit(character) || character == '.' || character == '_' || character == '-';
                if (!allowed)
                {
                    return "Enter a remote name such as origin.";
                }
            }

            return null;
        }

        public static string? DescribeRemoteUrlProblem(string? url)
        {
            if (url == null || url.Trim().Length == 0)
            {
                return "Enter a GitHub or Azure DevOps repository URL.";
            }

            string address = url.Trim();
            if (address.Length > 500 || address.StartsWith("-", StringComparison.Ordinal) || HasControlOrWhitespace(address))
            {
                return "Enter a GitHub or Azure DevOps repository URL.";
            }

            if (HasEmbeddedPassword(address))
            {
                return "Leave the password out of the URL. Git Credential Manager stores it after the first fetch.";
            }

            if (IsGitHubUrl(address) || IsAzureDevOpsUrl(address))
            {
                return null;
            }

            return "Enter a GitHub or Azure DevOps repository URL.";
        }

        private static bool IsGitHubUrl(string address)
        {
            if (TryTrimPrefix(address, "https://github.com/", out string httpsPath))
            {
                return HasOwnerAndRepository(httpsPath);
            }

            if (TryTrimPrefix(address, "ssh://git@github.com/", out string sshPath))
            {
                return HasOwnerAndRepository(sshPath);
            }

            if (TryTrimPrefix(address, "git@github.com:", out string scpPath))
            {
                return HasOwnerAndRepository(scpPath);
            }

            return false;
        }

        private static bool IsAzureDevOpsUrl(string address)
        {
            if (TryTrimPrefix(address, "git@ssh.dev.azure.com:", out string scpPath))
            {
                return TryTrimPrefix(scpPath, "v3/", out string repositoryPath) && HasThreeSegments(repositoryPath);
            }

            if (TryTrimPrefix(address, "ssh://git@ssh.dev.azure.com/", out string sshPath))
            {
                return TryTrimPrefix(sshPath, "v3/", out string repositoryPath) && HasThreeSegments(repositoryPath);
            }

            if (!TryTrimPrefix(address, "https://", out string remainder))
            {
                return false;
            }

            int slash = remainder.IndexOf('/');
            if (slash <= 0)
            {
                return false;
            }

            int gitMarker = remainder.IndexOf(
                "/_git/",
                slash,
                remainder.Length - slash,
                StringComparison.OrdinalIgnoreCase);
            if (gitMarker < 0)
            {
                return false;
            }

            string repository = remainder.Substring(gitMarker + "/_git/".Length).Trim('/');
            int nextSlash = repository.IndexOf('/');
            if (nextSlash >= 0)
            {
                repository = repository.Substring(0, nextSlash);
            }

            return repository.Length > 0;
        }

        private static bool HasOwnerAndRepository(string path)
        {
            string[] parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0;
        }

        private static bool HasThreeSegments(string path)
        {
            string[] parts = path.Trim('/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3;
        }

        private static bool TryTrimPrefix(string value, string prefix, out string rest)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = value.Substring(prefix.Length);
                return true;
            }

            rest = string.Empty;
            return false;
        }

        private static bool HasEmbeddedPassword(string address)
        {
            int scheme = address.IndexOf("://", StringComparison.Ordinal);
            if (scheme < 0)
            {
                return false;
            }

            int start = scheme + 3;
            int slash = address.IndexOf('/', start);
            string authority = slash < 0 ? address.Substring(start) : address.Substring(start, slash - start);
            int at = authority.LastIndexOf('@');
            if (at <= 0)
            {
                return false;
            }

            return authority.Substring(0, at).IndexOf(':') >= 0;
        }

        private static bool HasControlOrWhitespace(string value)
        {
            foreach (char character in value)
            {
                if (char.IsControl(character) || char.IsWhiteSpace(character))
                {
                    return true;
                }
            }

            return false;
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

            if (!result.IsSuccess && IsUnbornHead(result))
            {
                return Array.Empty<CommitInfo>();
            }

            EnsureSuccess(result, "Unable to read commit log.");
            return GitOutputParser.ParseCommitLog(result.StandardOutput).ToList();
        }

        public Task<DiffResult> GetWorkingTreeDiffAsync(CancellationToken ct = default) =>
            GetWorkingTreeDiffAsync(null, ct);

        public async Task<DiffResult> GetWorkingTreeDiffAsync(
            IReadOnlyList<string>? repositoryRelativePaths,
            CancellationToken ct = default)
        {
            string baseline = await HasHeadAsync(ct).ConfigureAwait(false) ? "HEAD" : EmptyTreeHash;
            var arguments = new List<string> { "diff", baseline };
            if (repositoryRelativePaths != null)
            {
                arguments.Add("--");
                arguments.AddRange(NormalizeRepositoryPaths(repositoryRelativePaths));
            }

            GitProcessResult result = await RunAsync(arguments, ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read working tree diff.");
            return GitOutputParser.ParseDiff(result.StandardOutput);
        }

        private async Task<bool> HasHeadAsync(CancellationToken ct)
        {
            GitProcessResult result = await RunAsync(
                new[] { "rev-parse", "--verify", "--quiet", "HEAD" },
                ct).ConfigureAwait(false);
            return result.IsSuccess;
        }

        private static bool IsUnbornHead(GitProcessResult result)
        {
            string text = (result.StandardError ?? string.Empty) + " " + (result.StandardOutput ?? string.Empty);
            return text.IndexOf("does not have any commits yet", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public Task<DiffResult> GetCommitDiffAsync(string commitHash, CancellationToken ct = default) =>
            GetCommitDiffAsync(commitHash, null, ct);

        public async Task<DiffResult> GetCommitDiffAsync(
            string commitHash,
            IReadOnlyList<string>? repositoryRelativePaths,
            CancellationToken ct = default)
        {
            // diff-tree with --root yields the patch for any commit, including the initial
            // (parentless) commit where "{hash}^..{hash}" would fail with "unknown revision".
            var arguments = new List<string> { "diff-tree", "--no-commit-id", "-p", "--root", commitHash };
            if (repositoryRelativePaths != null)
            {
                arguments.Add("--");
                arguments.AddRange(NormalizeRepositoryPaths(repositoryRelativePaths));
            }

            GitProcessResult result = await RunAsync(arguments, ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read commit diff.");
            return GitOutputParser.ParseDiff(result.StandardOutput);
        }

        public async Task<IReadOnlyList<FileRevision>> GetFileHistoryAsync(
            IReadOnlyList<string> repositoryRelativePaths,
            int maxCount,
            CancellationToken ct = default)
        {
            if (maxCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxCount), "Commit count must be positive.");
            }

            string[] paths = NormalizeRepositoryPaths(repositoryRelativePaths);
            var arguments = new List<string> { "log" };
            if (paths.Length == 1)
            {
                arguments.Add("--follow");
            }

            arguments.Add("--name-status");
            arguments.Add("--date=iso-strict");
            arguments.Add("--pretty=format:" + PrettyCommitFormat);
            arguments.Add("-n");
            arguments.Add(maxCount.ToString());
            arguments.Add("--");
            arguments.AddRange(paths);

            GitProcessResult result = await RunAsync(arguments, ct).ConfigureAwait(false);
            if (!result.IsSuccess && IsUnbornHead(result))
            {
                return Array.Empty<FileRevision>();
            }

            EnsureSuccess(result, "Unable to read file history.");
            return GitOutputParser.ParseFileHistory(result.StandardOutput);
        }

        public async Task<IReadOnlyList<string>> GetCommitFilesAsync(string commitHash, CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "diff-tree", "--no-commit-id", "--name-status", "-r", "--root", commitHash },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read commit files.");
            return GitOutputParser.ParseDiffTree(result.StandardOutput);
        }

        public async Task<IReadOnlyList<string>> ListExportFilesAsync(string commitHash, CancellationToken ct = default)
        {
            GitProcessResult result = await RunAsync(
                new[] { "ls-tree", "-r", "--name-only", commitHash, "--", "HmiExport", "HardwareExport" },
                ct).ConfigureAwait(false);

            EnsureSuccess(result, "Unable to read the export in this commit.");
            return (result.StandardOutput ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
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

        private string[] NormalizeRepositoryPaths(IReadOnlyList<string> repositoryRelativePaths)
        {
            if (repositoryRelativePaths == null || repositoryRelativePaths.Count == 0)
            {
                throw new ArgumentException("Repository-relative path is required.", nameof(repositoryRelativePaths));
            }

            var normalized = new string[repositoryRelativePaths.Count];
            for (int index = 0; index < repositoryRelativePaths.Count; index++)
            {
                normalized[index] = NormalizeRepositoryPath(repositoryRelativePaths[index]);
            }

            return normalized;
        }

        private string NormalizeRepositoryPath(string? repositoryRelativePath)
        {
            if (string.IsNullOrEmpty(repositoryRelativePath))
            {
                throw new ArgumentException("Repository-relative path is required.", nameof(repositoryRelativePath));
            }

            string path = repositoryRelativePath!;
            if (path.IndexOf('\0') >= 0)
            {
                throw new ArgumentException("Path must not contain a NUL character.", nameof(repositoryRelativePath));
            }

            if (Path.IsPathRooted(path) || path.StartsWith("\\\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("Path must be repository-relative, not rooted.", nameof(repositoryRelativePath));
            }

            string normalized = path.Replace('\\', '/');
            string[] segments = normalized.Split('/');
            foreach (string segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                {
                    throw new ArgumentException(
                        "Path must not contain empty, '.' or '..' segments.", nameof(repositoryRelativePath));
                }
            }

            string combined = Path.GetFullPath(Path.Combine(repositoryRoot, normalized));
            string rootWithSeparator = repositoryRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? repositoryRoot
                : repositoryRoot + Path.DirectorySeparatorChar;
            if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Path escapes the repository root.", nameof(repositoryRelativePath));
            }

            return normalized;
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
