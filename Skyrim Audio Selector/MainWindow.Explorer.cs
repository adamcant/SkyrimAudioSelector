
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfMessageBox = System.Windows.MessageBox;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
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
            {
                SoundVariant best = null;
                int bestPriority = int.MinValue;

                foreach (var v in list)
                {
                    if (v?.Mod == null)
                        continue;

                    int p = v.Mod.Priority;
                    if (best == null || p >= bestPriority)
                    {
                        best = v;
                        bestPriority = p;
                    }
                }

                return best;
            }

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

    }
}
