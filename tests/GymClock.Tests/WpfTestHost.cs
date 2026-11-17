using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using GymClock.Wpf;

namespace GymClock.Tests
{
    /// <summary>
    /// Runs WPF test code. WPF objects belong to the thread that made them and there is one WPF
    /// Application per process, so every WPF test runs on one long-lived STA thread that owns
    /// the Application and its theme. Use: WpfTestHost.Run(() => { ... });
    /// </summary>
    internal static class WpfTestHost
    {
        private static readonly object Sync = new object();
        private static Dispatcher _dispatcher;

        public static void Run(Action action)
        {
            Exception error = null;
            Get().Invoke(() =>
            {
                try { action(); }
                catch (Exception e) { error = e; }
            });
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static Dispatcher Get()
        {
            lock (Sync)
            {
                if (_dispatcher != null) return _dispatcher;

                using (ManualResetEventSlim ready = new ManualResetEventSlim())
                {
                    Thread thread = new Thread(() =>
                    {
                        _dispatcher = Dispatcher.CurrentDispatcher;
                        WpfHost.EnsureApplication();
                        ready.Set();
                        Dispatcher.Run();
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.IsBackground = true;
                    thread.Name = "WPF tests";
                    thread.Start();
                    ready.Wait();
                }
                return _dispatcher;
            }
        }
    }
}
