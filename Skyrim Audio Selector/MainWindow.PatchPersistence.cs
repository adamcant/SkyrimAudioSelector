using System.IO;
using System.Windows;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        // ---------------- PATCH PERSISTENCE ----------------

        private string? FindExistingPatchFolder(string modsRoot, string outputRootText)
        {
            string outputRoot = string.IsNullOrWhiteSpace(outputRootText) ? modsRoot : outputRootText;

            string candidate = Path.Combine(outputRoot, PatchFolderName);
            if (Directory.Exists(candidate))
                return candidate;

            if (!string.Equals(outputRoot, modsRoot, StringComparison.OrdinalIgnoreCase))
            {
                candidate = Path.Combine(modsRoot, PatchFolderName);
                if (Directory.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private void AutoSelectExistingPatchWinners()
        {
            if (_conflicts == null || _conflicts.Count == 0)
                return;

            foreach (var (key, list) in _conflicts)
            {
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
            PatchOutputs.TryDeleteFile(expectedBsa);
            PatchOutputs.DeleteAudioFiles(patchRoot);
        }
    }
}
