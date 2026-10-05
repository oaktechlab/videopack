namespace VideoPack.Models;

public enum PreviewSegmentKind { Intro, Main, Outro }

public readonly record struct PreviewSegmentDraft(
    PreviewSegmentKind Kind,
    string Source,
    double SourceStartSeconds,
    double DurationSeconds);

public sealed record PreviewSegment(
    PreviewSegmentKind Kind,
    string Source,
    double SourceStartSeconds,
    double DurationSeconds,
    double TimelineStartSeconds)
{
    public double TimelineEndSeconds => TimelineStartSeconds + DurationSeconds;

    public double ToSource(double timelineSeconds)
    {
        var local = Math.Clamp(timelineSeconds - TimelineStartSeconds, 0, Math.Max(0, DurationSeconds));
        return SourceStartSeconds + local;
    }

    public double ToTimeline(double sourceSeconds)
    {
        var local = Math.Clamp(sourceSeconds - SourceStartSeconds, 0, Math.Max(0, DurationSeconds));
        return TimelineStartSeconds + local;
    }
}

/// <summary>
/// Virtual timeline of the video that will be exported: intro, trimmed main video, outro.
/// Durations come from the media itself. Nothing is rendered to a temporary file.
/// </summary>
public sealed class PreviewTimeline
{
    public static PreviewTimeline Empty { get; } = new([]);

    private PreviewTimeline(IReadOnlyList<PreviewSegment> segments)
    {
        Segments = segments;
        DurationSeconds = segments.Count == 0 ? 0 : segments[^1].TimelineEndSeconds;
    }

    public IReadOnlyList<PreviewSegment> Segments { get; }
    public double DurationSeconds { get; }

    public static PreviewTimeline ComposeFinal(
        string? mainPath,
        double trimStart,
        double trimEnd,
        bool includeIntro,
        string? introPath,
        double introDuration,
        bool includeOutro,
        string? outroPath,
        double outroDuration)
    {
        if (string.IsNullOrWhiteSpace(mainPath)) return Empty;
        var drafts = new List<PreviewSegmentDraft>();
        if (includeIntro && introDuration > 0 && !string.IsNullOrWhiteSpace(introPath))
            drafts.Add(new PreviewSegmentDraft(PreviewSegmentKind.Intro, introPath, 0, introDuration));
        var kept = Math.Max(0, trimEnd - trimStart);
        if (kept > 0)
            drafts.Add(new PreviewSegmentDraft(PreviewSegmentKind.Main, mainPath, Math.Max(0, trimStart), kept));
        if (includeOutro && outroDuration > 0 && !string.IsNullOrWhiteSpace(outroPath))
            drafts.Add(new PreviewSegmentDraft(PreviewSegmentKind.Outro, outroPath, 0, outroDuration));
        return Compose(drafts);
    }

    public static PreviewTimeline Compose(IReadOnlyList<PreviewSegmentDraft> drafts)
    {
        var segments = new List<PreviewSegment>();
        var cursor = 0d;
        foreach (var draft in drafts)
        {
            if (draft.DurationSeconds <= 0.0001 || string.IsNullOrWhiteSpace(draft.Source)) continue;
            segments.Add(new PreviewSegment(draft.Kind, draft.Source, Math.Max(0, draft.SourceStartSeconds), draft.DurationSeconds, cursor));
            cursor += draft.DurationSeconds;
        }
        return new PreviewTimeline(segments);
    }

    public int IndexAt(double timelineSeconds)
    {
        if (Segments.Count == 0) return -1;
        if (timelineSeconds <= 0) return 0;
        for (var index = 0; index < Segments.Count; index++)
        {
            if (timelineSeconds < Segments[index].TimelineEndSeconds - 0.0000001)
                return index;
        }
        return Segments.Count - 1;
    }

    public PreviewSegment? SegmentAt(double timelineSeconds)
    {
        var index = IndexAt(timelineSeconds);
        return index < 0 ? null : Segments[index];
    }
}

public enum PreviewTransportKind { None, Hold, Open, Seek, Play, Pause, Stop, Clear }

public readonly record struct PreviewTransport(
    PreviewTransportKind Kind,
    string? Source,
    double SourcePosition,
    bool Play,
    double TimelinePosition)
{
    public static PreviewTransport None { get; } = new(PreviewTransportKind.None, null, 0, false, 0);
    public static PreviewTransport Hold(double timelinePosition) => new(PreviewTransportKind.Hold, null, 0, true, timelinePosition);
    public static PreviewTransport Open(PreviewSegment segment, double sourcePosition, bool play, double timelinePosition) =>
        new(PreviewTransportKind.Open, segment.Source, sourcePosition, play, timelinePosition);
    public static PreviewTransport SeekMedia(double sourcePosition, bool play, double timelinePosition) =>
        new(PreviewTransportKind.Seek, null, sourcePosition, play, timelinePosition);
    public static PreviewTransport Resume(double sourcePosition, double timelinePosition) =>
        new(PreviewTransportKind.Play, null, sourcePosition, true, timelinePosition);
    public static PreviewTransport Paused(double timelinePosition) => new(PreviewTransportKind.Pause, null, 0, false, timelinePosition);
    public static PreviewTransport Stopped(double timelinePosition) => new(PreviewTransportKind.Stop, null, 0, false, timelinePosition);
    public static PreviewTransport Cleared() => new(PreviewTransportKind.Clear, null, 0, false, 0);
}

/// <summary>
/// Maps the transport clock onto the virtual timeline. Segment changes follow the
/// real media position (and the end of each file), not an independent timer.
/// </summary>
public sealed class PreviewPlayback
{
    public const double BoundaryEpsilonSeconds = 0.05;
    private const double SeekDeadZoneSeconds = 0.02;

    public PreviewTimeline Timeline { get; private set; } = PreviewTimeline.Empty;
    public double PositionSeconds { get; private set; }
    public bool IsPlaying { get; private set; }
    public int SegmentIndex { get; private set; } = -1;

    public PreviewSegment? ActiveSegment =>
        SegmentIndex >= 0 && SegmentIndex < Timeline.Segments.Count ? Timeline.Segments[SegmentIndex] : null;

    public PreviewTransport Load(PreviewTimeline timeline)
    {
        Timeline = timeline ?? PreviewTimeline.Empty;
        IsPlaying = false;
        PositionSeconds = 0;
        if (Timeline.Segments.Count == 0)
        {
            SegmentIndex = -1;
            return PreviewTransport.Cleared();
        }

        SegmentIndex = 0;
        var first = Timeline.Segments[0];
        return PreviewTransport.Open(first, FramePosition(first.SourceStartSeconds), false, 0);
    }

    public PreviewTransport Update(PreviewTimeline timeline)
    {
        var previous = ActiveSegment;
        var previousSource = previous is null ? 0 : previous.ToSource(PositionSeconds);
        var wasPlaying = IsPlaying;
        Timeline = timeline ?? PreviewTimeline.Empty;
        if (Timeline.Segments.Count == 0)
        {
            SegmentIndex = -1;
            PositionSeconds = 0;
            IsPlaying = false;
            return PreviewTransport.Cleared();
        }

        if (previous is null)
            return Load(Timeline);

        // Parked on the first frame of the main video: a newly added intro is the real start.
        var introAdded = previous.Kind != PreviewSegmentKind.Intro
            && Timeline.Segments.Count > 0
            && Timeline.Segments[0].Kind == PreviewSegmentKind.Intro;
        var atMainInPoint = previous.Kind == PreviewSegmentKind.Main
            && Math.Abs(previousSource - previous.SourceStartSeconds) <= 0.25;
        if (!wasPlaying && introAdded && atMainInPoint)
            return Seek(0, false, previous, previousSource);

        return Seek(Remap(previous, previousSource, Timeline), wasPlaying, previous, previousSource);
    }

    public PreviewTransport Seek(double timelineSeconds, bool play) =>
        Seek(timelineSeconds, play, ActiveSegment, ActiveSegment is null ? 0 : ActiveSegment.ToSource(PositionSeconds));

    private PreviewTransport Seek(double timelineSeconds, bool play, PreviewSegment? previous, double oldSource)
    {
        if (Timeline.Segments.Count == 0)
        {
            SegmentIndex = -1;
            PositionSeconds = 0;
            IsPlaying = false;
            return PreviewTransport.Cleared();
        }

        var wasPlaying = IsPlaying;

        if (Timeline.DurationSeconds > 0 && timelineSeconds >= Timeline.DurationSeconds - 0.0005)
        {
            var last = Timeline.Segments[^1];
            var source = last.SourceStartSeconds + Math.Max(0, last.DurationSeconds - 0.001);
            PositionSeconds = Timeline.DurationSeconds;
            IsPlaying = false;
            SegmentIndex = Timeline.Segments.Count - 1;
            var changed = previous is null || !SameSource(previous, last);
            return changed
                ? PreviewTransport.Open(last, source, false, PositionSeconds)
                : PreviewTransport.SeekMedia(source, false, PositionSeconds);
        }

        var clamped = Math.Clamp(timelineSeconds, 0, Math.Max(0, Timeline.DurationSeconds));
        var index = Timeline.IndexAt(clamped);
        var segment = Timeline.Segments[index];
        var sourcePosition = segment.ToSource(clamped);
        PositionSeconds = segment.TimelineStartSeconds + Math.Clamp(sourcePosition - segment.SourceStartSeconds, 0, segment.DurationSeconds);
        IsPlaying = play;
        SegmentIndex = index;

        var sourceChanged = previous is null || !SameSource(previous, segment);
        if (!sourceChanged && Math.Abs(oldSource - sourcePosition) < SeekDeadZoneSeconds && play == wasPlaying)
            return PreviewTransport.Hold(PositionSeconds);
        if (!sourceChanged && play && !wasPlaying && Math.Abs(oldSource - sourcePosition) < SeekDeadZoneSeconds)
            return PreviewTransport.Resume(sourcePosition, PositionSeconds);
        return sourceChanged
            ? PreviewTransport.Open(segment, FramePosition(sourcePosition), play, PositionSeconds)
            : PreviewTransport.SeekMedia(FramePosition(sourcePosition), play, PositionSeconds);
    }

    public PreviewTransport TogglePlay()
    {
        if (Timeline.Segments.Count == 0) return PreviewTransport.None;
        if (IsPlaying)
        {
            IsPlaying = false;
            return PreviewTransport.Paused(PositionSeconds);
        }

        if (PositionSeconds >= Timeline.DurationSeconds - BoundaryEpsilonSeconds)
            return Seek(0, true);
        return Seek(PositionSeconds, true);
    }

    public PreviewTransport OnSourcePosition(double sourcePosition)
    {
        var segment = ActiveSegment;
        if (segment is null || !IsPlaying) return PreviewTransport.None;
        if (sourcePosition >= segment.SourceStartSeconds + segment.DurationSeconds - BoundaryEpsilonSeconds)
            return Advance();

        PositionSeconds = segment.ToTimeline(sourcePosition);
        return PreviewTransport.Hold(PositionSeconds);
    }

    public PreviewTransport OnMediaEnded()
    {
        if (ActiveSegment is null || !IsPlaying) return PreviewTransport.None;
        return Advance();
    }

    private PreviewTransport Advance()
    {
        if (SegmentIndex + 1 >= Timeline.Segments.Count)
        {
            PositionSeconds = Timeline.DurationSeconds;
            IsPlaying = false;
            return PreviewTransport.Stopped(PositionSeconds);
        }

        SegmentIndex++;
        var next = Timeline.Segments[SegmentIndex];
        PositionSeconds = next.TimelineStartSeconds;
        IsPlaying = true;
        return PreviewTransport.Open(next, FramePosition(next.SourceStartSeconds), true, PositionSeconds);
    }

    private static double Remap(PreviewSegment previous, double sourcePosition, PreviewTimeline timeline)
    {
        var match = timeline.Segments.FirstOrDefault(segment => segment.Kind == previous.Kind);
        if (match is null) return 0;
        if (previous.Kind == PreviewSegmentKind.Main)
        {
            var end = match.SourceStartSeconds + match.DurationSeconds;
            var clamped = Math.Clamp(sourcePosition, match.SourceStartSeconds, end);
            if (clamped >= end) clamped = Math.Max(match.SourceStartSeconds, end - 0.001);
            return match.ToTimeline(clamped);
        }

        var local = Math.Clamp(sourcePosition - previous.SourceStartSeconds, 0, match.DurationSeconds);
        if (local >= match.DurationSeconds) local = Math.Max(0, match.DurationSeconds - 0.001);
        return match.TimelineStartSeconds + local;
    }

    private static bool SameSource(PreviewSegment left, PreviewSegment right) =>
        string.Equals(left.Source, right.Source, StringComparison.OrdinalIgnoreCase);

    private static double FramePosition(double sourcePosition) => sourcePosition <= 0.001 ? 0.001 : sourcePosition;
}
