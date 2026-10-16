using System;
using System.Threading;
using System.Windows.Threading;

namespace TiaGitAddIn.Services
{
    /// <summary>
    /// Pumps a dispatcher until <paramref name="done"/> is set. TIA Portal does not run a WPF
    /// dispatcher on the add-in thread after the menu callback returns, so posted Openness work
    /// would wait forever unless this frame stays on that thread while the Git panel is open.
    /// </summary>
    internal static class DispatcherPump
    {
        public static void RunUntil(Dispatcher dispatcher, WaitHandle done)
        {
            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }

            if (done == null)
            {
                throw new ArgumentNullException(nameof(done));
            }

            if (!dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("The caller pump must run on the thread that owns the dispatcher.");
            }

            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Background,
                (_, __) =>
                {
                    if (done.WaitOne(0))
                    {
                        frame.Continue = false;
                    }
                },
                dispatcher);
            timer.Start();
            try
            {
                Dispatcher.PushFrame(frame);
            }
            finally
            {
                timer.Stop();
            }
        }
    }
}
