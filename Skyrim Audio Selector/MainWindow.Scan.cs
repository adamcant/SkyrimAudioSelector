using System.IO;
using System.Windows;
using System.Windows.Controls;
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
            bool showVanilla = ShowVanillaConflictsCheckBox.IsChecked == true;
            bool safeMode = SafeModeCheckBox.IsChecked == true;

            var filteredKeys = new List<string>(_conflicts.Count);

            foreach (var kvp in _conflicts)
            {
                if (ShouldShowConflict(kvp.Key, kvp.Value, selectedMod, search, showVanilla, safeMode))
                    filteredKeys.Add(kvp.Key);
            }

            filteredKeys.Sort(StringComparer.OrdinalIgnoreCase);
            ConflictsListBox.ItemsSource = filteredKeys;
        }

        private static bool ShouldShowConflict(
            string key,
            List<SoundVariant> variants,
            string selectedMod,
            string search,
            bool showVanilla,
            bool safeMode)
        {
            if (string.IsNullOrEmpty(key) || variants == null || variants.Count == 0)
                return false;

            if (!string.IsNullOrEmpty(search) &&
                key.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            bool involvesBase = false;
            bool hasNonBase = false;
            bool involvesPatch = false;

            string firstNonBaseMod = null;
            bool hasSecondNonBaseMod = false;

            bool anyFromSelected = selectedMod == null;

            foreach (var v in variants)
            {
                if (v?.Mod == null)
                    continue;

                string modName = v.Mod.Name;

                if (!anyFromSelected && string.Equals(modName, selectedMod, StringComparison.OrdinalIgnoreCase))
                    anyFromSelected = true;

                if (v.Mod.IsPatch)
                    involvesPatch = true;

                if (string.Equals(modName, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase))
                {
                    involvesBase = true;
                    continue;
                }

                hasNonBase = true;

                if (!hasSecondNonBaseMod)
                {
                    if (firstNonBaseMod == null)
                        firstNonBaseMod = modName;
                    else if (!string.Equals(firstNonBaseMod, modName, StringComparison.OrdinalIgnoreCase))
                        hasSecondNonBaseMod = true;
                }
            }

            if (selectedMod != null && !anyFromSelected)
                return false;

            if (safeMode && !(involvesBase && hasNonBase))
                return false;

            if (!showVanilla && involvesBase && !hasSecondNonBaseMod && !involvesPatch)
                return false;

            return true;
        }

        private bool IsSafeModeConflict(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;

            if (_conflicts == null || !_conflicts.TryGetValue(key, out var variants) || variants == null || variants.Count == 0)
                return false;

            bool hasBase = false;
            bool hasNonBase = false;

            foreach (var v in variants)
            {
                if (v?.Mod == null)
                    continue;

                bool isBase = string.Equals(v.Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase);
                hasBase |= isBase;
                hasNonBase |= !isBase;

                if (hasBase && hasNonBase)
                    return true;
            }

            return false;
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
                    WpfMessageBox.Show(
                        "Please select a valid mods root directory.",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                return;
            }

            if (string.IsNullOrWhiteSpace(skyrimData) || !Directory.Exists(skyrimData))
            {
                if (showPopup)
                {
                    WpfMessageBox.Show(
                        "Please select a valid Skyrim Data folder (Under game root, make sure folder is not empty).",
                        "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
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

                modsToScan.Add(ConflictManager.CreateBaseGameMod(skyrimData));
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
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            DoScan(showPopup: true);
        }
    }
}
