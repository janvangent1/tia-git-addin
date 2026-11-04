using System;
using System.Threading.Tasks;
using System.Windows.Input;
using TiaGitAddIn.Logging;

namespace TiaGitAddIn.UI
{
    public sealed class AsyncCommand(Func<object?, Task> executeAsync, Func<object?, bool>? canExecute = null) : ICommand
    {
        private readonly Func<object?, Task> executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        private bool isExecuting;

        public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
            : this(_ => execute(), _ => canExecute?.Invoke() ?? true)
        {
        }

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            !isExecuting && (canExecute == null || canExecute(parameter));

        public async void Execute(object? parameter)
        {
            if (!CanExecute(parameter))
            {
                return;
            }

            isExecuting = true;
            RaiseCanExecuteChanged();
            try
            {
                await executeAsync(parameter).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                // async void runs on the TIA dispatcher. An exception here closes Portal.
                try
                {
                    new FileLogger().Error("A Git panel command failed.", ex);
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                isExecuting = false;
                try
                {
                    RaiseCanExecuteChanged();
                }
                catch (Exception)
                {
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
