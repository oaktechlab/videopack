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
        Assert.DoesNotContain("trim=start=", filter);
    }

    [Fact]
    public void Trim_is_applied_before_scale_watermark_and_concat()
    {
        var builder = new VideoCommandBuilder();
        var request = new ExportRequest(
            new VideoMetadata("C:\\clips\\user.mp4", 1920, 1080, 25, 120, "h264", 5_000_000, true, "aac", 100_000),
            OutputFormat.Landscape, FramingMode.Fit, 1920, 1080, 25, 5000, 192, "libx264", "aac",
            true, true, true, "C:\\logos\\logo.png", WatermarkPosition.TopRight, 0.12, 0.035,
            "C:\\out\\final.mp4", 3.5, 90);
        var arguments = builder.Build(request,
        [
            new("C:\\bumpers\\in.mp4", 4, true),
            new("C:\\clips\\user.mp4", 86.5, true, true, 3.5, 90),
            new("C:\\bumpers\\out.mp4", 3, false)
        ], "C:\\temp\\partial.mp4");
        var filter = arguments[Array.IndexOf(arguments.ToArray(), "-filter_complex") + 1];
        var mainInput = Array.IndexOf(arguments.ToArray(), "C:\\clips\\user.mp4");

        Assert.Equal("+genpts", arguments[mainInput - 2]);
        Assert.Contains("setpts=PTS-STARTPTS,trim=start=3.5:end=90,setpts=PTS-STARTPTS,scale=", filter);
        Assert.Contains("atrim=start=3.5:end=90", filter);
        Assert.Contains("atrim=duration=86.5", filter);
        Assert.Contains("[v1src][wm]overlay=", filter);
        Assert.DoesNotContain("[0:v:0]setpts=PTS-STARTPTS,trim=", filter);
        Assert.Contains("anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration=3", filter);
        Assert.Equal("C:\\temp\\partial.mp4", arguments[^1]);
        Assert.NotEqual("C:\\clips\\user.mp4", arguments[^1]);
    }

    [Fact]
    public void Default_text_watermark_is_two_lines_within_the_limit()
    {
        var text = VideoRules.NormalizeTextWatermark(VideoRules.DefaultTextWatermark);
        var lines = text.Split('\n');

        Assert.Equal(VideoRules.DefaultTextWatermark, text);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.InRange(line.Length, 1, VideoRules.TextWatermarkMaxLineLength));
    }

    [Theory]
    [InlineData("uno\r\ndos\r\ntres\r\ncuatro", "uno\ndos\ntres")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", "abcdefghijklmnopqrstuvwxyz0123")]
    public void Text_watermark_drops_extra_lines_and_characters(string input, string expected)
    {
        Assert.Equal(expected, VideoRules.NormalizeTextWatermark(input));
        Assert.Equal(35, VideoRules.TextWatermarkFontSize(1920, 1080));
        Assert.Equal(35, VideoRules.TextWatermarkFontSize(1080, 1920));
    }

    [Fact]
    public void Text_watermark_is_drawn_only_on_the_user_video()
    {
        var builder = new VideoCommandBuilder();
        var request = new ExportRequest(
            new VideoMetadata("C:\\clips\\user.mp4", 1920, 1080, 25, 30, "h264", 5_000_000, true, "aac", 100_000),
            OutputFormat.Landscape, FramingMode.Fit, 1920, 1080, 25, 5000, 192, "libx264", "aac",
            true, true, false, null, WatermarkPosition.BottomRight, 0.12, 0.035,
            "C:\\out\\final.mp4", 0, null,
            true, VideoRules.DefaultTextWatermark, WatermarkPosition.BottomLeft,
            "C:\\Video Packs\\fonts\\OpenSans_SemiCondensed-Light.ttf",
            "C:\\Video Packs\\temp\\mosca.txt");
        var arguments = builder.Build(request,
        [
            new("C:\\bumpers\\in.mp4", 4, true),
            new("C:\\clips\\user.mp4", 30, true, true),
            new("C:\\bumpers\\out.mp4", 3, true)
        ], "C:\\temp\\partial.mp4");
        var filter = arguments[Array.IndexOf(arguments.ToArray(), "-filter_complex") + 1];

        Assert.Contains("[v1src]drawtext=", filter);
        Assert.Contains("fontfile='C\\:/Video Packs/fonts/OpenSans_SemiCondensed-Light.ttf'", filter);
        Assert.Contains("textfile='C\\:/Video Packs/temp/mosca.txt'", filter);
        Assert.Contains("text_align=left", filter);
        Assert.Contains("1080-text_h-", filter);
        Assert.DoesNotContain("[v0src]", filter);
        Assert.DoesNotContain("[v2src]", filter);
        Assert.DoesNotContain("overlay=", filter);
        Assert.Equal(3, arguments.Count(argument => argument == "-i"));
    }

    [Fact]
    public void Logo_and_text_watermarks_share_the_user_video_without_sharing_a_corner()
    {
        var builder = new VideoCommandBuilder();
        var request = new ExportRequest(
            new VideoMetadata("C:\\clips\\user.mp4", 1920, 1080, 25, 30, "h264", 5_000_000, true, "aac", 100_000),
            OutputFormat.Landscape, FramingMode.Fit, 1920, 1080, 25, 5000, 192, "libx264", "aac",
            true, true, true, "C:\\logos\\logo.png", WatermarkPosition.BottomRight, 0.12, 0.035,
            "C:\\out\\final.mp4", 0, null,
            true, "Proyecto", WatermarkPosition.TopLeft,
            "C:\\fonts\\OpenSans_SemiCondensed-Light.ttf", "C:\\temp\\mosca.txt");
        var arguments = builder.Build(request,
        [
            new("C:\\bumpers\\in.mp4", 4, true),
            new("C:\\clips\\user.mp4", 30, true, true),
            new("C:\\bumpers\\out.mp4", 3, true)
        ], "C:\\temp\\partial.mp4");
        var filter = arguments[Array.IndexOf(arguments.ToArray(), "-filter_complex") + 1];

        Assert.Contains("[v1src][wm]overlay=", filter);
        Assert.Contains("[v1logo]drawtext=", filter);
        Assert.Contains("text_align=left", filter);
        Assert.DoesNotContain("[v0src]", filter);
        Assert.DoesNotContain("[v2src]", filter);
        Assert.Contains("[v0][a0][v1][a1][v2][a2]concat=n=3", filter);
    }
}