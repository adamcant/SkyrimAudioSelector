using System;
using System.Collections.Generic;
using System.IO;

namespace Skyrim_Audio_Selector
{
    internal static class AudioFiles
    {
        internal static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".xwm",
            ".wav",
            ".mp3",
            ".ogg",
        };

        internal static bool IsAudioExtension(string? ext)
        {
            if (string.IsNullOrWhiteSpace(ext)) return false;
            return Extensions.Contains(ext);
        }

        internal static bool IsAudioFile(string path)
            => IsAudioExtension(Path.GetExtension(path));

        internal static string GetExtensionOrDefault(string path, string defaultExt)
        {
            string ext = Path.GetExtension(path);
            return string.IsNullOrWhiteSpace(ext) ? defaultExt : ext;
        }
    }
}
