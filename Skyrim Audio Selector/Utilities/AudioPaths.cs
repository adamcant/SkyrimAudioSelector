using System;
using System.IO;

namespace Skyrim_Audio_Selector
{
    internal static class AudioPaths
    {
        internal static string NormalizeArchivePath(string p)
            => (p ?? string.Empty).Replace('\\', '/').TrimStart('/');

        internal static string? ComputeAudioKeyFromAbsoluteFile(string modRoot, string file)
        {
            if (string.IsNullOrWhiteSpace(modRoot) || string.IsNullOrWhiteSpace(file))
                return null;

            string rel;
            try
            {
                rel = Path.GetRelativePath(modRoot, file);
            }
            catch
            {
                return null;
            }

            return ComputeAudioKeyFromRelativePath(rel);
        }

        internal static string? ComputeAudioKeyFromRelativePath(string relPath)
        {
            if (string.IsNullOrWhiteSpace(relPath))
                return null;

            string rel = relPath.Replace('\\', '/');
            string lower = rel.ToLowerInvariant();

            int soundIdx = FindSegmentStart(lower, "sound");
            int musicIdx = FindSegmentStart(lower, "music");

            int idx;
            if (soundIdx < 0) idx = musicIdx;
            else if (musicIdx < 0) idx = soundIdx;
            else idx = Math.Min(soundIdx, musicIdx);

            if (idx < 0)
                return null;

            string path = lower.Substring(idx);

            int lastSlash = path.LastIndexOf('/');
            int lastDot = path.LastIndexOf('.');
            if (lastDot > lastSlash)
                path = path.Substring(0, lastDot);

            return path;
        }

        private static int FindSegmentStart(string path, string segment)
        {

            string startNeedle = segment + "/";
            if (path.StartsWith(startNeedle, StringComparison.Ordinal))
                return 0;

            string midNeedle = "/" + segment + "/";
            int idx = path.IndexOf(midNeedle, StringComparison.Ordinal);
            if (idx >= 0)
                return idx + 1;

            return -1;
        }
    }
}
