using VideoPack.FFmpeg;
using VideoPack.Models;
using VideoPack.Services;
using Xunit;

namespace VideoPack.Tests;

public sealed class VideoRulesTests
{
    [Theory]
    [InlineData(3840, OutputFormat.Landscape, 3840, 2160)]
    [InlineData(1920, OutputFormat.Portrait, 1080, 1920)]
    [InlineData(1280, OutputFormat.Square, 1280, 1280)]
    public void Resolution_matches_output_aspect(int longEdge, OutputFormat format, int width, int height)
    {
        Assert.Equal((width, height), PresetService.GetResolution(longEdge, format));
    }

    [Theory]
    [InlineData(1920, 1080, OutputFormat.Landscape)]
    [InlineData(1080, 1920, OutputFormat.Portrait)]
    [InlineData(1080, 1080, OutputFormat.Square)]
    public void Detects_format_from_dimensions(int width, int height, OutputFormat expected)
    {
        Assert.Equal(expected, VideoRules.DetectFormat(width, height));
    }

    [Theory]
    [InlineData(OutputFormat.Landscape, true)]
    [InlineData(OutputFormat.Portrait, false)]
    [InlineData(OutputFormat.Square, false)]
    public void Intro_is_available_only_for_landscape(OutputFormat format, bool expected)
    {
        Assert.Equal(expected, VideoRules.IsIntroAvailable(format));
    }

    [Theory]
    [InlineData(OutputFormat.Landscape, "out-landscape.mp4")]
    [InlineData(OutputFormat.Portrait, "out-portrait.mp4")]
    [InlineData(OutputFormat.Square, "out-portrait.mp4")]
    public void Selects_matching_outro(OutputFormat format, string expected)
    {
        var config = new AppConfiguration { OutroLandscape = "out-landscape.mp4", OutroPortrait = "out-portrait.mp4" };
        Assert.Equal(expected, VideoRules.GetOutroAsset(format, config));
    }

    [Fact]
    public void Manual_adjustment_selects_custom_preset()
    {
        Assert.Equal(QualityPreset.Custom, VideoRules.AfterManualChange());
    }

    [Theory]
    [InlineData(WatermarkPosition.TopLeft, "trunc(min(1920\\,1080)*0.035)", "trunc(min(1920\\,1080)*0.035)")]
    [InlineData(WatermarkPosition.TopRight, "1920-overlay_w-trunc(min(1920\\,1080)*0.035)", "trunc(min(1920\\,1080)*0.035)")]
    [InlineData(WatermarkPosition.BottomLeft, "trunc(min(1920\\,1080)*0.035)", "1080-overlay_h-trunc(min(1920\\,1080)*0.035)")]
    [InlineData(WatermarkPosition.BottomRight, "1920-overlay_w-trunc(min(1920\\,1080)*0.035)", "1080-overlay_h-trunc(min(1920\\,1080)*0.035)")]
    public void Watermark_positions_respect_safe_margin(WatermarkPosition position, string expectedX, string expectedY)
    {
        Assert.Equal((expectedX, expectedY), VideoRules.GetWatermarkCoordinates(1920, 1080, 0.035, position));
    }

    [Fact]
    public void Estimated_size_uses_video_and_audio_bitrates()
    {
        Assert.Equal(38_940_000, PresetService.EstimateBytes(60, 5000, 192));
    }

    [Fact]
    public void Builds_safe_filter_graph_and_preserves_paths_as_arguments()
    {
        var builder = new VideoCommandBuilder();
        var request = new ExportRequest(
            new VideoMetadata("C:\\Video Packs\\source.mp4", 1920, 1080, 25, 60, "h264", 5_000_000, false, "Sin audio", 100_000),
            OutputFormat.Portrait, FramingMode.Crop, 1080, 1920, 25, 5000, 192, "libx264", "aac",
            false, true, true, "C:\\Video Packs\\logos\\logo.png", WatermarkPosition.BottomRight, 0.12, 0.035,
            "C:\\Output Folder\\final.mp4");
        var arguments = builder.Build(request,
        [
            new("C:\\Video Packs\\source.mp4", 60, false, true),
            new("C:\\Video Packs\\outro.mp4", 3, true)
        ], "C:\\Video Packs\\temp\\partial.mp4");
        var filter = arguments[Array.IndexOf(arguments.ToArray(), "-filter_complex") + 1];

        Assert.Contains("C:\\Video Packs\\source.mp4", arguments);
        Assert.Contains("anullsrc=channel_layout=stereo", filter);
        Assert.Contains("force_original_aspect_ratio=increase,crop=1080:1920", filter);
        Assert.Contains("concat=n=2:v=1:a=1", filter);
        Assert.Contains("[v0src][wm]overlay=x=1080-overlay_w-", filter);
        Assert.DoesNotContain("[basev][wm]", filter);
        Assert.Contains("-b:v", arguments);
    }

    [Fact]
    public void Watermark_is_burned_only_into_the_user_video()
    {
        var builder = new VideoCommandBuilder();
        var request = new ExportRequest(
            new VideoMetadata("C:\\clips\\user.mp4", 1920, 1080, 25, 30, "h264", 5_000_000, true, "aac", 100_000),
            OutputFormat.Landscape, FramingMode.Fit, 1920, 1080, 25, 5000, 192, "libx264", "aac",
            true, true, true, "C:\\logos\\logo.png", WatermarkPosition.TopRight, 0.12, 0.035,
            "C:\\out\\final.mp4");
        var arguments = builder.Build(request,
        [
            new("C:\\bumpers\\in.mp4", 4, true),
            new("C:\\clips\\user.mp4", 30, true, true),
            new("C:\\bumpers\\out.mp4", 3, true)
        ], "C:\\temp\\partial.mp4");
        var filter = arguments[Array.IndexOf(arguments.ToArray(), "-filter_complex") + 1];

        Assert.Contains("[v1src][wm]overlay=", filter);
        Assert.DoesNotContain("[v0src]", filter);
        Assert.DoesNotContain("[v2src]", filter);
        Assert.Contains("[v0][a0][v1][a1][v2][a2]concat=n=3", filter);
    }
}