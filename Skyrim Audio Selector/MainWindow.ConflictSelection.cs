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
        // ---------------- Conflict selection ----------------

        private void ConflictsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            StopPlayback();

            if (ConflictsListBox.SelectedItem is not string key)
            {
                _currentConflictKey = null;
                _currentVariants.Clear();
                return;
            }

            _currentConflictKey = key;
            _currentVariants.Clear();

            if (_conflicts.TryGetValue(key, out var list))
            {
                list.Sort((a, b) => a.Mod.Priority.CompareTo(b.Mod.Priority));

                _winners.TryGetValue(key, out var winner);
                foreach (var v in list)
                {
                    v.IsWinner = (winner != null && ReferenceEquals(v, winner));
                    v.IsPlaying = false;
                    _currentVariants.Add(v);
                }

                EnsureDurationsForVariants(list);
            }
        }

    }
}
