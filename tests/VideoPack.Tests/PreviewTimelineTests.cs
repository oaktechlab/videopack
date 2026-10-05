using VideoPack.Models;
using VideoPack.Services;
using VideoPack.ViewModels;
using Xunit;

namespace VideoPack.Tests;

public sealed class PreviewTimelineTests
{
    [Fact]
    public void Full_video_without_bumpers_is_a_single_segment()
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", 0, 120, false, null, 5, false, null, 4);

        var segment = Assert.Single(timeline.Segments);
        Assert.Equal(PreviewSegmentKind.Main, segment.Kind);
        Assert.Equal(0, segment.SourceStartSeconds);
        Assert.Equal(120, segment.DurationSeconds);
        Assert.Equal(120, timeline.DurationSeconds);
    }

    [Fact]
    public void Intro_main_and_outro_share_one_timeline()
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", 0, 82, true, "intro.mp4", 5, true, "outro.mp4", 5);

        Assert.Equal(92, timeline.DurationSeconds);
        Assert.Equal(PreviewSegmentKind.Intro, timeline.SegmentAt(0)!.Kind);
        Assert.Equal(PreviewSegmentKind.Main, timeline.SegmentAt(5)!.Kind);
        Assert.Equal(0, timeline.SegmentAt(5)!.ToSource(5));
        Assert.Equal(PreviewSegmentKind.Outro, timeline.SegmentAt(87)!.Kind);
        Assert.Equal(PreviewSegmentKind.Outro, timeline.SegmentAt(91.9)!.Kind);
    }

    [Fact]
    public void Vertical_and_square_timelines_omit_the_intro()
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", 0, 30, false, "intro.mp4", 5, true, "outro-portrait.mp4", 4);

        Assert.Equal(2, timeline.Segments.Count);
        Assert.Equal(PreviewSegmentKind.Main, timeline.Segments[0].Kind);
        Assert.Equal(PreviewSegmentKind.Outro, timeline.Segments[1].Kind);
        Assert.Equal("outro-portrait.mp4", timeline.Segments[1].Source);
        Assert.Equal(34, timeline.DurationSeconds);
    }

    [Theory]
    [InlineData(8, 120, 112)]
    [InlineData(0, 100, 100)]
    [InlineData(8, 108, 100)]
    public void Trimmed_main_segment_uses_only_the_kept_range(double start, double end, double kept)
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", start, end, true, "intro.mp4", 5, true, "outro.mp4", 4);
        var main = timeline.Segments.Single(segment => segment.Kind == PreviewSegmentKind.Main);

        Assert.Equal(start, main.SourceStartSeconds);
        Assert.Equal(kept, main.DurationSeconds);
        Assert.Equal(5 + kept + 4, timeline.DurationSeconds);
        Assert.Equal(start, main.ToSource(main.TimelineStartSeconds));
        Assert.Equal(end, main.ToSource(main.TimelineEndSeconds), 3);
    }

    [Fact]
    public void Playback_walks_intro_trimmed_main_and_outro_using_media_positions()
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", 8, 108, true, "intro.mp4", 5, true, "outro.mp4", 4);
        var playback = new PreviewPlayback();
        var opened = playback.Load(timeline);

        Assert.Equal(PreviewTransportKind.Open, opened.Kind);
        Assert.Equal("intro.mp4", opened.Source);
        Assert.False(opened.Play);

        var playing = playback.TogglePlay();
        Assert.True(playing.Play);
        Assert.Equal(PreviewSegmentKind.Intro, playback.ActiveSegment!.Kind);

        var main = playback.OnSourcePosition(4.97);
        Assert.Equal(PreviewTransportKind.Open, main.Kind);
        Assert.Equal("main.mp4", main.Source);
        Assert.Equal(8, main.SourcePosition, 3);
        Assert.True(main.Play);
        Assert.Equal(5, main.TimelinePosition, 3);

        var inside = playback.OnSourcePosition(20);
        Assert.Equal(PreviewTransportKind.Hold, inside.Kind);
        Assert.Equal(17, inside.TimelinePosition, 3);

        var outro = playback.OnSourcePosition(107.97);
        Assert.Equal("outro.mp4", outro.Source);
        Assert.True(outro.Play);

        var stopped = playback.OnMediaEnded();
        Assert.Equal(PreviewTransportKind.Stop, stopped.Kind);
        Assert.False(playback.IsPlaying);
        Assert.Equal(timeline.DurationSeconds, playback.PositionSeconds);
    }

    [Fact]
    public void Seeking_uses_the_final_timeline_and_skips_discarded_media()
    {
        var timeline = PreviewTimeline.ComposeFinal("main.mp4", 8, 48, true, "intro.mp4", 5, false, null, 0);
        var playback = new PreviewPlayback();
        playback.Load(timeline);

        var transport = playback.Seek(15, false);

        Assert.Equal(PreviewSegmentKind.Main, playback.ActiveSegment!.Kind);
        Assert.Equal(18, transport.SourcePosition, 3);
        Assert.Equal(15, playback.PositionSeconds, 3);
        Assert.False(transport.Play);
    }

    [Fact]
    public void Updating_the_trim_keeps_a_visible_frame_inside_the_new_range()
    {
        var playback = new PreviewPlayback();
        playback.Load(PreviewTimeline.ComposeFinal("main.mp4", 0, 120, false, null, 0, false, null, 0));
        playback.Seek(30, false);

        var transport = playback.Update(PreviewTimeline.ComposeFinal("main.mp4", 8, 100, true, "intro.mp4", 5, true, "outro.mp4", 4));

        Assert.Equal(PreviewSegmentKind.Main, playback.ActiveSegment!.Kind);
        Assert.Equal(30, playback.ActiveSegment.ToSource(playback.PositionSeconds), 3);
        Assert.Equal(27, playback.PositionSeconds, 3);
        Assert.Equal(PreviewTransportKind.Hold, transport.Kind);
    }

    [Fact]
    public void A_trim_that_passes_the_playhead_moves_to_the_new_in_point()
    {
        var playback = new PreviewPlayback();
        playback.Load(PreviewTimeline.ComposeFinal("main.mp4", 0, 120, false, null, 0, false, null, 0));
        playback.Seek(4, false);

        var transport = playback.Update(PreviewTimeline.ComposeFinal("main.mp4", 10, 120, false, null, 0, false, null, 0));

        Assert.Equal(PreviewTransportKind.Seek, transport.Kind);
        Assert.Equal(10, transport.SourcePosition, 3);
        Assert.Equal(0, playback.PositionSeconds, 3);
    }

    [Fact]
    public void A_new_intro_pulls_a_paused_preview_back_to_the_start()
    {
        var playback = new PreviewPlayback();
        playback.Load(PreviewTimeline.ComposeFinal("main.mp4", 0, 8, false, null, 0, false, null, 0));

        var transport = playback.Update(PreviewTimeline.ComposeFinal("main.mp4", 0, 8, true, "intro.mp4", 11, true, "outro.mp4", 12));

        Assert.Equal(PreviewTransportKind.Open, transport.Kind);
        Assert.Equal("intro.mp4", transport.Source);
        Assert.Equal(0, playback.PositionSeconds);
        Assert.Equal(PreviewSegmentKind.Intro, playback.ActiveSegment!.Kind);
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void Replacing_the_video_reloads_from_the_start()
    {
        var playback = new PreviewPlayback();
        playback.Load(PreviewTimeline.ComposeFinal("first.mp4", 10, 40, true, "intro.mp4", 5, false, null, 0));
        playback.Seek(12, true);

        var transport = playback.Load(PreviewTimeline.ComposeFinal("second.mp4", 0, 20, false, null, 0, false, null, 0));

        Assert.Equal("second.mp4", transport.Source);
        Assert.Equal(0, playback.PositionSeconds);
        Assert.False(playback.IsPlaying);
        Assert.Equal(PreviewSegmentKind.Main, playback.ActiveSegment!.Kind);
    }
}

public sealed class VideoTrimTests
{
    [Fact]
    public void Handles_cannot_cross_and_keep_at_least_one_second()
    {
        var (start, end) = VideoTrim.ClampStart(19, 20, 20);
        Assert.Equal(19, start);
        Assert.Equal(20, end);

        (start, end) = VideoTrim.ClampEnd(5, 5.2, 20);
        Assert.Equal(5, start);
        Assert.Equal(6, end);

        (start, end) = VideoTrim.ClampStart(0, 20, 20);
        Assert.Equal((0, 20), (start, end));
        Assert.False(VideoTrim.IsActive(start, end, 20));
    }

    [Fact]
    public void Edges_snap_back_to_the_full_video()
    {
        var (start, end) = VideoTrim.ClampEnd(0, 19.97, 20);
        Assert.Equal((0, 20), (start, end));

        (start, end) = VideoTrim.ClampStart(0.04, 20, 20);
        Assert.Equal(0, start);
    }

    [Fact]
    public void A_very_short_video_cannot_be_trimmed_away()
    {
        var (start, end) = VideoTrim.ClampStart(0.3, 0.4, 0.4);
        Assert.Equal(0, start);
        Assert.Equal(0.4, end);
        Assert.False(VideoTrim.IsActive(start, end, 0.4));
    }

    [Theory]
    [InlineData(3, "00:03")]
    [InlineData(99, "01:39")]
    [InlineData(3661, "01:01:01")]
    public void Clock_hides_milliseconds(double seconds, string expected) =>
        Assert.Equal(expected, TimeFormat.Clock(seconds));
}

public sealed class TrimPipelineTests
{
    [Fact]
    public async Task Loading_another_video_clears_the_previous_trim()
    {
        var paths = new AppPaths();
        if (!File.Exists(paths.FfmpegPath)) return;
        paths.EnsureDirectories();
        var first = Path.Combine(paths.TempDirectory, $"trim-a-{Guid.NewGuid():N}.mp4");
        var second = Path.Combine(paths.TempDirectory, $"trim-b-{Guid.NewGuid():N}.mp4");
        try
        {
            await CreateVideoAsync(paths.FfmpegPath, first, 4, withAudio: true);
            await CreateVideoAsync(paths.FfmpegPath, second, 6, withAudio: false);
            var viewModel = new MainViewModel();
            await viewModel.InitializeAsync();

            await viewModel.LoadVideoAsync(first);
            Assert.True(viewModel.MediaRevision > 0);
            Assert.Contains(viewModel.Timeline.Segments, segment => segment.Kind == PreviewSegmentKind.Main);
            viewModel.SetTrimStart(1);
            Assert.True(viewModel.IsTrimmed);
            var trimmedDuration = viewModel.TotalDurationSeconds;

            viewModel.SelectFormatCommand.Execute(OutputFormat.Portrait);
            Assert.DoesNotContain(viewModel.Timeline.Segments, segment => segment.Kind == PreviewSegmentKind.Intro);

            viewModel.SelectFormatCommand.Execute(OutputFormat.Square);
            Assert.Contains(viewModel.Timeline.Segments, segment => segment.Kind == PreviewSegmentKind.Outro);
            Assert.DoesNotContain(viewModel.Timeline.Segments, segment => segment.Kind == PreviewSegmentKind.Intro);

            viewModel.SelectFormatCommand.Execute(OutputFormat.Landscape);
            viewModel.AddIntro = true;
            viewModel.AddOutro = true;
            Assert.Contains(viewModel.Timeline.Segments, segment => segment.Kind == PreviewSegmentKind.Intro);
            Assert.True(viewModel.TotalDurationSeconds > viewModel.KeptDurationSeconds);

            var revision = viewModel.MediaRevision;
            await viewModel.LoadVideoAsync(second);
            Assert.False(viewModel.IsTrimmed);
            Assert.True(viewModel.MediaRevision > revision);
            Assert.Equal(0, viewModel.TrimStartSeconds);
            Assert.NotEqual(trimmedDuration, viewModel.TotalDurationSeconds);
            Assert.Contains("Sin audio", viewModel.VideoSecondaryDetails);
            var estimated = PresetService.EstimateBytes(viewModel.TotalDurationSeconds, int.Parse(viewModel.VideoBitrate), int.Parse(viewModel.AudioBitrate));
            Assert.Contains($"{estimated / 1_000_000d:0}", viewModel.EstimatedSizeLabel);
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
        }
    }

    [Fact]
    public async Task Export_keeps_only_the_selected_interval_and_leaves_the_source_intact()
    {
        var paths = new AppPaths();
        if (!File.Exists(paths.FfmpegPath)) return;
        paths.EnsureDirectories();
        var sourcePath = Path.Combine(paths.TempDirectory, $"trim-src-{Guid.NewGuid():N}.mp4");
        var silentPath = Path.Combine(paths.TempDirectory, $"trim-silent-{Guid.NewGuid():N}.mp4");
        var outputPath = Path.Combine(paths.TempDirectory, $"trim-out-{Guid.NewGuid():N}.mp4");
        var silentOutput = Path.Combine(paths.TempDirectory, $"trim-silent-out-{Guid.NewGuid():N}.mp4");
        try
        {
            await CreateVideoAsync(paths.FfmpegPath, sourcePath, 6, withAudio: true);
            await CreateVideoAsync(paths.FfmpegPath, silentPath, 6, withAudio: false);
            var sourceLength = new FileInfo(sourcePath).Length;
            var sourceStamp = File.GetLastWriteTimeUtc(sourcePath);
            var presetService = new PresetService(paths);
            await presetService.LoadAsync();
            var probe = new FFmpeg.FfprobeService(paths.FfprobePath);
            var source = await probe.ProbeAsync(sourcePath);
            var export = new ExportService(paths, presetService);
            var request = new ExportRequest(source, OutputFormat.Landscape, FramingMode.Crop, 320, 180, 25,
                800, 96, "libx264", "aac", false, false, false, null, WatermarkPosition.TopRight,
                0.12, 0.035, outputPath, 1, 4);

            await export.ExportAsync(request, null, CancellationToken.None);
            var output = await probe.ProbeAsync(outputPath);

            Assert.InRange(output.DurationSeconds, 2.7, 3.3);
            Assert.True(output.HasAudio);
            Assert.Equal(sourceLength, new FileInfo(sourcePath).Length);
            Assert.Equal(sourceStamp, File.GetLastWriteTimeUtc(sourcePath));

            var silent = await probe.ProbeAsync(silentPath);
            await export.ExportAsync(request with { Source = silent, OutputPath = silentOutput, TrimStartSeconds = 0, TrimEndSeconds = 2 }, null, CancellationToken.None);
            var silentResult = await probe.ProbeAsync(silentOutput);
            Assert.InRange(silentResult.DurationSeconds, 1.7, 2.3);
            Assert.False(silent.HasAudio);
            Assert.True(silentResult.HasAudio);
        }
        finally
        {
            foreach (var path in new[] { sourcePath, silentPath, outputPath, silentOutput })
                if (File.Exists(path)) File.Delete(path);
        }
    }

    private static async Task CreateVideoAsync(string ffmpeg, string path, int seconds, bool withAudio)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", $"color=c=0x52799A:s=640x360:r=25:d={seconds}"
        };
        if (withAudio)
        {
            arguments.AddRange(["-f", "lavfi", "-i", $"sine=frequency=440:sample_rate=48000:duration={seconds}"]);
            arguments.AddRange(["-shortest", "-c:a", "aac", "-b:a", "96k"]);
        }
        else arguments.Add("-an");
        arguments.AddRange(["-c:v", "libx264", "-pix_fmt", "yuv420p", "-t", seconds.ToString(), "-y", path]);

        var start = new System.Diagnostics.ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }
}
