using System;
using System.ComponentModel;
using System.IO;

namespace Skyrim_Audio_Selector
{
    public class SoundVariant : INotifyPropertyChanged
    {
        public ModInfo Mod { get; }

        public string FilePath { get; }
        public string RelativeKey { get; }

        public bool FromBsa { get; }
        public string BsaPath { get; }

        private bool _isWinner;
        public bool IsWinner
        {
            get => _isWinner;
            set
            {
                if (_isWinner != value)
                {
                    _isWinner = value;
                    OnPropertyChanged(nameof(IsWinner));
                    OnPropertyChanged(nameof(WinnerMarker));
                }
            }
        }

        public string WinnerMarker => IsWinner ? "★" : "";

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                    OnPropertyChanged(nameof(PlayButtonText));
                }
            }
        }

        public string PlayButtonText => IsPlaying ? "Stop" : "Play";

        private double? _durationSeconds;
        public double? DurationSeconds
        {
            get => _durationSeconds;
            set
            {
                if (_durationSeconds != value)
                {
                    _durationSeconds = value;
                    OnPropertyChanged(nameof(DurationSeconds));
                    OnPropertyChanged(nameof(DurationDisplay));
                }
            }
        }

        public string DurationDisplay
        {
            get
            {
                if (!_durationSeconds.HasValue)
                    return string.Empty;

                int roundedSeconds = (int)Math.Round(
                    _durationSeconds.Value,
                    MidpointRounding.AwayFromZero);

                if (roundedSeconds < 1)
                    roundedSeconds = 1;

                var ts = TimeSpan.FromSeconds(roundedSeconds);

                if (ts.TotalHours >= 1)
                    return $"{(int)ts.TotalHours}:{ts.Minutes:00}:{ts.Seconds:00}";

                return $"{ts.Minutes}:{ts.Seconds:00}";
            }
        }

        public string ModName => Mod.Name;

        public string DisplayPriority =>
            string.Equals(Mod.Name, ConflictManager.BaseGameModName, StringComparison.OrdinalIgnoreCase)
                ? "Base"
                : (Mod.IsPatch ? "Patch" : Mod.Priority.ToString());

        public int ModPriority => Mod.Priority;

        public string SourceDescription =>
            FromBsa ? $"BSA: {Path.GetFileName(BsaPath)}" : "Loose file";

        public SoundVariant(ModInfo mod, string filePath, string relativeKey)
            : this(mod, filePath, relativeKey, fromBsa: false, bsaPath: null)
        { }

        public SoundVariant(ModInfo mod, string filePath, string relativeKey, bool fromBsa, string bsaPath)
        {
            Mod = mod;
            FilePath = filePath;
            RelativeKey = relativeKey;
            FromBsa = fromBsa;
            BsaPath = bsaPath;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}