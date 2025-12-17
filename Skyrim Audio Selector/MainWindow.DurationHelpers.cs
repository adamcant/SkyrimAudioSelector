using System.Globalization;
using System.IO;
using System.Windows;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        // ---------------- Duration helpers ----------------

        private void EnsureDurationsForVariants(IEnumerable<SoundVariant> variants)
        {
            if (variants == null) return;

            var snapshot = variants
                .Where(v => v != null && !v.DurationSeconds.HasValue)
                .ToArray();

            if (snapshot.Length == 0)
                return;

            Task.Run(() =>
            {
                foreach (var v in snapshot)
                    ComputeDurationForVariantIfNeeded(v, CancellationToken.None);
            });
        }

        private void StartGlobalDurationComputation()
        {
            if (_conflicts == null || _conflicts.Count == 0)
                return;

            _durationCts?.Cancel();
            _durationCts = new CancellationTokenSource();
            var token = _durationCts.Token;

            var allVariants = _conflicts.Values.SelectMany(list => list).ToList();

            Task.Run(() =>
            {
                foreach (var v in allVariants)
                {
                    if (token.IsCancellationRequested)
                        break;

                    ComputeDurationForVariantIfNeeded(v, token);
                }
            }, token);
        }

        private void ComputeDurationForVariantIfNeeded(SoundVariant variant, CancellationToken token)
        {
            if (variant == null || variant.DurationSeconds.HasValue || token.IsCancellationRequested)
                return;

            try
            {
                string sourcePath = variant.FromBsa ? ExtractBsaEntryToTemp(variant) : variant.FilePath;

                double? dur = GetAudioDurationSeconds(sourcePath);
                if (!dur.HasValue || token.IsCancellationRequested)
                    return;

                Dispatcher.Invoke(() =>
                {
                    if (!token.IsCancellationRequested)
                        variant.DurationSeconds = dur.Value;
                });
            }
            catch
            {
            }
        }

        private double? GetAudioDurationSeconds(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return null;

            try
            {
                var res = ProcessRunner.Run(FfmpegPath, $"-i \"{sourcePath}\"");

                string text = (res.StdErr ?? string.Empty) + "\n" + (res.StdOut ?? string.Empty);

                var match = DurationRegex.Match(text);
                if (!match.Success)
                    return null;

                int h = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
                int m = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
                double s = double.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);

                double totalSeconds = h * 3600 + m * 60 + s;
                if (totalSeconds <= 0.01)
                    return null;

                return totalSeconds;
            }
            catch
            {
                return null;
            }
        }
    }
}
