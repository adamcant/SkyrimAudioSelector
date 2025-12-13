using System;

namespace Skyrim_Audio_Selector
{
    public class ModInfo
    {
        public string Name { get; }
        public string DirectoryPath { get; }
        public bool Enabled { get; }
        public int Priority { get; }
        public bool IsPatch { get; }

        public ModInfo(string name, string directoryPath, bool enabled, int priority, bool isPatch = false)
        {
            Name = name;
            DirectoryPath = directoryPath;
            Enabled = enabled;
            Priority = priority;
            IsPatch = isPatch;
        }
    }
}