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
        private MenuItem _miOpenConflictInExplorer;
        private MenuItem _miOpenVariantInExplorer;

        private bool _isDarkMode = true;

        private SoundVariant _currentlyPlayingVariant;
        private SoundPlayer _soundPlayer;
        private DispatcherTimer _playbackTimer;

        private readonly Dictionary<string, string> _bsaExtractCache =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _transcodedCache =
            new(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, List<SoundVariant>> _conflicts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SoundVariant> _winners =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly ObservableCollection<SoundVariant> _currentVariants = new();
        private string _currentConflictKey;

        private CancellationTokenSource _durationCts;

        private static readonly string FfmpegPath = ResolveFfmpegPath();

        private const string PatchFolderName = "SkyrimAudioSelector_Patch";
        private const string PatchModDisplayName = "SkyrimAudioSelector_Patch (Generated Patch)";

        private static readonly Regex DurationRegex = new(
            @"Duration:\s(?<h>\d+):(?<m>\d+):(?<s>\d+(\.\d+)?)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public MainWindow()
        {
            InitializeComponent();
            VariantsDataGrid.ItemsSource = _currentVariants;

            SetTheme(true);
            SetupExplorerContextMenus();
        }

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

        // ---------------- Explorer context menus ----------------

        private void SetupExplorerContextMenus()
        {
            ConflictsListBox.PreviewMouseRightButtonDown += ConflictsListBox_PreviewMouseRightButtonDown;
            VariantsDataGrid.PreviewMouseRightButtonDown += VariantsDataGrid_PreviewMouseRightButtonDown;

            _miOpenConflictInExplorer = new MenuItem { Header = "Open winner location in Explorer" };
            _miOpenConflictInExplorer.Click += OpenConflictWinnerLocation_Click;

            ConflictsListBox.ContextMenu = new ContextMenu();
            ConflictsListBox.ContextMenu.Items.Add(_miOpenConflictInExplorer);
            ConflictsListBox.ContextMenuOpening += ConflictsListBox_ContextMenuOpening;

            _miOpenVariantInExplorer = new MenuItem { Header = "Open file location in Explorer" };
            _miOpenVariantInExplorer.Click += OpenSelectedVariantLocation_Click;

            VariantsDataGrid.ContextMenu = new ContextMenu();
            VariantsDataGrid.ContextMenu.Items.Add(_miOpenVariantInExplorer);
            VariantsDataGrid.ContextMenuOpening += VariantsDataGrid_ContextMenuOpening;
        }

        private void ConflictsListBox_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not ListBoxItem)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is ListBoxItem item)
                ConflictsListBox.SelectedItem = item.DataContext;
        }

        private void VariantsDataGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && dep is not DataGridRow)
                dep = VisualTreeHelper.GetParent(dep);

            if (dep is DataGridRow row)
                VariantsDataGrid.SelectedItem = row.Item;
        }

        private void ConflictsListBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_miOpenConflictInExplorer == null)
                return;

            if (ConflictsListBox.SelectedItem is not string key)
            {
                _miOpenConflictInExplorer.IsEnabled = false;
                _miOpenConflictInExplorer.ToolTip = null;
                return;
            }

            var variant = GetEffectiveWinnerVariant(key);
            bool ok = variant != null
                      && !variant.FromBsa
                      && !string.IsNullOrWhiteSpace(variant.FilePath)
                      && File.Exists(variant.FilePath);

            _miOpenConflictInExplorer.IsEnabled = ok;
            _miOpenConflictInExplorer.ToolTip = ok
                ? null
                : (variant?.FromBsa == true
                    ? "Disabled: this winner is inside a BSA/BA2 (not a loose file)."
                    : "Disabled: no loose winner file found.");
        }

        private void VariantsDataGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (_miOpenVariantInExplorer == null)
                return;

            if (VariantsDataGrid.SelectedItem is not SoundVariant v)
            {
                _miOpenVariantInExplorer.IsEnabled = false;
                _miOpenVariantInExplorer.ToolTip = null;
                return;
            }

            bool ok = !v.FromBsa
                      && !string.IsNullOrWhiteSpace(v.FilePath)
                      && File.Exists(v.FilePath);

            _miOpenVariantInExplorer.IsEnabled = ok;
            _miOpenVariantInExplorer.ToolTip = ok
                ? null
                : (v.FromBsa
                    ? "Disabled: this file is inside a BSA/BA2 (not a loose file)."
                    : "Disabled: file not found on disk.");
        }

        private void OpenConflictWinnerLocation_Click(object sender, RoutedEventArgs e)
        {
            if (ConflictsListBox.SelectedItem is not string key)
                return;

            var variant = GetEffectiveWinnerVariant(key);
            if (variant == null || variant.FromBsa)
                return;

            OpenInExplorerSelectFile(variant.FilePath);
        }

        private void OpenSelectedVariantLocation_Click(object sender, RoutedEventArgs e)
        {
            if (VariantsDataGrid.SelectedItem is not SoundVariant v)
                return;

            if (v.FromBsa)
                return;

            OpenInExplorerSelectFile(v.FilePath);
        }

        private SoundVariant GetEffectiveWinnerVariant(string conflictKey)
        {
            if (string.IsNullOrWhiteSpace(conflictKey) || _conflicts == null)
                return null;

            if (_winners.TryGetValue(conflictKey, out var explicitWinner) && explicitWinner != null)
                return explicitWinner;

            if (_conflicts.TryGetValue(conflictKey, out var list) && list != null && list.Count > 0)
                return list.OrderBy(v => v.Mod.Priority).LastOrDefault();

            return null;
        }

        private void OpenInExplorerSelectFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Failed to open Explorer:\n" + ex.Message,
                    "Explorer error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);

            StopPlayback();
            _durationCts?.Cancel();
        }

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

        // ---------------- Duration helpers ----------------

        private void EnsureDurationsForVariants(IEnumerable<SoundVariant> variants)
        {
            if (variants == null) return;

            var snapshot = variants
                .Where(v => v != null && !v.DurationSeconds.HasValue)
                .ToArray();

            if (snapshot.Length == 0)
                return;

            Task.Run(() =>
            {
                foreach (var v in snapshot)
                    ComputeDurationForVariantIfNeeded(v, CancellationToken.None);
            });
        }

        private void StartGlobalDurationComputation()
        {
            if (_conflicts == null || _conflicts.Count == 0)
                return;

            _durationCts?.Cancel();
            _durationCts = new CancellationTokenSource();
            var token = _durationCts.Token;

            var allVariants = _conflicts.Values.SelectMany(list => list).ToList();

            Task.Run(() =>
            {
                foreach (var v in allVariants)
                {
                    if (token.IsCancellationRequested)
                        break;

                    ComputeDurationForVariantIfNeeded(v, token);
                }
            }, token);
        }

        private void ComputeDurationForVariantIfNeeded(SoundVariant variant, CancellationToken token)
        {
            if (variant == null || variant.DurationSeconds.HasValue || token.IsCancellationRequested)
                return;

            try
            {
                string sourcePath = variant.FromBsa
                    ? ExtractBsaEntryToTemp(variant)
                    : variant.FilePath;

                double? dur = GetAudioDurationSeconds(sourcePath);
                if (!dur.HasValue || token.IsCancellationRequested)
                    return;

                Dispatcher.Invoke(() =>
                {
                    if (!token.IsCancellationRequested)
                        variant.DurationSeconds = dur.Value;
                });
            }
            catch
            {
            }
        }

        private double? GetAudioDurationSeconds(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return null;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = FfmpegPath,
                    Arguments = $"-i \"{sourcePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc == null)
                    return null;

                string stderr = proc.StandardError.ReadToEnd();
                string stdout = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                string text = stderr + "\n" + stdout;

                var match = DurationRegex.Match(text);
                if (!match.Success)
                    return null;

                int h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                int m = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
                double s = double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);

                double totalSeconds = h * 3600 + m * 60 + s;
                if (totalSeconds <= 0.01)
                    return null;

                return totalSeconds;
            }
            catch
            {
                return null;
            }
        }

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

        // ---------------- Generate patch (loose or BSA) ----------------

        private void GeneratePatchButton_Click(object sender, RoutedEventArgs e)
        {
            string modsRoot = ModsRootTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                WpfMessageBox.Show("Mods root must be set (needed when output is left empty).",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string outputRoot = OutputModTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputRoot))
                outputRoot = modsRoot;

            string patchRoot = Path.Combine(outputRoot, PatchFolderName);

            bool packToBsa = PackToBsaCheckBox.IsChecked == true;
            string bsarchPath = BsarchPathTextBox.Text.Trim();

            try
            {
                if (SafeModeCheckBox.IsChecked == true && _winners.Count > 0)
                {
                    var nonSafe = new List<(string Key, SoundVariant Variant)>();

                    foreach (var kvp in _winners)
                    {
                        if (!IsSafeModeConflict(kvp.Key))
                            nonSafe.Add((kvp.Key, kvp.Value));
                    }

                    if (nonSafe.Count > 0)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Safe mode is enabled, but you have selected winners for conflicts that do NOT involve the base game (mod-vs-mod conflicts).");
                        sb.AppendLine();
                        sb.AppendLine("These audio files are affected:");
                        sb.AppendLine();

                        foreach (var item in nonSafe)
                        {
                            string ext = Path.GetExtension(item.Variant.FilePath);
                            if (string.IsNullOrEmpty(ext))
                                ext = ".wav";

                            sb.AppendLine($"- {item.Variant.Mod.Name}: {item.Key}{ext} ({item.Variant.SourceDescription})");
                        }

                        sb.AppendLine();
                        sb.Append("Do you want to continue and include these files in the patch?");

                        var result = WpfMessageBox.Show(
                            sb.ToString(),
                            "Safe mode warning",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result != MessageBoxResult.Yes)
                            return;
                    }
                }

                ClearPreviousPatchOutputs(patchRoot);

                ConflictManager.GeneratePatchMod(patchRoot, _winners);

                string message;

                if (!packToBsa)
                {
                    message = $"Patch mod generated as loose files under:\n{patchRoot}";
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(bsarchPath) || !File.Exists(bsarchPath))
                    {
                        message = $"Patch mod generated as loose files under:\n{patchRoot}\n\n" +
                                  "BSA packing was requested but bsarch.exe was not found or invalid.\n" +
                                  "Install/provide bsarch.exe and try again if you want a BSA-only patch.";
                    }
                    else
                    {
                        string bsaName = PatchFolderName + ".bsa";
                        string archivePath = Path.Combine(patchRoot, bsaName);

                        PackPatchWithBsarch(bsarchPath, patchRoot, archivePath);

                        DeleteLooseAudioFilesFromPatch(patchRoot, archivePath);

                        message = $"Patch mod generated as BSA under:\n{archivePath}\n\n" +
                                  "Loose audio files in this patch folder were deleted (BSA-only mode).";
                    }
                }

                WpfMessageBox.Show(message, "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                DoScan(showPopup: false);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error while generating patch mod / packing BSA:\n" + ex,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void PackPatchWithBsarch(string bsarchPath, string sourceDir, string archivePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = bsarchPath,
                Arguments = $"pack \"{sourceDir}\" \"{archivePath}\" -sse -z",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Failed to start bsarch.exe.");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"bsarch.exe exited with code {proc.ExitCode}.\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");
            }
        }

        private static void DeleteLooseAudioFilesFromPatch(string patchRoot, string archivePath)
        {
            foreach (var file in Directory.EnumerateFiles(patchRoot, "*.*", SearchOption.AllDirectories))
            {
                if (string.Equals(file, archivePath, StringComparison.OrdinalIgnoreCase))
                    continue;

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

        // ---------------- Playback ----------------

        private static string NormalizeArchivePath(string p)
            => p.Replace('\\', '/').TrimStart('/');

        private string ExtractBsaEntryToTemp(SoundVariant variant)
        {
            if (!variant.FromBsa)
                throw new InvalidOperationException("Variant is not from BSA/BA2.");

            string cacheKey = $"{variant.BsaPath}|{variant.FilePath}".ToLowerInvariant();

            lock (_bsaExtractCache)
            {
                if (_bsaExtractCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                    return cached;
            }

            string tempRoot = Path.Combine(
                Path.GetTempPath(), "SkyrimAudioSelector", "BsaExtracts");
            Directory.CreateDirectory(tempRoot);

            string fileName = Path.GetFileName(
                variant.FilePath.Replace('/', Path.DirectorySeparatorChar));
            string outPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}_{fileName}");

            var reader = Archive.CreateReader(GameRelease.SkyrimSE, variant.BsaPath);
            string wantedPath = NormalizeArchivePath(variant.FilePath);

            var entry = reader.Files.FirstOrDefault(f =>
                string.Equals(
                    NormalizeArchivePath(f.Path),
                    wantedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new FileNotFoundException(
                    $"Entry {variant.FilePath} not found in {variant.BsaPath}.");

            File.WriteAllBytes(outPath, entry.GetBytes());

            lock (_bsaExtractCache)
            {
                _bsaExtractCache[cacheKey] = outPath;
            }

            return outPath;
        }

        private string TranscodeToWav(string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            if (_transcodedCache.TryGetValue(sourcePath, out string cached) && File.Exists(cached))
                return cached;

            string tempRoot = Path.Combine(
                Path.GetTempPath(), "SkyrimAudioSelector", "Transcoded");
            Directory.CreateDirectory(tempRoot);

            string outFile = Path.Combine(
                tempRoot,
                Path.GetFileNameWithoutExtension(sourcePath) + "_" +
                Guid.NewGuid().ToString("N") + ".wav");

            var psi = new ProcessStartInfo
            {
                FileName = FfmpegPath,
                Arguments = $"-y -i \"{sourcePath}\" \"{outFile}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException("Failed to start ffmpeg process.");

            string stderr = proc.StandardError.ReadToEnd();
            string stdout = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit();

            if (proc.ExitCode != 0 || !File.Exists(outFile))
            {
                throw new InvalidOperationException(
                    $"ffmpeg failed with exit code {proc.ExitCode}.\n\nSTDOUT:\n{stdout}\n\nSTDERR:\n{stderr}");
            }

            _transcodedCache[sourcePath] = outFile;
            return outFile;
        }

        private void StopPlaybackTimer()
        {
            if (_playbackTimer != null)
            {
                try { _playbackTimer.Stop(); } catch { }
                _playbackTimer = null;
            }
        }

        private void StartPlaybackTimer(SoundVariant variant, double seconds)
        {
            StopPlaybackTimer();

            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };
            _playbackTimer.Tick += (s, e) =>
            {
                _playbackTimer.Stop();
                _playbackTimer = null;

                if (_currentlyPlayingVariant == variant)
                    StopPlayback();
            };
            _playbackTimer.Start();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SoundVariant variant)
                return;

            if (_currentlyPlayingVariant == variant && variant.IsPlaying)
            {
                StopPlayback();
                return;
            }

            PlaySound(variant);
        }

        private void PlaySound(SoundVariant variant)
        {
            StopPlayback();

            try
            {
                string sourcePath = variant.FromBsa
                    ? ExtractBsaEntryToTemp(variant)
                    : variant.FilePath;

                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("Source file not found.", sourcePath);

                string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
                string wavToPlay = ext == ".wav"
                    ? sourcePath
                    : TranscodeToWav(sourcePath);

                if (!File.Exists(wavToPlay))
                    throw new FileNotFoundException("WAV file to play not found.", wavToPlay);

                _soundPlayer = new SoundPlayer(wavToPlay);
                _soundPlayer.Load();
                _soundPlayer.Play();

                _currentlyPlayingVariant = variant;
                variant.IsPlaying = true;

                double seconds = (variant.DurationSeconds.HasValue && variant.DurationSeconds.Value > 0.01)
                    ? variant.DurationSeconds.Value
                    : 5.0;

                if (seconds < 1.0)
                    seconds = 1.0;

                StartPlaybackTimer(variant, seconds);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error starting playback:\n" + ex,
                    "Playback error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopPlayback()
        {
            StopPlaybackTimer();

            if (_soundPlayer != null)
            {
                try { _soundPlayer.Stop(); } catch { }
                _soundPlayer.Dispose();
                _soundPlayer = null;
            }

            if (_currentlyPlayingVariant != null)
            {
                _currentlyPlayingVariant.IsPlaying = false;
                _currentlyPlayingVariant = null;
            }
        }

        private void VariantsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }

    // ---------------- Data models ----------------

    public class ModInfo
    {
        public string Name { get; }
        public string DirectoryPath { get; }
        public bool Enabled { get; }
        public int Priority { get; }
        public bool IsPatch { get; }

        public ModInfo(string name, string directoryPath, bool enabled, int priority, bool isPatch = false)
        {
            Name = name;
            DirectoryPath = directoryPath;
            Enabled = enabled;
            Priority = priority;
            IsPatch = isPatch;
        }
    }

    public class SoundVariant : INotifyPropertyChanged
    {
        public ModInfo Mod { get; }

        public string FilePath { get; }
        public string RelativeKey { get; }

        public bool FromBsa { get; }
        public string BsaPath { get; }

        private bool _isWinner;
        public bool IsWinner
        {
            get => _isWinner;
            set
            {
                if (_isWinner != value)
                {
                    _isWinner = value;
                    OnPropertyChanged(nameof(IsWinner));
                    OnPropertyChanged(nameof(WinnerMarker));
                }
            }
        }

        public string WinnerMarker => IsWinner ? "★" : "";

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                    OnPropertyChanged(nameof(PlayButtonText));
                }
            }
        }

        public string PlayButtonText => IsPlaying ? "Stop" : "Play";

        private double? _durationSeconds;
        public double? DurationSeconds
        {
            get => _durationSeconds;
            set
            {
                if (_durationSeconds != value)
                {
                    _durationSeconds = value;
                    OnPropertyChanged(nameof(DurationSeconds));
                    OnPropertyChanged(nameof(DurationDisplay));
                }
            }
        }

        public string DurationDisplay
        {
            get
            {
                if (!_durationSeconds.HasValue)
                    return string.Empty;

                int roundedSeconds = (int)Math.Round(
                    _durationSeconds.Value,
                    MidpointRounding.AwayFromZero);

                if (roundedSeconds < 1)
                    roundedSeconds = 1;

                var ts = TimeSpan.FromSeconds(roundedSeconds);

                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}";

                return $"{ts.Minutes}:{ts.Seconds:00}";
            }
        }

        public string ModName => Mod.Name;

        public string DisplayPriority =>
            string.Equals(Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase)
                ? "Base"
                : (Mod.IsPatch ? "Patch" : Mod.Priority.ToString());

        public int ModPriority => Mod.Priority;

        public string SourceDescription =>
            FromBsa ? $"BSA: {Path.GetFileName(BsaPath)}" : "Loose file";

        public SoundVariant(ModInfo mod, string filePath, string relativeKey)
            : this(mod, filePath, relativeKey, fromBsa: false, bsaPath: null)
        { }

        public SoundVariant(ModInfo mod, string filePath, string relativeKey, bool fromBsa, string bsaPath)
        {
            Mod = mod;
            FilePath = filePath;
            RelativeKey = relativeKey;
            FromBsa = fromBsa;
            BsaPath = bsaPath;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // ---------------- Conflict manager ----------------

    public static class ConflictManager
    {
        public const string BaseGameModName = "Skyrim (Base Game)";

        private static bool IsMo2VfsActive()
        {
            try
            {
                foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
                {
                    string name = m?.ModuleName ?? "";
                    if (name.IndexOf("usvfs", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MO2_PATH"))) return true;
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MO2_INSTANCE"))) return true;

            return false;
        }

        private static bool LooksLikeVanillaArchiveName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;

            return fileName.StartsWith("Skyrim - ", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Update", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Dawnguard", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("Dragonborn", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("HearthFires", StringComparison.OrdinalIgnoreCase)
                || fileName.StartsWith("cc", StringComparison.OrdinalIgnoreCase);
        }

        public static ModInfo CreateBaseGameMod(string skyrimDataPath)
        {
            return new ModInfo(
                name: BaseGameModName,
                directoryPath: skyrimDataPath,
                enabled: true,
                priority: int.MinValue);
        }

        private static string NormalizeArchivePath(string p)
            => p.Replace('\\', '/').TrimStart('/');

        private static string ComputeRelativeAudioKeyFromRelative(string relPath)
        {
            string rel = relPath.Replace('\\', '/');
            var parts = rel.Split('/');
            int idx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].ToLowerInvariant();
                if (p == "sound" || p == "music")
                {
                    idx = i;
                    break;
                }
            }

            if (idx == -1) return null;

            string path = string.Join("/", parts.Skip(idx)).ToLowerInvariant();

            int lastSlash = path.LastIndexOf('/');
            int lastDot = path.LastIndexOf('.');
            if (lastDot > lastSlash)
                path = path.Substring(0, lastDot);

            return path;
        }

        private static string ComputeRelativeAudioKey(string modRoot, string file)
        {
            string rel = Path.GetRelativePath(modRoot, file)
                            .Replace('\\', '/');

            var parts = rel.Split('/');
            int idx = -1;
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].ToLowerInvariant();
                if (p == "sound" || p == "music")
                {
                    idx = i;
                    break;
                }
            }

            if (idx == -1) return null;

            string path = string.Join("/", parts.Skip(idx)).ToLowerInvariant();

            int lastSlash = path.LastIndexOf('/');
            int lastDot = path.LastIndexOf('.');
            if (lastDot > lastSlash)
                path = path.Substring(0, lastDot);

            return path;
        }

        private static bool IsAudioFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            return IsAudioExtension(ext);
        }

        private static bool IsAudioExtension(string ext)
        {
            return ext == ".xwm" || ext == ".wav" || ext == ".mp3" || ext == ".ogg";
        }

        public static Dictionary<string, ModInfo> LoadMods(string modsRoot, string modlistPath)
        {
            var mods = new Dictionary<string, ModInfo>(StringComparer.OrdinalIgnoreCase);
            var priorityByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var enabledByName = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(modlistPath) && File.Exists(modlistPath))
            {
                var lines = File.ReadAllLines(modlistPath);
                int index = 0;
                foreach (var raw in lines)
                {
                    var line = raw.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                        continue;

                    bool enabled = true;
                    string modName;
                    char first = line[0];
                    if (first == '+' || first == '-')
                    {
                        enabled = (first == '+');
                        modName = line.Substring(1).Trim();
                    }
                    else
                    {
                        modName = line;
                    }

                    if (string.IsNullOrEmpty(modName))
                        continue;

                    priorityByName[modName] = index++;
                    if (enabled)
                        enabledByName.Add(modName);
                }
            }

            var dirs = Directory.GetDirectories(modsRoot);
            int fallbackIndex = priorityByName.Count;

            foreach (var dir in dirs)
            {
                string name = Path.GetFileName(dir);

                bool enabled;
                int priority;
                if (priorityByName.Count > 0)
                {
                    bool listed = priorityByName.ContainsKey(name);
                    enabled = enabledByName.Count > 0 ? enabledByName.Contains(name) : listed;
                    priority = listed ? priorityByName[name] : fallbackIndex++;
                }
                else
                {
                    enabled = true;
                    priority = fallbackIndex++;
                }

                if (!enabled) continue;

                mods[name] = new ModInfo(name, dir, enabled, priority);
            }

            return mods;
        }

        public static Dictionary<string, List<SoundVariant>> ScanForAudioConflicts(IEnumerable<ModInfo> mods)
        {
            var byKey = new Dictionary<string, List<SoundVariant>>(StringComparer.OrdinalIgnoreCase);

            bool mo2Vfs = IsMo2VfsActive();

            foreach (var mod in mods)
            {
                bool isBase = string.Equals(mod.Name, BaseGameModName, StringComparison.OrdinalIgnoreCase);

                if (!(mo2Vfs && isBase))
                {
                    foreach (var file in Directory.EnumerateFiles(mod.DirectoryPath, "*.*", SearchOption.AllDirectories))
                    {
                        if (!IsAudioFile(file)) continue;
                        var key = ComputeRelativeAudioKey(mod.DirectoryPath, file);
                        if (key == null) continue;

                        var variant = new SoundVariant(mod, file, key, fromBsa: false, bsaPath: null);
                        if (!byKey.TryGetValue(key, out var list))
                        {
                            list = new List<SoundVariant>();
                            byKey[key] = list;
                        }
                        list.Add(variant);
                    }
                }

                var archives = Directory.EnumerateFiles(mod.DirectoryPath, "*.bsa", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(mod.DirectoryPath, "*.ba2", SearchOption.TopDirectoryOnly));

                if (mo2Vfs && isBase)
                    archives = archives.Where(p => LooksLikeVanillaArchiveName(Path.GetFileName(p)));

                foreach (var bsaPath in archives)
                {
                    var reader = Archive.CreateReader(GameRelease.SkyrimSE, bsaPath);
                    foreach (var file in reader.Files)
                    {
                        string entryPath = file.Path;
                        string ext = Path.GetExtension(entryPath).ToLowerInvariant();
                        if (!IsAudioExtension(ext)) continue;

                        var key = ComputeRelativeAudioKeyFromRelative(entryPath);
                        if (key == null) continue;

                        var variant = new SoundVariant(mod, entryPath, key, fromBsa: true, bsaPath: bsaPath);
                        if (!byKey.TryGetValue(key, out var list))
                        {
                            list = new List<SoundVariant>();
                            byKey[key] = list;
                        }
                        list.Add(variant);
                    }
                }
            }

            var conflicts = new Dictionary<string, List<SoundVariant>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in byKey)
                if (kvp.Value.Count > 1) conflicts[kvp.Key] = kvp.Value;

            return conflicts;
        }

        public static void GeneratePatchMod(string patchRootPath, Dictionary<string, SoundVariant> winners)
        {
            if (winners == null || winners.Count == 0)
                return;

            Directory.CreateDirectory(patchRootPath);

            foreach (var kvp in winners)
            {
                string key = kvp.Key;
                var winner = kvp.Value;

                string ext = Path.GetExtension(winner.FilePath);
                string relativeWithExt = key + ext;

                string destPath = Path.Combine(
                    patchRootPath,
                    relativeWithExt.Replace('/', Path.DirectorySeparatorChar));

                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                if (!winner.FromBsa)
                {
                    File.Copy(winner.FilePath, destPath, overwrite: true);
                }
                else
                {
                    string tempRoot = Path.Combine(
                        Path.GetTempPath(), "SkyrimAudioSelector", "BsaPatchExtracts");
                    Directory.CreateDirectory(tempRoot);

                    string fileName = Path.GetFileName(
                        winner.FilePath.Replace('/', Path.DirectorySeparatorChar));
                    string outPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}_{fileName}");

                    var reader = Archive.CreateReader(GameRelease.SkyrimSE, winner.BsaPath);
                    string wantedPath = NormalizeArchivePath(winner.FilePath);

                    var entry = reader.Files.FirstOrDefault(f =>
                        string.Equals(
                            NormalizeArchivePath(f.Path),
                            wantedPath,
                            StringComparison.OrdinalIgnoreCase));

                    if (entry == null)
                        throw new FileNotFoundException(
                            $"Entry {winner.FilePath} not found in {winner.BsaPath}.");

                    File.WriteAllBytes(outPath, entry.GetBytes());
                    File.Copy(outPath, destPath, overwrite: true);
                }
            }
        }
    }
}
