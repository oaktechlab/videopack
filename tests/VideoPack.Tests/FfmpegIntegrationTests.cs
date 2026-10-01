using System.Diagnostics;
using VideoPack.FFmpeg;
using VideoPack.Models;
using VideoPack.Services;
using Xunit;

namespace VideoPack.Tests;

public sealed class FfmpegIntegrationTests
{
    [Fact]
    public async Task Exports_mp4_with_real_bumpers_watermark_and_audio_normalization()
    {
        if (Environment.GetEnvironmentVariable("VIDEOPACK_RUN_FFMPEG_INTEGRATION") != "1") return;

        var paths = new AppPaths();
        Assert.True(File.Exists(paths.FfmpegPath), "Prepara FFmpeg ejecutando build-portable.ps1 -PrepareOnly.");
        paths.EnsureDirectories();
        var sourcePath = Path.Combine(paths.TempDirectory, $"integration-source-{Guid.NewGuid():N}.mp4");
        var outputPath = Path.Combine(paths.TempDirectory, $"integration-output-{Guid.NewGuid():N}.mp4");
        try
        {
            await RunAsync(paths.FfmpegPath,
            [
                "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i",
                "color=c=0x52799A:s=640x360:r=25:d=2", "-an", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-t", "2", "-y", sourcePath
            ]);
            var presetService = new PresetService(paths);
            await presetService.LoadAsync();
            var source = await new FfprobeService(paths.FfprobePath).ProbeAsync(sourcePath);
            var logo = paths.ResolveAsset(Path.Combine("assets", "watermarks", presetService.Configuration.Logos[0].FileName));
            var request = new ExportRequest(source, OutputFormat.Landscape, FramingMode.Fit, 320, 180, 25,
                800, 128, "libx264", "aac", true, true, true, logo, WatermarkPosition.BottomRight,
                presetService.Configuration.WatermarkWidthRatio, presetService.Configuration.SafeMarginRatio, outputPath);

            var result = await new ExportService(paths, presetService).ExportAsync(request, null, CancellationToken.None);
            var output = await new FfprobeService(paths.FfprobePath).ProbeAsync(result.OutputPath);

            Assert.Equal(320, output.Width);
            Assert.Equal(180, output.Height);
            Assert.Equal("h264", output.VideoCodec);
            Assert.True(output.HasAudio);
            Assert.Equal("aac", output.AudioCodec);
            Assert.True(output.DurationSeconds > source.DurationSeconds);
            Assert.True(output.FileSizeBytes > 0);
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
    }

    private static async Task RunAsync(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }
}