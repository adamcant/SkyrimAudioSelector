using System;
using System.Diagnostics;

namespace Skyrim_Audio_Selector
{
    internal readonly record struct ProcessRunResult(int ExitCode, string StdOut, string StdErr)
    {
        internal string CombinedOutput => (StdOut ?? string.Empty) + "\n" + (StdErr ?? string.Empty);
    }

    internal static class ProcessRunner
    {
        internal static ProcessRunResult Run(string exePath, string arguments)
        {
            if (string.IsNullOrWhiteSpace(exePath))
                throw new ArgumentException("Executable path is empty.", nameof(exePath));

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null)
                throw new InvalidOperationException($"Failed to start process: {exePath}");

            // Read both streams concurrently to avoid deadlocks.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();

            proc.WaitForExit();

            string stdout = stdoutTask.GetAwaiter().GetResult();
            string stderr = stderrTask.GetAwaiter().GetResult();

            return new ProcessRunResult(proc.ExitCode, stdout, stderr);
        }
    }
}
