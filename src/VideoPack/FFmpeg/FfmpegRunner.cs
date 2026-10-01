using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace VideoPack.FFmpeg;

public sealed record FfmpegRunResult(int ExitCode, string StandardError, string CommandLine);

public sealed class FfmpegRunner(string executablePath)
{
    public async Task<FfmpegRunResult> RunAsync(IReadOnlyList<string> arguments, double durationSeconds,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        var command = Quote(executablePath) + " " + string.Join(' ', arguments.Select(Quote));
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var output = new StringBuilder();
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                output.AppendLine(line);
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
                    && long.TryParse(line.AsSpan("out_time_us=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var microseconds)
                    && durationSeconds > 0)
                    progress?.Report(Math.Clamp(microseconds / (durationSeconds * 1_000_000d) * 100d, 0, 99));
                else if (line == "progress=end") progress?.Report(100);
            }
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            await process.WaitForExitAsync(CancellationToken.None);
            throw;
        }
        var error = await errorTask;
        return new FfmpegRunResult(process.ExitCode, error, command + Environment.NewLine + output);
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";
}