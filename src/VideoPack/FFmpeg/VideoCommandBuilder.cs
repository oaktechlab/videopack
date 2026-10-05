using System.Globalization;
using VideoPack.Models;

namespace VideoPack.FFmpeg;

public sealed record VideoSegment(
    string Path,
    double DurationSeconds,
    bool HasAudio,
    bool Watermark = false,
    double TrimStartSeconds = 0,
    double? TrimEndSeconds = null);

public sealed class VideoCommandBuilder
{
    public IReadOnlyList<string> Build(ExportRequest request, IReadOnlyList<VideoSegment> segments, string outputPath)
    {
        if (segments.Count == 0) throw new ArgumentException("El pipeline necesita al menos un vídeo.", nameof(segments));
        var arguments = new List<string> { "-hide_banner", "-y" };
        foreach (var segment in segments)
        {
            // Generate missing timestamps before an accurate trim. Input options apply only to the next file.
            if (segment.TrimEndSeconds is not null)
            {
                arguments.Add("-fflags");
                arguments.Add("+genpts");
            }
            arguments.Add("-i");
            arguments.Add(segment.Path);
        }
        var watermarkTarget = -1;
        if (request.AddWatermark && !string.IsNullOrWhiteSpace(request.WatermarkPath))
        {
            for (var index = 0; index < segments.Count; index++)
            {
                if (!segments[index].Watermark) continue;
                watermarkTarget = index;
                break;
            }
        }

        var watermarkInput = -1;
        if (watermarkTarget >= 0)
        {
            watermarkInput = segments.Count;
            arguments.Add("-i");
            arguments.Add(request.WatermarkPath!);
        }

        var filters = new List<string>();
        var concatInputs = new List<string>();
        for (var index = 0; index < segments.Count; index++)
        {
            var fit = request.Framing == FramingMode.Fit
                ? $"scale={request.Width}:{request.Height}:force_original_aspect_ratio=decrease,pad={request.Width}:{request.Height}:(ow-iw)/2:(oh-ih)/2:color=black"
                : $"scale={request.Width}:{request.Height}:force_original_aspect_ratio=increase,crop={request.Width}:{request.Height}";
            var videoLabel = index == watermarkTarget ? $"v{index}src" : $"v{index}";
            filters.Add(VideoFilter(index, segments[index], fit, request.Fps, videoLabel));

            var duration = Math.Max(0.01, segments[index].DurationSeconds).ToString("0.######", CultureInfo.InvariantCulture);
            if (segments[index].HasAudio)
                filters.Add(AudioFilter(index, segments[index], duration));
            else
                filters.Add($"anullsrc=channel_layout=stereo:sample_rate=48000,atrim=duration={duration}[a{index}]");
            concatInputs.Add($"[v{index}][a{index}]");
        }

        if (watermarkTarget >= 0)
        {
            var logoWidth = Math.Max(2, (int)Math.Round(request.Width * request.WatermarkWidthRatio / 2) * 2);
            filters.Add($"[{watermarkInput}:v:0]scale={logoWidth}:-1[wm]");
            var (x, y) = VideoRules.GetWatermarkCoordinates(request.Width, request.Height, request.SafeMarginRatio, request.WatermarkPosition);
            filters.Add($"[v{watermarkTarget}src][wm]overlay=x={x}:y={y}:eof_action=repeat:shortest=0[v{watermarkTarget}]");
        }

        filters.Add($"{string.Concat(concatInputs)}concat=n={segments.Count}:v=1:a=1[basev][basea]");
        filters.Add("[basev]null[outv]");

        arguments.Add("-filter_complex");
        arguments.Add(string.Join(';', filters));
        arguments.AddRange(["-map", "[outv]", "-map", "[basea]", "-c:v", request.VideoCodec, "-preset", "medium"]);
        arguments.AddRange(["-b:v", $"{request.VideoBitrateKbps}k", "-maxrate", $"{request.VideoBitrateKbps}k", "-bufsize", $"{request.VideoBitrateKbps * 2}k"]);
        arguments.AddRange(["-r", request.Fps.ToString(CultureInfo.InvariantCulture), "-pix_fmt", "yuv420p", "-c:a", request.AudioCodec, "-b:a", $"{request.AudioBitrateKbps}k"]);
        arguments.AddRange(["-movflags", "+faststart", "-progress", "pipe:1", "-nostats", outputPath]);
        return arguments;
    }

    private static string VideoFilter(int index, VideoSegment segment, string fit, int fps, string label)
    {
        // Reset timestamps first so trim is relative to the first frame, including files whose PTS does not start at zero.
        var head = segment.TrimEndSeconds is double end
            ? $"setpts=PTS-STARTPTS,trim=start={FormatSeconds(segment.TrimStartSeconds)}:end={FormatSeconds(end)},setpts=PTS-STARTPTS,"
            : "setpts=PTS-STARTPTS,";
        return $"[{index}:v:0]{head}{fit},fps={fps},setsar=1,format=yuv420p[{label}]";
    }

    private static string AudioFilter(int index, VideoSegment segment, string duration)
    {
        var head = segment.TrimEndSeconds is double end
            ? $"asetpts=PTS-STARTPTS,atrim=start={FormatSeconds(segment.TrimStartSeconds)}:end={FormatSeconds(end)},asetpts=PTS-STARTPTS,"
            : string.Empty;
        return $"[{index}:a:0]{head}aresample=48000,aformat=sample_fmts=fltp:channel_layouts=stereo,asetpts=PTS-STARTPTS,apad,atrim=duration={duration}[a{index}]";
    }

    private static string FormatSeconds(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}