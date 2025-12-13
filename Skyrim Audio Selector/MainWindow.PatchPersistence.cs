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
        // ---------------- PATCH PERSISTENCE ----------------

        private string FindExistingPatchFolder(string modsRoot, string outputRootText)
        {
            string outputRoot = string.IsNullOrWhiteSpace(outputRootText) ? modsRoot : outputRootText;

            string candidate1 = Path.Combine(outputRoot, PatchFolderName);
            if (Directory.Exists(candidate1))
                return candidate1;

            string candidate2 = Path.Combine(modsRoot, PatchFolderName);
            if (Directory.Exists(candidate2))
                return candidate2;

            return null;
        }

        private void AutoSelectExistingPatchWinners()
        {
            if (_conflicts == null || _conflicts.Count == 0)
                return;

            foreach (var kvp in _conflicts)
            {
                string key = kvp.Key;
                var list = kvp.Value;
                if (list == null || list.Count == 0)
                    continue;

                var patchVariant = list
                    .Where(v => v?.Mod?.IsPatch == true)
                    .OrderBy(v => v.FromBsa ? 1 : 0)
                    .FirstOrDefault();

                if (patchVariant != null)
                    _winners[key] = patchVariant;
            }
        }

        private static void ClearPreviousPatchOutputs(string patchRoot)
        {
            if (string.IsNullOrWhiteSpace(patchRoot))
                return;

            Directory.CreateDirectory(patchRoot);

            string expectedBsa = Path.Combine(patchRoot, PatchFolderName + ".bsa");
            if (File.Exists(expectedBsa))
            {
                try { File.Delete(expectedBsa); } catch {
                }
            }

            foreach (var file in Directory.EnumerateFiles(patchRoot, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".wav" || ext == ".xwm" || ext == ".mp3" || ext == ".ogg")
                {
                    try { File.Delete(file); } catch {
                    }
                }
            }

            foreach (var dir in Directory.GetDirectories(patchRoot, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, false);
                }
                catch {
                }
            }
        }

    }
}
