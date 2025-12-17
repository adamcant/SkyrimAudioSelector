using System.Windows;
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
