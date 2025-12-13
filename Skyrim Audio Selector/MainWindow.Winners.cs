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
        // ---------------- Winner handling ----------------

        private void SetWinnerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConflictKey == null)
            {
                WpfMessageBox.Show("Select a conflict first.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (VariantsDataGrid.SelectedItem is not SoundVariant selected)
            {
                WpfMessageBox.Show("Select a variant row first.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _winners[_currentConflictKey] = selected;

            foreach (var v in _currentVariants)
                v.IsWinner = ReferenceEquals(v, selected);
        }

        private void ClearWinnerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentConflictKey == null) return;

            _winners.Remove(_currentConflictKey);
            foreach (var v in _currentVariants)
                v.IsWinner = false;
        }

    }
}
