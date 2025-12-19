using System.IO;
using System.Text;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        // ---------------- Generate patch (loose or BSA) ----------------

        private void GeneratePatchButton_Click(object sender, RoutedEventArgs e)
        {
            string modsRoot = ModsRootTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            {
                WpfMessageBox.Show(
                    "Mods root must be set.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            string outputRoot = OutputModTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputRoot))
                outputRoot = modsRoot;

            string patchRoot = Path.Combine(outputRoot, PatchFolderName);

            bool packToBsa = PackToBsaCheckBox.IsChecked == true;
            string bsarchPath = BsarchPathTextBox.Text.Trim();

            if (_winners.Count == 0)
            {
                WpfMessageBox.Show(
                    "No winners are selected, so the patch would be empty. Pick a winner for at least one conflict and try again.",
                    "Nothing to generate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (packToBsa && (string.IsNullOrWhiteSpace(bsarchPath) || !File.Exists(bsarchPath)))
            {
                WpfMessageBox.Show(
                    "BSA packing is enabled, but bsarch.exe was not found or the path is invalid. Provide a valid bsarch.exe path and try again.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

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
                            string ext = AudioFiles.GetExtensionOrDefault(item.Variant.FilePath, ".wav");
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

                string stagingRoot = Path.Combine(outputRoot, $"{PatchFolderName}_staging_{Guid.NewGuid():N}");

                bool completed = false;

                try
                {
                    if (Directory.Exists(stagingRoot))
                        TryDeleteDirectory(stagingRoot);

                    Directory.CreateDirectory(stagingRoot);

                    ConflictManager.GeneratePatchMod(stagingRoot, _winners);

                    if (!HasAnyAudioFiles(stagingRoot))
                    {
                        WpfMessageBox.Show(
                            "No audio files were written to the patch output. This usually means the selection is empty or the chosen sources are missing.",
                            "Nothing to generate",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    string? finalArchivePath = null;

                    if (packToBsa)
                    {
                        string bsaName = PatchFolderName + ".bsa";
                        string archivePathInStaging = Path.Combine(stagingRoot, bsaName);

                        string tempBsaDir = Path.Combine(Path.GetTempPath(), "SkyrimAudioSelector", "BsarchOutputs");
                        Directory.CreateDirectory(tempBsaDir);

                        string tempArchivePath = Path.Combine(tempBsaDir, $"{PatchFolderName}_{Guid.NewGuid():N}.bsa");
                        PatchOutputs.TryDeleteFile(tempArchivePath);

                        PackPatchWithBsarch(bsarchPath, stagingRoot, tempArchivePath);

                        PatchOutputs.TryDeleteFile(archivePathInStaging);
                        File.Move(tempArchivePath, archivePathInStaging);

                        PatchOutputs.DeleteAudioFiles(stagingRoot, keepFilePath: archivePathInStaging);

                        finalArchivePath = Path.Combine(patchRoot, bsaName);
                    }

                    ReplacePatchFolder(patchRoot, stagingRoot);

                    completed = true;

                    string message = !packToBsa
                        ? $"Patch mod generated as loose files under:\n{patchRoot}"
                        : $"Patch mod generated as a BSA under:\n{finalArchivePath}\n\nLoose audio files in this patch folder were deleted (BSA-only mode).";

                    WpfMessageBox.Show(message, "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    DoScan(showPopup: false);
                }
                finally
                {
                    if (!completed)
                        TryDeleteDirectory(stagingRoot);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error while generating patch mod / packing BSA:\n" + ex,
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static bool HasAnyAudioFiles(string root)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    if (AudioFiles.IsAudioExtension(Path.GetExtension(file)))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static void ReplacePatchFolder(string patchRoot, string stagingRoot)
        {
            if (string.IsNullOrWhiteSpace(patchRoot))
                throw new ArgumentException("patchRoot is empty.", nameof(patchRoot));
            if (string.IsNullOrWhiteSpace(stagingRoot))
                throw new ArgumentException("stagingRoot is empty.", nameof(stagingRoot));
            if (!Directory.Exists(stagingRoot))
                throw new DirectoryNotFoundException($"Staging folder does not exist: {stagingRoot}");

            if (!Directory.Exists(patchRoot))
            {
                Directory.Move(stagingRoot, patchRoot);
                return;
            }

            string backup = patchRoot + "_backup_" + Guid.NewGuid().ToString("N");
            bool movedOld = false;

            try
            {
                Directory.Move(patchRoot, backup);
                movedOld = true;

                Directory.Move(stagingRoot, patchRoot);

                TryDeleteDirectory(backup);
                return;
            }
            catch
            {
                if (movedOld && !Directory.Exists(patchRoot) && Directory.Exists(backup))
                {
                    try { Directory.Move(backup, patchRoot); } catch { }
                }
            }

            ClearPreviousPatchOutputs(patchRoot);
            CopyDirectoryRecursive(stagingRoot, patchRoot);
            TryDeleteDirectory(stagingRoot);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, dirPath);
                var dest = Path.Combine(targetDir, rel);
                Directory.CreateDirectory(dest);
            }

            foreach (var filePath in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, filePath);
                var dest = Path.Combine(targetDir, rel);

                string? destFolder = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(destFolder))
                    Directory.CreateDirectory(destFolder);

                File.Copy(filePath, dest, overwrite: true);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch { }
        }

        private static void PackPatchWithBsarch(string bsarchPath, string sourceDir, string archivePath)
        {
            var res = ProcessRunner.Run(bsarchPath, $"pack \"{sourceDir}\" \"{archivePath}\" -sse -z");

            if (res.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"bsarch.exe exited with code {res.ExitCode}.\n\nSTDOUT:\n{res.StdOut}\n\nSTDERR:\n{res.StdErr}");
            }
        }
    }
}
