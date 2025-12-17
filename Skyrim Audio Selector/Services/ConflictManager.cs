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
            catch
            {
            }

            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MO2_PATH"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MO2_INSTANCE"));
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
            => new(
                name: BaseGameModName,
                directoryPath: skyrimDataPath,
                enabled: true,
                priority: int.MinValue);

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

                if (!(mo2Vfs && isBase))
                    ScanLooseFiles(mod, byKey);

                ScanArchives(mod, byKey, mo2Vfs && isBase);
            }

            var conflicts = new Dictionary<string, List<SoundVariant>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in byKey)
                if (kvp.Value.Count > 1) conflicts[kvp.Key] = kvp.Value;

            return conflicts;
        }

        private static void ScanLooseFiles(ModInfo mod, Dictionary<string, List<SoundVariant>> byKey)
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(mod.DirectoryPath, "*.*", SearchOption.AllDirectories);
            }
            catch
            {
                return;
            }

            foreach (var file in files)
            {
                if (!AudioFiles.IsAudioFile(file)) continue;

                var key = AudioPaths.ComputeAudioKeyFromAbsoluteFile(mod.DirectoryPath, file);
                if (key == null) continue;

                AddVariant(byKey, key, new SoundVariant(mod, file, key, fromBsa: false, bsaPath: null));
            }
        }

        private static void ScanArchives(ModInfo mod, Dictionary<string, List<SoundVariant>> byKey, bool vanillaOnly)
        {
            IEnumerable<string> archives;
            try
            {
                archives = Directory.EnumerateFiles(mod.DirectoryPath, "*.bsa", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.EnumerateFiles(mod.DirectoryPath, "*.ba2", SearchOption.TopDirectoryOnly));
            }
            catch
            {
                return;
            }

            if (vanillaOnly)
                archives = archives.Where(p => LooksLikeVanillaArchiveName(Path.GetFileName(p)));

            foreach (var bsaPath in archives)
            {
                try
                {
                    var reader = Archive.CreateReader(GameRelease.SkyrimSE, bsaPath);
                    foreach (var file in reader.Files)
                    {
                        string entryPath = file.Path;
                        if (!AudioFiles.IsAudioExtension(Path.GetExtension(entryPath)))
                            continue;

                        var key = AudioPaths.ComputeAudioKeyFromRelativePath(entryPath);
                        if (key == null) continue;

                        AddVariant(byKey, key, new SoundVariant(mod, entryPath, key, fromBsa: true, bsaPath: bsaPath));
                    }
                }
                catch
                {
                }
            }
        }

        private static void AddVariant(Dictionary<string, List<SoundVariant>> byKey, string key, SoundVariant variant)
        {
            if (!byKey.TryGetValue(key, out var list))
            {
                list = new List<SoundVariant>();
                byKey[key] = list;
            }

            list.Add(variant);
        }

        public static void GeneratePatchMod(string patchRootPath, Dictionary<string, SoundVariant> winners)
        {
            if (winners == null || winners.Count == 0)
                return;

            Directory.CreateDirectory(patchRootPath);

            // 1) Copy loose winners immediately.
            // 2) Group BSA winners so we only open each archive once.
            var bsaGroups = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in winners)
            {
                string key = kvp.Key;
                var winner = kvp.Value;

                if (winner == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(winner.FilePath))
                    continue;

                string ext = AudioFiles.GetExtensionOrDefault(winner.FilePath, ".wav");
                string relativeWithExt = key + ext;

                string destPath = Path.Combine(
                    patchRootPath,
                    relativeWithExt.Replace('/', Path.DirectorySeparatorChar));

                string destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                if (!winner.FromBsa)
                {
                    if (string.Equals(Path.GetFullPath(winner.FilePath), Path.GetFullPath(destPath), StringComparison.OrdinalIgnoreCase))
                        continue;

                    File.Copy(winner.FilePath, destPath, overwrite: true);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(winner.BsaPath))
                    throw new InvalidOperationException($"Variant for {key} is marked as FromBsa, but BsaPath is empty.");

                string wantedPath = AudioPaths.NormalizeArchivePath(winner.FilePath);

                if (!bsaGroups.TryGetValue(winner.BsaPath, out var map))
                {
                    map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    bsaGroups[winner.BsaPath] = map;
                }

                map[wantedPath] = destPath;
            }

            foreach (var group in bsaGroups)
            {
                string bsaPath = group.Key;
                var wantedToDest = group.Value;

                var reader = Archive.CreateReader(GameRelease.SkyrimSE, bsaPath);

                foreach (var file in reader.Files)
                {
                    if (wantedToDest.Count == 0)
                        break;

                    string normalized = AudioPaths.NormalizeArchivePath(file.Path);
                    if (!wantedToDest.TryGetValue(normalized, out var destPath))
                        continue;

                    File.WriteAllBytes(destPath, file.GetBytes());
                    wantedToDest.Remove(normalized);
                }

                if (wantedToDest.Count > 0)
                {
                    var missing = wantedToDest.Keys.First();
                    throw new FileNotFoundException($"Entry {missing} not found in {bsaPath}.");
                }
            }
        }
    }
}