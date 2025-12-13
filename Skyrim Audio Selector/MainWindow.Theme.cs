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
        // ---------------- THEME ----------------

        private void DarkModeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            bool dark = DarkModeCheckBox.IsChecked == true;
            SetTheme(dark);
        }

        private SolidColorBrush MakeBrush(byte r, byte g, byte b)
            => new SolidColorBrush(MediaColor.FromRgb(r, g, b));

        private static string ResolveFfmpegPath()
        {
            try
            {
                string local = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
                if (File.Exists(local))
                    return local;
            }
            catch { }

            return "ffmpeg.exe";
        }

        private void SetTheme(bool dark)
        {
            _isDarkMode = dark;

            SolidColorBrush bgMain;
            SolidColorBrush bgPanel;
            SolidColorBrush bgControl;
            SolidColorBrush bgControlAlt;
            SolidColorBrush fgMain;
            SolidColorBrush accent;

            if (dark)
            {
                bgMain = MakeBrush(0x1E, 0x1E, 0x1E);
                bgPanel = MakeBrush(0x25, 0x25, 0x25);
                bgControl = MakeBrush(0x2F, 0x2F, 0x2F);
                bgControlAlt = MakeBrush(0x3A, 0x3A, 0x3A);
                fgMain = MakeBrush(0xEA, 0xEA, 0xEA);
                accent = MakeBrush(0x3C, 0x78, 0xD8);
            }
            else
            {
                bgMain = MakeBrush(0xF0, 0xF0, 0xF0);
                bgPanel = MakeBrush(0xFF, 0xFF, 0xFF);
                bgControl = MakeBrush(0xF8, 0xF8, 0xF8);
                bgControlAlt = MakeBrush(0xE0, 0xE0, 0xE0);
                fgMain = MakeBrush(0x20, 0x20, 0x20);
                accent = MakeBrush(0x00, 0x67, 0xC0);
            }

            Resources["BgMain"] = bgMain;
            Resources["BgPanel"] = bgPanel;
            Resources["BgControl"] = bgControl;
            Resources["BgControlAlt"] = bgControlAlt;
            Resources["FgMain"] = fgMain;
            Resources["Accent"] = accent;

            Background = bgMain;
            Foreground = fgMain;
        }

    }
}
