using Mutagen.Bethesda;
using Mutagen.Bethesda.Archives;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfMessageBox = System.Windows.MessageBox;

namespace Skyrim_Audio_Selector
{
    public partial class MainWindow : Window
    {
        // ---------------- Playback ----------------

        private string ExtractBsaEntryToTemp(SoundVariant variant)
        {
            if (!variant.FromBsa)
                throw new InvalidOperationException("Variant is not from BSA/BA2.");

            string cacheKey = $"{variant.BsaPath}|{variant.FilePath}".ToLowerInvariant();

            lock (_bsaExtractCache)
            {
                if (_bsaExtractCache.TryGetValue(cacheKey, out var cached) && File.Exists(cached))
                    return cached;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "SkyrimAudioSelector", "BsaExtracts");
            Directory.CreateDirectory(tempRoot);

            string fileName = Path.GetFileName(variant.FilePath.Replace('/', Path.DirectorySeparatorChar));
            string outPath = Path.Combine(tempRoot, $"{Guid.NewGuid():N}_{fileName}");

            var reader = Archive.CreateReader(GameRelease.SkyrimSE, variant.BsaPath);
            string wantedPath = AudioPaths.NormalizeArchivePath(variant.FilePath);

            var entry = reader.Files.FirstOrDefault(f =>
                string.Equals(
                    AudioPaths.NormalizeArchivePath(f.Path),
                    wantedPath,
                    StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                throw new FileNotFoundException($"Entry {variant.FilePath} not found in {variant.BsaPath}.");

            File.WriteAllBytes(outPath, entry.GetBytes());

            lock (_bsaExtractCache)
            {
                _bsaExtractCache[cacheKey] = outPath;
            }

            return outPath;
        }

        private string TranscodeToWav(string sourcePath)
        {
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("Source file not found.", sourcePath);

            lock (_transcodedCache)
            {
                if (_transcodedCache.TryGetValue(sourcePath, out var cached) && File.Exists(cached))
                    return cached;
            }

            string tempRoot = Path.Combine(Path.GetTempPath(), "SkyrimAudioSelector", "Transcoded");
            Directory.CreateDirectory(tempRoot);

            string outFile = Path.Combine(
                tempRoot,
                Path.GetFileNameWithoutExtension(sourcePath) + "_" + Guid.NewGuid().ToString("N") + ".wav");

            var res = ProcessRunner.Run(FfmpegPath, $"-y -i \"{sourcePath}\" \"{outFile}\"");

            if (res.ExitCode != 0 || !File.Exists(outFile))
            {
                throw new InvalidOperationException(
                    $"ffmpeg failed with exit code {res.ExitCode}.\n\nSTDOUT:\n{res.StdOut}\n\nSTDERR:\n{res.StdErr}");
            }

            lock (_transcodedCache)
            {
                _transcodedCache[sourcePath] = outFile;
            }

            return outFile;
        }

        private void StopPlaybackTimer()
        {
            if (_playbackTimer == null)
                return;

            try { _playbackTimer.Stop(); } catch { }
            _playbackTimer = null;
        }

        private void StartPlaybackTimer(SoundVariant variant, double seconds)
        {
            StopPlaybackTimer();

            _playbackTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(seconds)
            };

            _playbackTimer.Tick += (s, e) =>
            {
                _playbackTimer.Stop();
                _playbackTimer = null;

                if (_currentlyPlayingVariant == variant)
                    StopPlayback();
            };

            _playbackTimer.Start();
        }

        private void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SoundVariant variant)
                return;

            if (_currentlyPlayingVariant == variant && variant.IsPlaying)
            {
                StopPlayback();
                return;
            }

            PlaySound(variant);
        }

        private void PlaySound(SoundVariant variant)
        {
            StopPlayback();

            try
            {
                string sourcePath = variant.FromBsa ? ExtractBsaEntryToTemp(variant) : variant.FilePath;

                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("Source file not found.", sourcePath);

                string wavToPlay = string.Equals(Path.GetExtension(sourcePath), ".wav", StringComparison.OrdinalIgnoreCase)
                    ? sourcePath
                    : TranscodeToWav(sourcePath);

                if (!File.Exists(wavToPlay))
                    throw new FileNotFoundException("WAV file to play not found.", wavToPlay);

                _soundPlayer = new SoundPlayer(wavToPlay);
                _soundPlayer.Load();
                _soundPlayer.Play();

                _currentlyPlayingVariant = variant;
                variant.IsPlaying = true;

                double seconds = (variant.DurationSeconds.HasValue && variant.DurationSeconds.Value > 0.01)
                    ? variant.DurationSeconds.Value
                    : 5.0;

                if (seconds < 1.0)
                    seconds = 1.0;

                StartPlaybackTimer(variant, seconds);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(
                    "Error starting playback:\n" + ex,
                    "Playback error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void StopPlayback()
        {
            StopPlaybackTimer();

            if (_soundPlayer != null)
            {
                try { _soundPlayer.Stop(); } catch { }
                _soundPlayer.Dispose();
                _soundPlayer = null;
            }

            if (_currentlyPlayingVariant != null)
            {
                _currentlyPlayingVariant.IsPlaying = false;
                _currentlyPlayingVariant = null;
            }
        }

        private void VariantsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }
}
