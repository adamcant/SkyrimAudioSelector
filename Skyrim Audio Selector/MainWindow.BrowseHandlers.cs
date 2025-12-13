using Microsoft.Win32;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using WpfMessageBox = System.Windows.MessageBox;

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
