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

            // Avoid weird states (empty patch generation, BSA packing without bsarch, etc.)
            if (_winners.Count == 0)
            {
                WpfMessageBox.Show(
                    "No winners are selected, so the patch would be empty." +
                    "Pick a winner for at least one conflict and try again.",
                    "Nothing to generate",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (packToBsa && (string.IsNullOrWhiteSpace(bsarchPath) || !File.Exists(bsarchPath)))
            {
                WpfMessageBox.Show(
                    "BSA packing is enabled, but bsarch.exe was not found or the path is invalid." +
                    "Provide a valid bsarch.exe path and try again.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            try
            {
                // Safe mode warning (unchanged behavior)
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

                // Build into a staging folder first. This prevents crashes when regenerating
                // from an existing exported patch (sources live in the current patch folder).
                string stagingRoot = Path.Combine(
                    outputRoot,
                    $"{PatchFolderName}_staging_{Guid.NewGuid():N}");

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
                            "No audio files were written to the patch output." +
                            "This usually means the selection is empty or the chosen sources are missing.",
                            "Nothing to generate",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        return;
                    }

                    string finalArchivePath = null;

                    if (packToBsa)
                    {
                        string bsaName = PatchFolderName + ".bsa";
                        string archivePathInStaging = Path.Combine(stagingRoot, bsaName);

                        // IMPORTANT: output the BSA OUTSIDE the folder being packed, to avoid
                        // bsarch including the output archive into itself or tripping over file locks.
                        string tempBsaDir = Path.Combine(Path.GetTempPath(), "SkyrimAudioSelector", "BsarchOutputs");
                        Directory.CreateDirectory(tempBsaDir);

                        string tempArchivePath = Path.Combine(tempBsaDir, $"{PatchFolderName}_{Guid.NewGuid():N}.bsa");
                        if (File.Exists(tempArchivePath))
                        {
                            try { File.Delete(tempArchivePath); } catch { }
                        }

                        PackPatchWithBsarch(bsarchPath, stagingRoot, tempArchivePath);

                        if (File.Exists(archivePathInStaging))
                        {
                            try { File.Delete(archivePathInStaging); } catch { }
                        }

                        File.Move(tempArchivePath, archivePathInStaging);

                        DeleteLooseAudioFilesFromPatch(stagingRoot, archivePathInStaging);

                        finalArchivePath = Path.Combine(patchRoot, bsaName);
                    }

                    ReplacePatchFolder(patchRoot, stagingRoot);

                    completed = true;

                    string message = !packToBsa
                        ? $"Patch mod generated as loose files under:{patchRoot}": $"Patch mod generated as BSA under: {finalArchivePath} Loose audio files in this patch folder were deleted (BSA-only mode).";

                    WpfMessageBox.Show(message, "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    DoScan(showPopup: false);
                }
                finally
                {
                    // If we didn't successfully move/copy staging into the final folder, clean it up.
                    if (!completed)
                        TryDeleteDirectory(stagingRoot);
                }
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error while generating patch mod / packing BSA:" + ex,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static bool HasAnyAudioFiles(string root)
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".wav" || ext == ".xwm" || ext == ".mp3" || ext == ".ogg")
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

            // Fast path: if patch folder doesn't exist yet, just move staging into place.
            if (!Directory.Exists(patchRoot))
            {
                Directory.Move(stagingRoot, patchRoot);
                return;
            }

            // Attempt atomic-ish replace by renaming the old folder aside, then moving staging in.
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
                // If we moved the old folder aside but failed to move staging in, attempt rollback.
                if (movedOld && !Directory.Exists(patchRoot) && Directory.Exists(backup))
                {
                    try { Directory.Move(backup, patchRoot); } catch { }
                }
            }

            // Fallback: clear previous patch outputs and copy staging into the existing patch folder.
            // This is less "atomic", but works when the folder can't be renamed (eg. locked by something).
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

                string destFolder = Path.GetDirectoryName(dest);
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
            catch
            {
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

            // Read both streams concurrently to avoid deadlocks.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            proc.WaitForExit();

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"bsarch.exe exited with code {proc.ExitCode}.STDOUT:{stdout}STDERR:{stderr}");
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
    }
}
