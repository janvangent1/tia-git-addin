using System;
using System.IO;
using System.Windows;
using TiaGitAddIn.UI;
using TiaGitAddIn.UI.Views;

namespace TiaGitAddIn.Preview
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string folder = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])
                ? args[0]
                : Directory.GetCurrentDirectory();

            GitPanelLaunchResult result = new GitPanelLaunchService().CreateViewModelForFolder(folder);
            if (!result.Success || result.CreateViewModel == null)
            {
                MessageBox.Show(
                    result.Message,
                    "TIA Git preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return 1;
            }

            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            var window = new GitPanelWindow(result.CreateViewModel())
            {
                Title = "TIA Git preview"
            };
            application.MainWindow = window;
            application.Run(window);
            return 0;
        }
    }
}
