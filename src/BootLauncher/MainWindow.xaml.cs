using System.Windows;
using System.Windows.Input;
using BootLauncher.ViewModels;
using BootLauncher.Views;

namespace BootLauncher
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel
            {
                Confirm = (title, message) =>
                    MessageBox.Show(this, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes,
                PickTargetDisk = (edition, disks) =>
                {
                    var dlg = new TargetDiskWindow(edition, disks, _vm!.Hardware.IsUefi) { Owner = this };
                    return dlg.ShowDialog() == true ? dlg.SelectedDisk : null;
                }
            };
            DataContext = _vm;
            Loaded += async (s, e) => await _vm.InitializeAsync();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Technician shortcut: F10 / Shift+F10 opens a command prompt (like Windows Setup).
            if (e.Key == Key.F10 || (e.Key == Key.System && e.SystemKey == Key.F10))
            {
                _vm.OpenCmdCommand.Execute(null);
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }
    }
}
