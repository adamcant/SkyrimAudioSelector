using System.Windows;
using Forms = System.Windows.Forms;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        // ---------------- Browse handlers ----------------

        private void BrowseModsRoot_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select MO2/Vortex mods root folder"
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                ModsRootTextBox.Text = dlg.SelectedPath;
        }

        private void BrowseSkyrimData_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select Skyrim Data folder (under game root)"
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                SkyrimDataTextBox.Text = dlg.SelectedPath;
        }

        private void BrowseOutputMod_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new Forms.FolderBrowserDialog
            {
                Description = "Select root folder where the patch mod folder will be created"
            };
            if (dlg.ShowDialog() == Forms.DialogResult.OK)
                OutputModTextBox.Text = dlg.SelectedPath;
        }

        private void BrowseModlist_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "modlist.txt|modlist.txt|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
                ModlistTextBox.Text = dlg.FileName;
        }

        private void BrowseBsarch_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "bsarch.exe|bsarch*.exe|Executables|*.exe|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
                BsarchPathTextBox.Text = dlg.FileName;
        }

        private void PackToBsaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
        }

    }
}
