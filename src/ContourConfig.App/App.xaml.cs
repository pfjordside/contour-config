using System.Threading;
using System.Windows;

namespace ContourConfig.App
{
    internal sealed partial class ContourApplication : Application
    {
        private Mutex? instanceMutex;
        private bool ownsMutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            instanceMutex = new(true, @"Local\ContourConfig", out ownsMutex);
            if (!ownsMutex)
            {
                MessageBox.Show("Contour Config is already running. Close the other window before starting another instance.",
                    "Contour Config", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
            base.OnStartup(e);
            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (ownsMutex)
            {
                instanceMutex?.ReleaseMutex();
            }
            instanceMutex?.Dispose();
            base.OnExit(e);
        }
    }
}