using System.Windows;
using System.Windows.Controls;

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
