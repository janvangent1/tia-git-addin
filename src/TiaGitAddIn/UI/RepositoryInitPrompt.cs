using System;
using System.Threading;
using System.Windows;

namespace TiaGitAddIn.UI
{
    public interface IRepositoryInitPrompt
    {
        bool Confirm(string workspacePath);
    }

    public sealed class RepositoryInitPrompt : IRepositoryInitPrompt
    {
        public bool Confirm(string workspacePath)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
            {
                return Show(workspacePath);
            }

            bool confirmed = false;
            Exception? error = null;
            Thread thread = new(() =>
            {
                try
                {
                    confirmed = Show(workspacePath);
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();
            if (error != null)
            {
                throw new InvalidOperationException("Unable to ask whether to create a Git repository.", error);
            }

            return confirmed;
        }

        private static bool Show(string workspacePath)
        {
            MessageBoxResult result = MessageBox.Show(
                "This VCI workspace is not a Git repository yet. Create one in this folder?"
                + Environment.NewLine
                + Environment.NewLine
                + workspacePath,
                "TIA Git",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }
    }
}
