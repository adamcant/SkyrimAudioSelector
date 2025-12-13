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
        // ---------------- Scan / filter plumbing ----------------

        private void RebuildModFilterList()
        {
            if (_conflicts == null || _conflicts.Count == 0)
            {
                ModFilterComboBox.ItemsSource = null;
                return;
            }

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var list in _conflicts.Values)
            {
                foreach (var v in list)
                    names.Add(v.Mod.Name);
            }

            var items = new List<string> { "<All mods>" };
            items.AddRange(names);

            ModFilterComboBox.ItemsSource = items;
            ModFilterComboBox.SelectedIndex = 0;
        }

        private void ShowVanillaConflictsCheckBox_Click(object sender, RoutedEventArgs e) => ApplyConflictFilters();
        private void SafeModeCheckBox_Click(object sender, RoutedEventArgs e) => ApplyConflictFilters();
        private void ModFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyConflictFilters();
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyConflictFilters();

        private void ApplyConflictFilters()
        {
            if (_conflicts == null || _conflicts.Count == 0)
            {
                ConflictsListBox.ItemsSource = null;
                return;
            }

            string selectedMod = ModFilterComboBox.SelectedItem as string;
            if (!string.IsNullOrEmpty(selectedMod) &&
                string.Equals(selectedMod, "<All mods>", StringComparison.OrdinalIgnoreCase))
            {
                selectedMod = null;
            }

            string search = (SearchTextBox.Text ?? string.Empty).Trim();
            bool hasSearch = !string.IsNullOrEmpty(search);

            bool showVanilla = ShowVanillaConflictsCheckBox.IsChecked == true;
            bool safeMode = SafeModeCheckBox.IsChecked == true;

            var filteredKeys = _conflicts.Keys
                .Where(key =>
                {
                    if (!_conflicts.TryGetValue(key, out var variants) ||
                        variants == null || variants.Count == 0)
                        return false;

                    if (hasSearch &&
                        key.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
                        return false;

                    if (safeMode)
                    {
                        bool hasBase = variants.Any(v =>
                            string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase));

                        bool hasNonBase = variants.Any(v =>
                            !string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase));

                        if (!(hasBase && hasNonBase))
                            return false;
                    }

                    if (!showVanilla)
                    {
                        bool involvesBase = variants.Any(v =>
                            string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase));

                        var nonBaseMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var v in variants)
                        {
                            if (!string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase))
                                nonBaseMods.Add(v.Mod.Name);
                        }

                        bool involvesPatch = variants.Any(v => v.Mod.IsPatch);

                        if (involvesBase && nonBaseMods.Count <= 1 && !involvesPatch)
                            return false;
                    }

                    if (selectedMod != null)
                    {
                        bool anyFromSelected = variants.Any(v =>
                            string.Equals(v.Mod.Name, selectedMod, StringComparison.OrdinalIgnoreCase));
                        if (!anyFromSelected)
                            return false;
                    }

                    return true;
                })
                .OrderBy(k => k)
                .ToList();

            ConflictsListBox.ItemsSource = filteredKeys;
        }

        private bool IsSafeModeConflict(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (_conflicts == null ||
                !_conflicts.TryGetValue(key, out var variants) ||
                variants == null || variants.Count == 0)
                return false;

            bool hasBase = variants.Any(v =>
                string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase));

            bool hasNonBase = variants.Any(v =>
                !string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase));

            return hasBase && hasNonBase;
        }
        private void DoScan(bool showPopup)
        {
            string modsRoot = ModsRootTextBox.Text.Trim();
            string modlistPath = ModlistTextBox.Text.Trim();
            string skyrimData = SkyrimDataTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                if (showPopup)
                {
                    WpfMessageBox.Show("Please select a valid mods root directory.",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(skyrimData) || !Directory.Exists(skyrimData))
            {
                if (showPopup)
                {
                    WpfMessageBox.Show(
                        "Please select a valid Skyrim Data folder (Under game root, make sure folder is not empty).",
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            try
            {
                var modsDict = ConflictManager.LoadMods(
                    modsRoot,
                    string.IsNullOrWhiteSpace(modlistPath) ? null : modlistPath);

                modsDict.Remove(PatchFolderName);

                var modsToScan = new List<ModInfo>();

                var baseGameMod = ConflictManager.CreateBaseGameMod(skyrimData);
                modsToScan.Add(baseGameMod);

                modsToScan.AddRange(modsDict.Values);

                string patchFolder = FindExistingPatchFolder(modsRoot, OutputModTextBox.Text.Trim());
                if (!string.IsNullOrWhiteSpace(patchFolder))
                {
                    modsToScan.Add(new ModInfo(
                        name: PatchModDisplayName,
                        directoryPath: patchFolder,
                        enabled: true,
                        priority: int.MaxValue,
                        isPatch: true));
                }

                _conflicts = ConflictManager.ScanForAudioConflicts(modsToScan);

                _winners.Clear();
                _currentConflictKey = null;
                _currentVariants.Clear();

                AutoSelectExistingPatchWinners();

                StartGlobalDurationComputation();

                RebuildModFilterList();
                ApplyConflictFilters();

                if (showPopup)
                {
                    WpfMessageBox.Show(
                        $"Found {_conflicts.Count} conflicts.",
                        "Scan complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {

                WpfMessageBox.Show(
                    "Error while scanning mods:\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            DoScan(showPopup: true);
        }

    }
}
