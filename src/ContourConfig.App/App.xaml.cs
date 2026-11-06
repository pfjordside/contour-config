using System.Threading;
using System.Windows;

namespace ContourConfig.App
{
    internal sealed partial class ContourApplication : Application
    {
        private const string ShowEventName = @"Local\ContourConfig.Show";
        private const int AnyProcess = -1;
        private Mutex? instanceMutex;
        private bool ownsMutex;
        private EventWaitHandle? showRequest;
        private RegisteredWaitHandle? showRegistration;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Run by the uninstaller so a removed app is not left registered to start with Windows.
            if (e.Args.Contains(StartupRegistration.UnregisterArgument, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    StartupRegistration.Set(false);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
                {
                    Shutdown(1);
                    return;
                }
                Shutdown();
                return;
            }
            instanceMutex = new(true, @"Local\ContourConfig", out ownsMutex);
            if (!ownsMutex)
            {
                // The running copy is usually hidden in the notification area; ask it to show its window instead.
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out EventWaitHandle? show))
                {
                    using (show)
                    {
                        _ = NativeMethods.AllowSetForegroundWindow(AnyProcess);
                        _ = show.Set();
                    }
                }
                Shutdown();
                return;
            }
            base.OnStartup(e);
            MainWindow window = new();
            MainWindow = window;
            showRequest = new(false, EventResetMode.AutoReset, ShowEventName);
            showRegistration = ThreadPool.RegisterWaitForSingleObject(showRequest,
                (_, _) => window.Dispatcher.BeginInvoke(window.ShowFromTray), null, Timeout.Infinite, false);
            if (e.Args.Contains(StartupRegistration.StartupArgument, StringComparer.OrdinalIgnoreCase))
            {
                window.StartHidden();
            }
            else
            {
                window.ShowFromTray();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _ = showRegistration?.Unregister(null);
            showRequest?.Dispose();
            if (ownsMutex)
            {
                instanceMutex?.ReleaseMutex();
            }
            instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}