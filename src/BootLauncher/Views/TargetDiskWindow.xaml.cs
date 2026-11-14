using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BootLauncher.Models;

namespace BootLauncher.Views
{
    /// <summary>Target-disk picker for Direct Apply. The technician must type the disk number to confirm.</summary>
    public partial class TargetDiskWindow : Window
    {
        public TargetDiskWindow(ImageEdition edition, TargetDisk[] disks, bool uefi)
        {
            InitializeComponent();

            EditionText.Text = $"{edition.Name} · {edition.Arch} · installed by Direct Apply (DISM)";
            LayoutText.Text = uefi
                ? "Layout: GPT — EFI 260 MB + MSR 16 MB + Windows (rest). Boots in UEFI mode."
                : "Layout: MBR — System Reserved 500 MB + Windows (rest). Boots in Legacy BIOS mode.";
            DiskList.ItemsSource = disks;
            DiskList.SelectedItem = disks.FirstOrDefault(d => !d.IsBlocked && disks.Count(x => !x.IsBlocked) == 1);
        }

        public TargetDisk? SelectedDisk => DiskList.SelectedItem as TargetDisk;

        private void DiskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var d = SelectedDisk;
            ConfirmBox.Text = "";
            ConfirmBox.IsEnabled = d != null && !d.IsBlocked;
            PromptText.Text = d == null ? "Select a disk above." : $"Type the disk number  {d.Index}  to confirm:";
            InstallButton.IsEnabled = false;
            if (ConfirmBox.IsEnabled) ConfirmBox.Focus();
        }

        private void ConfirmBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var d = SelectedDisk;
            InstallButton.IsEnabled = d != null && !d.IsBlocked && ConfirmBox.Text.Trim() == d.Index.ToString();
        }

        private void Install_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
