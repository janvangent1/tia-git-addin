using System;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using TiaGitAddIn.Services;
using Xunit;

namespace TiaGitAddIn.Tests.Hmi
{
    public sealed class StaThreadInvokerTests
    {
        [Fact]
        public void InvokeRunsOnTheCapturedStaThread()
        {
            StaThreadInvoker invoker = null;
            int staThreadId = 0;
            using (var ready = new ManualResetEventSlim(false))
            {
                var thread = new Thread(() =>
                {
                    invoker = StaThreadInvoker.Capture();
                    staThreadId = Thread.CurrentThread.ManagedThreadId;
                    ready.Set();
                    Application.Run();
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                Assert.True(ready.Wait(5000));

                int ranOn = invoker.Invoke(() => Thread.CurrentThread.ManagedThreadId);
                invoker.Invoke(() =>
                {
                    Application.ExitThread();
                    return 0;
                });
                Assert.True(thread.Join(5000));
                Assert.Equal(staThreadId, ranOn);
                invoker.Dispose();
            }
        }

        [Fact]
        public void PostedWorkRunsWhileTheCallerThreadPumps()
        {
            int ranOn = -1;
            int pumpThreadId = -1;
            Exception error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    pumpThreadId = Thread.CurrentThread.ManagedThreadId;
                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    using (var done = new ManualResetEventSlim(false))
                    {
                        var worker = new Thread(() =>
                        {
                            ranOn = dispatcher.Invoke(() => Thread.CurrentThread.ManagedThreadId);
                            done.Set();
                        });
                        worker.IsBackground = true;
                        worker.Start();
                        DispatcherPump.RunUntil(dispatcher, done.WaitHandle);
                        Assert.True(worker.Join(5000));
                    }
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(10000));
            Assert.Null(error);
            Assert.Equal(pumpThreadId, ranOn);
        }
    }
}
