using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Logging;
using TiaGitAddIn.Services.Revision;
using AddInProcess = Siemens.Engineering.AddIn.Utilities.Process;
using AddInProcessStartInfo = Siemens.Engineering.AddIn.Utilities.ProcessStartInfo;

[assembly: InternalsVisibleTo("TiaGitAddIn.Tests")]

namespace TiaGitAddIn.Services
{
    public sealed class GitProcessRunner(TimeSpan timeout) : IGitProcessRunner, IGitBinaryProcessRunner
    {
        private const int BinaryReadBufferSize = 81920;

        // Status and stage walk the whole workspace. On a large HMI export that takes minutes.
        // The old 30-second limit killed git, and disposing that Siemens process froze TIA.
        internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

        public GitProcessRunner()
            : this(DefaultTimeout)
        {
        }

        public Task<GitProcessResult> RunAsync(
            string gitExecutablePath,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            ValidateProcessTarget(gitExecutablePath, workingDirectory);

            return Task.Run(
                () => RunProcess(gitExecutablePath, workingDirectory, arguments, cancellationToken),
                cancellationToken);
        }

        public Task<GitBinaryProcessResult> RunBinaryAsync(
            string gitExecutablePath,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            int maximumStandardOutputBytes,
            CancellationToken cancellationToken)
        {
            ValidateProcessTarget(gitExecutablePath, workingDirectory);
            if (maximumStandardOutputBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumStandardOutputBytes));
            }

            return Task.Run(
                () => RunBinaryProcessAsync(gitExecutablePath, workingDirectory, arguments, maximumStandardOutputBytes, cancellationToken),
                cancellationToken);
        }

        private static void ValidateProcessTarget(string gitExecutablePath, string workingDirectory)
        {
            ValidationResult gitPathResult = PathValidator.ValidateGitExecutablePath(gitExecutablePath);
            if (!gitPathResult.IsValid)
            {
                throw new ArgumentException(gitPathResult.ErrorMessage, nameof(gitExecutablePath));
            }

            ValidationResult workingDirectoryResult = PathValidator.Validate(workingDirectory);
            if (!workingDirectoryResult.IsValid)
            {
                throw new ArgumentException(workingDirectoryResult.ErrorMessage, nameof(workingDirectory));
            }
        }

        private GitProcessResult RunProcess(
            string gitExecutablePath,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            // Not a using-block. Dispose waits for a live child, and that wait runs on the
            // TIA thread and closes Portal. Dispose only after git has actually exited.
            AddInProcess process = new AddInProcess();
            bool started = false;
            bool exited = false;
            Task<string>? outputTask = null;
            Task<string>? errorTask = null;
            try
            {
                process.StartInfo = BuildStartInfo(gitExecutablePath, workingDirectory, arguments);

                using (CancellationTokenRegistration cancellation = cancellationToken.Register(() => TryKill(process)))
                {
                    process.Start();
                    started = true;
                    outputTask = process.StandardOutput.ReadToEndAsync();
                    errorTask = process.StandardError.ReadToEndAsync();

                    exited = WaitUntilExit(process, cancellationToken);
                    if (!exited)
                    {
                        TryKill(process);
                        exited = WaitOnce(process, 5000);
                        GitProcessResult timedOut = new GitProcessResult
                        {
                            ExitCode = -1,
                            TimedOut = true,
                            StandardOutput = ReadAvailable(outputTask),
                            StandardError = "Git took too long and was stopped. Run it again."
                        };
                        LogFailure(arguments, timedOut);
                        return timedOut;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    GitProcessResult result = new GitProcessResult
                    {
                        ExitCode = process.ExitCode,
                        StandardOutput = ReadAvailable(outputTask),
                        StandardError = ReadAvailable(errorTask)
                    };
                    LogFailure(arguments, result);
                    return result;
                }
            }
            catch (OperationCanceledException)
            {
                TryKill(process);
                exited = WaitOnce(process, 2000);
                Observe(outputTask, errorTask);
                throw;
            }
            catch (Exception ex)
            {
                TryKill(process);
                exited = WaitOnce(process, 2000);
                Observe(outputTask, errorTask);
                GitProcessResult failed = new GitProcessResult
                {
                    ExitCode = -1,
                    StandardError = ex.Message
                };
                LogFailure(arguments, failed);
                return failed;
            }
            finally
            {
                if (!started || exited)
                {
                    TryDispose(process);
                }
            }
        }

        // Short waits. One long WaitForExit is marshalled onto the TIA thread and Portal
        // stops painting until it returns.
        private bool WaitUntilExit(AddInProcess process, CancellationToken cancellationToken)
        {
            const int sliceMilliseconds = 250;
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int remaining = (int)Math.Min(sliceMilliseconds, (timeout - watch.Elapsed).TotalMilliseconds);
                if (remaining <= 0)
                {
                    break;
                }

                if (WaitOnce(process, remaining))
                {
                    return true;
                }
            }

            return false;
        }

        // Binary reads never go through StandardOutput's StreamReader (which would transcode/corrupt
        // non-text blob content). Instead process.StandardOutput.BaseStream is read directly and bounded
        // by ReadBoundedAsync. A Task.WhenAny race lets a size-limit fault interrupt WaitForExit promptly
        // instead of waiting out the full timeout; the final `await outputTask` still re-observes any
        // fault even if WaitForExit happened to win the race, so no path can swallow it.
        private async Task<GitBinaryProcessResult> RunBinaryProcessAsync(
            string gitExecutablePath,
            string workingDirectory,
            IReadOnlyList<string> arguments,
            int maximumStandardOutputBytes,
            CancellationToken cancellationToken)
        {
            using (AddInProcess process = new())
            {
                process.StartInfo = BuildStartInfo(gitExecutablePath, workingDirectory, arguments);

                using (CancellationTokenRegistration cancellation = cancellationToken.Register(() => TryKill(process)))
                {
                    process.Start();

                    Task<string> errorTask = process.StandardError.ReadToEndAsync();
                    Task<byte[]> outputTask = ReadBoundedAsync(
                        process.StandardOutput.BaseStream, maximumStandardOutputBytes, cancellationToken);
                    Task<bool> waitTask = Task.Run(
                        () => WaitUntilExit(process, CancellationToken.None), CancellationToken.None);

                    Task firstCompleted = await Task.WhenAny(outputTask, waitTask).ConfigureAwait(false);
                    if (firstCompleted == outputTask && outputTask.IsFaulted)
                    {
                        TryKill(process);
                    }

                    bool exited = await waitTask.ConfigureAwait(false);
                    if (!exited)
                    {
                        TryKill(process);
                        WaitOnce(process, 5000);
                        return new GitBinaryProcessResult(-1, Array.Empty<byte>(), "Git took too long and was stopped. Run it again.", true);
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    // Always awaited last: re-observes a size-limit (or cancellation) fault even if
                    // waitTask happened to complete first, so it can never be silently missed.
                    byte[] output = await outputTask.ConfigureAwait(false);
                    string standardError = await errorTask.ConfigureAwait(false);
                    return new GitBinaryProcessResult(process.ExitCode, output, standardError, false);
                }
            }
        }

        /// <summary>
        /// Reads <paramref name="stream"/> to completion in <see cref="BinaryReadBufferSize"/> chunks,
        /// throwing <see cref="RevisionSizeLimitException"/> and clearing everything accumulated so far
        /// as soon as the running total exceeds <paramref name="maximumBytes"/>. Internal and exposed to
        /// the test assembly via <see cref="InternalsVisibleToAttribute"/> so this bounded-read behavior
        /// can be verified without spinning up a real OS process for every scenario.
        /// </summary>
        internal static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumBytes, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[BinaryReadBufferSize];
            using (MemoryStream accumulated = new MemoryStream())
            {
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    accumulated.Write(buffer, 0, bytesRead);
                    if (accumulated.Length > maximumBytes)
                    {
                        accumulated.SetLength(0);
                        Array.Clear(buffer, 0, buffer.Length);
                        throw new RevisionSizeLimitException(
                            $"Git standard output exceeded the {maximumBytes}-byte limit.");
                    }
                }

                Array.Clear(buffer, 0, buffer.Length);
                return accumulated.ToArray();
            }
        }

        private static AddInProcessStartInfo BuildStartInfo(
            string gitExecutablePath, string workingDirectory, IReadOnlyList<string> arguments)
            => new AddInProcessStartInfo
            {
                FileName = gitExecutablePath,
                Arguments = BuildArgumentString(arguments),
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

        internal static string BuildArgumentString(IReadOnlyList<string> arguments)
        {
            StringBuilder builder = new();
            foreach (string argument in arguments)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(GitArgumentEscaper.Escape(argument));
            }

            return builder.ToString();
        }

        private static bool WaitOnce(AddInProcess process, int milliseconds)
        {
            try
            {
                return process.WaitForExit(milliseconds);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void Observe(Task<string>? outputTask, Task<string>? errorTask)
        {
            if (outputTask != null)
            {
                ReadAvailable(outputTask);
            }

            if (errorTask != null)
            {
                ReadAvailable(errorTask);
            }
        }

        private static string ReadAvailable(Task<string> task)
        {
            try
            {
                if (!task.Wait(TimeSpan.FromSeconds(5)))
                {
                    return string.Empty;
                }

                return task.Result ?? string.Empty;
            }
            catch (Exception ex)
            {
                return ex.GetBaseException().Message;
            }
        }

        private static void LogFailure(IReadOnlyList<string> arguments, GitProcessResult result)
        {
            if (result.IsSuccess)
            {
                return;
            }

            string detail = result.StandardError ?? string.Empty;
            if (detail.Length > 500)
            {
                detail = detail.Substring(0, 500);
            }

            try
            {
                new FileLogger().Info("git " + string.Join(" ", arguments) + " failed. " + detail);
            }
            catch (Exception)
            {
            }
        }

        private static void TryKill(AddInProcess process)
        {
            try
            {
                process.Kill();
            }
            catch (Exception)
            {
            }
        }

        private static void TryDispose(AddInProcess process)
        {
            try
            {
                process.Dispose();
            }
            catch (Exception)
            {
            }
        }
    }
}
