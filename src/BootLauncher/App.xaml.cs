using System;
using System.Windows;
using System.Windows.Threading;
using BootLauncher.Services;

namespace BootLauncher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandled;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => Log.Error("Fatal: " + a.ExceptionObject);

            Log.Info($"BootLauncher {typeof(App).Assembly.GetName().Version} starting. WinPE={SystemInfo.IsWinPE}, 64-bit={Environment.Is64BitOperatingSystem}");

            if (SystemInfo.IsWinPE) DisplayHelper.EnsureMinimumResolution();

            // Branding first so the window opens with the right colours/logo.
            BrandingService.Load();
            BrandingService.ApplyTheme(Resources);

            new MainWindow().Show();
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Log.Error("Unhandled: " + e.Exception);
            MessageBox.Show("Unexpected error:\n\n" + e.Exception.Message + "\n\nLog: " + Log.FilePath,
                "CP Bootable USB", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
