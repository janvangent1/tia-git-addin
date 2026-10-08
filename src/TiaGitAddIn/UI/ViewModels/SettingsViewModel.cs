using System;
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
            set => SetProperty(defaultRemote, value ?? string.Empty, updated => defaultRemote = updated);
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
                DefaultRemote = DefaultRemote,
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
            if (!hasName && !hasEmail)
            {
                ValidationMessage = "Settings saved successfully.";
                return Task.CompletedTask;
            }

            return RunBusyAsync("Saving Git identity…", async ct =>
            {
                OperationResult result = await gitService.SetLocalIdentityAsync(
                    config.CommitAuthorName,
                    config.CommitAuthorEmail,
                    ct).ConfigureAwait(false);
                InvokeOnUI(() => ValidationMessage = result.Success
                    ? "Settings saved. " + result.Message
                    : GitUserMessages.Describe(result));
            });
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
            else
            {
                ValidationMessage = string.Empty;
            }

            InvokeOnUI(() => SaveCommand.RaiseCanExecuteChanged());
        }

    }
}
