using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Win32;
using TiaGitAddIn.Configuration;
using TiaGitAddIn.Models;
using TiaGitAddIn.Services;
using TiaGitAddIn.UI;

namespace TiaGitAddIn.UI.ViewModels
{
    public sealed class SettingsViewModel : ViewModelBase
    {
        private readonly IConfigurationService configurationService;
        private readonly IGitService gitService;
        private readonly string repositoryRoot;
        private string gitExecutablePath = string.Empty;
        private string commitAuthorName = string.Empty;
        private string commitAuthorEmail = string.Empty;
        private string repositoryPath = string.Empty;
        private string defaultRemote = "origin";
        private string remoteUrl = string.Empty;
        private bool remoteUrlEdited;
        private int maxLogEntries = 200;
        private string validationMessage = string.Empty;
        private bool canSaveSettings = true;

        public SettingsViewModel(
            IConfigurationService configurationService,
            string repositoryRoot,
            IGitService gitService,
            IUiDispatcher? uiDispatcher = null)
            : base(uiDispatcher)
        {
            this.configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
            this.gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
            this.repositoryRoot = repositoryRoot;

            BrowseGitExeCommand = new RelayCommand(_ => BrowseGitExe());
            SaveCommand = new AsyncCommand(() => SaveAsync(), () => canSaveSettings);

            LoadSettings();
            _ = RefreshRemoteUrlAsync();
        }

        public string GitExecutablePath
        {
            get => gitExecutablePath;
            set
            {
                if (SetProperty(gitExecutablePath, value ?? string.Empty, updated => gitExecutablePath = updated))
                {
                    Validate();
                }
            }
        }

        public string CommitAuthorName
        {
            get => commitAuthorName;
            set
            {
                if (SetProperty(commitAuthorName, value ?? string.Empty, updated => commitAuthorName = updated))
                {
                    Validate();
                }
            }
        }

        public string CommitAuthorEmail
        {
            get => commitAuthorEmail;
            set
            {
                if (SetProperty(commitAuthorEmail, value ?? string.Empty, updated => commitAuthorEmail = updated))
                {
                    Validate();
                }
            }
        }

        public string RepositoryPath
        {
            get => repositoryPath;
            set => SetProperty(repositoryPath, value ?? string.Empty, updated => repositoryPath = updated);
        }

        public string DefaultRemote
        {
            get => defaultRemote;
            set
            {
                if (SetProperty(defaultRemote, value ?? string.Empty, updated => defaultRemote = updated))
                {
                    Validate();
                }
            }
        }

        public string RemoteUrl
        {
            get => remoteUrl;
            set
            {
                if (SetProperty(remoteUrl, value ?? string.Empty, updated => remoteUrl = updated))
                {
                    remoteUrlEdited = true;
                    Validate();
                }
            }
        }

        public int MaxLogEntries
        {
            get => maxLogEntries;
            set => SetProperty(maxLogEntries, value, updated => maxLogEntries = updated);
        }

        public string ValidationMessage
        {
            get => validationMessage;
            private set => SetProperty(validationMessage, value ?? string.Empty, updated => validationMessage = updated);
        }

        public RelayCommand BrowseGitExeCommand { get; }
        public AsyncCommand SaveCommand { get; }

        private void LoadSettings()
        {
            var config = configurationService.Load(repositoryRoot);
            gitExecutablePath = config.GitExecutablePath ?? string.Empty;
            commitAuthorName = config.CommitAuthorName ?? string.Empty;
            commitAuthorEmail = config.CommitAuthorEmail ?? string.Empty;
            repositoryPath = config.RepositoryPath ?? repositoryRoot;
            defaultRemote = config.DefaultRemote ?? "origin";
            remoteUrl = config.RemoteUrl ?? string.Empty;
            maxLogEntries = config.MaxLogEntries;

            OnPropertyChanged(string.Empty);
            Validate();
        }

        private Task SaveAsync()
        {
            GitConfiguration config = new()
            {
                GitExecutablePath = string.IsNullOrWhiteSpace(GitExecutablePath) ? null : GitExecutablePath,
                RepositoryPath = RepositoryPath,
                DefaultRemote = string.IsNullOrWhiteSpace(DefaultRemote) ? "origin" : DefaultRemote.Trim(),
                RemoteUrl = RemoteUrl.Trim(),
                MaxLogEntries = MaxLogEntries,
                CommitAuthorName = CommitAuthorName.Trim(),
                CommitAuthorEmail = CommitAuthorEmail.Trim(),
                Version = 1
            };

            try
            {
                configurationService.Save(repositoryRoot, config);
            }
            catch (Exception ex)
            {
                ValidationMessage = $"Failed to save settings: {ex.Message}";
                return Task.CompletedTask;
            }

            bool hasName = !string.IsNullOrWhiteSpace(config.CommitAuthorName);
            bool hasEmail = !string.IsNullOrWhiteSpace(config.CommitAuthorEmail);
            bool saveRemote = config.RemoteUrl.Length > 0;
            if (!hasName && !hasEmail && !saveRemote)
            {
                ValidationMessage = "Settings saved successfully.";
                return Task.CompletedTask;
            }

            return RunBusyAsync("Saving settings…", async ct =>
            {
                var notes = new List<string>();
                if (hasName && hasEmail)
                {
                    OperationResult identity = await gitService.SetLocalIdentityAsync(
                        config.CommitAuthorName,
                        config.CommitAuthorEmail,
                        ct).ConfigureAwait(false);
                    notes.Add(identity.Success ? identity.Message : GitUserMessages.Describe(identity));
                }

                if (saveRemote)
                {
                    OperationResult remote = await gitService.SetRemoteUrlAsync(
                        config.DefaultRemote,
                        config.RemoteUrl,
                        ct).ConfigureAwait(false);
                    notes.Add(remote.Success ? remote.Message : GitUserMessages.Describe(remote));
                }

                InvokeOnUI(() => ValidationMessage = "Settings saved. " + string.Join(" ", notes));
            });
        }

        private async Task RefreshRemoteUrlAsync()
        {
            try
            {
                IReadOnlyList<RemoteInfo> remotes = await gitService.GetRemotesAsync().ConfigureAwait(false);
                string name = string.IsNullOrWhiteSpace(DefaultRemote) ? "origin" : DefaultRemote.Trim();
                RemoteInfo? match = null;
                foreach (RemoteInfo remote in remotes)
                {
                    if (!string.Equals(remote.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    match = remote;
                    if (string.Equals(remote.Purpose, "fetch", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                if (match == null || string.IsNullOrWhiteSpace(match.Url) || remoteUrlEdited)
                {
                    return;
                }

                string currentUrl = match.Url;
                InvokeOnUI(() =>
                {
                    if (remoteUrlEdited)
                    {
                        return;
                    }

                    remoteUrl = currentUrl;
                    OnPropertyChanged(nameof(RemoteUrl));
                    Validate();
                });
            }
            catch (Exception)
            {
                // Keep the URL stored in the settings file when git cannot list remotes yet.
            }
        }

        protected override void ReportStatus(string message) => ValidationMessage = message;

        private void BrowseGitExe()
        {
            OpenFileDialog dialog = new()
            {
                Filter = "Git Executable (git.exe)|git.exe|All Files (*.*)|*.*",
                Title = "Select git.exe"
            };

            if (dialog.ShowDialog() == true)
            {
                GitExecutablePath = dialog.FileName;
            }
        }

        private void Validate()
        {
            canSaveSettings = true;
            if (!string.IsNullOrWhiteSpace(GitExecutablePath))
            {
                var result = PathValidator.ValidateGitExecutablePath(GitExecutablePath);
                if (!result.IsValid)
                {
                    canSaveSettings = false;
                    ValidationMessage = result.ErrorMessage ?? "Invalid path";
                    InvokeOnUI(() => SaveCommand.RaiseCanExecuteChanged());
                    return;
                }
            }

            bool hasName = !string.IsNullOrWhiteSpace(CommitAuthorName);
            bool hasEmail = !string.IsNullOrWhiteSpace(CommitAuthorEmail);
            if (hasName != hasEmail)
            {
                canSaveSettings = false;
                ValidationMessage = "Enter both a name and an email, or leave both empty.";
            }
            else if (hasEmail && CommitAuthorEmail.IndexOf('@') < 1)
            {
                canSaveSettings = false;
                ValidationMessage = "Enter an email address.";
            }
            else if (RemoteUrl.Trim().Length > 0)
            {
                string? urlProblem = GitService.DescribeRemoteUrlProblem(RemoteUrl);
                string? nameProblem = GitService.DescribeRemoteNameProblem(DefaultRemote);
                if (urlProblem != null)
                {
                    canSaveSettings = false;
                    ValidationMessage = urlProblem;
                }
                else if (nameProblem != null)
                {
                    canSaveSettings = false;
                    ValidationMessage = nameProblem;
                }
                else
                {
                    ValidationMessage = string.Empty;
                }
            }
            else
            {
                ValidationMessage = string.Empty;
            }

            InvokeOnUI(() => SaveCommand.RaiseCanExecuteChanged());
        }

    }
}
