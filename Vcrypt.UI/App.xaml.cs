using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Vcrypt.UI
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show($"Fatal Error: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Vcrypt Crash", MessageBoxButton.OK, MessageBoxImage.Error);
            LogCrash(e.Exception);
            e.Handled = true;
            Environment.Exit(1);
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogCrash(ex);
                try
                {
                    Application.Current?.Dispatcher?.Invoke(() =>
                    {
                        MessageBox.Show($"Fatal Error: {ex.Message}\n\n{ex.StackTrace}", "Vcrypt Crash", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
                catch { }
            }
            Environment.Exit(1);
        }

        private void LogCrash(Exception ex)
        {
            try
            {
                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "vcrypt_crash.txt");
                File.WriteAllText(path, ex.ToString());
            }
            catch { }
        }
    }
}
