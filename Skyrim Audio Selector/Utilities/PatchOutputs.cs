using System;
using System.IO;
using System.Linq;

namespace Skyrim_Audio_Selector
{
    internal static class PatchOutputs
    {
        internal static void DeleteAudioFiles(string root, string? keepFilePath = null)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (!string.IsNullOrWhiteSpace(keepFilePath) &&
                    string.Equals(file, keepFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (AudioFiles.IsAudioExtension(Path.GetExtension(file)))
                {
                    try { File.Delete(file); } catch { }
                }
            }

            DeleteEmptyDirectories(root);
        }

        internal static void DeleteEmptyDirectories(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;

            foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                                         .OrderByDescending(d => d.Length))
            {
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir, false);
                }
                catch { }
            }
        }

        internal static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
