using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Forms;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Runs work on the STA thread that captured it. TIA Portal's UI thread has a message
    /// loop but no WPF dispatcher the add-in can see, so a hidden window carries the call back.
    /// </summary>
    internal sealed class StaThreadInvoker : IDisposable
    {
        private readonly Control window;
        private readonly int threadId;

        private StaThreadInvoker(Control window)
        {
            this.window = window;
            threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public int ThreadId => threadId;

        public static StaThreadInvoker Capture()
        {
            if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            {
                throw new InvalidOperationException("The TIA Portal thread is not an STA thread.");
            }

            var window = new Control();
            _ = window.Handle;
            return new StaThreadInvoker(window);
        }

        public bool CheckAccess()
        {
            return Thread.CurrentThread.ManagedThreadId == threadId && !window.IsDisposed;
        }

        public T Invoke<T>(Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (CheckAccess())
            {
                return action();
            }

            T result = default!;
            ExceptionDispatchInfo? error = null;
            window.Invoke((Action)(() =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    error = ExceptionDispatchInfo.Capture(ex);
                }
            }));

            error?.Throw();
            return result;
        }

        public void Dispose()
        {
            if (window.IsDisposed)
            {
                return;
            }

            if (CheckAccess())
            {
                window.Dispose();
                return;
            }

            try
            {
                window.BeginInvoke((Action)(() => window.Dispose()));
            }
            catch (Exception)
            {
            }
        }
    }
}
