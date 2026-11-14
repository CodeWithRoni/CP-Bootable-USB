using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace UsbMaker;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Needed to decode diskpart/bootsect output, which uses the OEM code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        DispatcherUnhandledException += OnUnhandledException;
        base.OnStartup(e);
    }

    private static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Unexpected error:\n\n{e.Exception.Message}", "CP Bootable USB",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
