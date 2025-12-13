using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Skyrim_Audio_Selector
{
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

            string[] dirs;
            try
            {
                dirs = Directory.GetDirectories(modsRoot);
            }
            catch
            {
                return mods;
            }

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

                // Loose files
                if (!(mo2Vfs && isBase))
                {
                    IEnumerable<string> files;
                    try
                    {
                        files = Directory.EnumerateFiles(mod.DirectoryPath, "*.*", SearchOption.AllDirectories);
                    }
                    catch
                    {
                        files = Enumerable.Empty<string>();
                    }

                    foreach (var file in files)
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

                // Archives
                IEnumerable<string> archives;
                try
                {
                    archives = Directory.EnumerateFiles(mod.DirectoryPath, "*.bsa", SearchOption.TopDirectoryOnly)
                        .Concat(Directory.EnumerateFiles(mod.DirectoryPath, "*.ba2", SearchOption.TopDirectoryOnly));
                }
                catch
                {
                    archives = Enumerable.Empty<string>();
                }

                if (mo2Vfs && isBase)
                    archives = archives.Where(p => LooksLikeVanillaArchiveName(Path.GetFileName(p)));

                foreach (var bsaPath in archives)
                {
                    try
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
                    catch
                    {
                        // Ignore broken/unreadable archives instead of crashing the whole scan.
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

                if (winner == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(winner.FilePath))
                    continue;

                string ext = Path.GetExtension(winner.FilePath);
                if (string.IsNullOrEmpty(ext))
                    ext = ".wav";

                string relativeWithExt = key + ext;

                string destPath = Path.Combine(
                    patchRootPath,
                    relativeWithExt.Replace('/', Path.DirectorySeparatorChar));

                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                if (!winner.FromBsa)
                {
                    // Avoid "source and destination are the same file"
                    // when a user regenerates a patch and the winner points to the current patch output.
                    if (string.Equals(Path.GetFullPath(winner.FilePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                        continue;

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