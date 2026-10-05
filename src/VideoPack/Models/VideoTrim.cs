namespace VideoPack.Models;

/// <summary>
/// In and out points of the main video. The source file is never modified.
/// The UI snaps to tenths of a second; the values themselves are what export uses.
/// </summary>
public static class VideoTrim
{
    public const double MinimumKeptSeconds = 1;
    public const double EdgeSnapSeconds = 0.05;

    public static double Kept(double start, double end) => Math.Max(0, end - start);

    public static bool IsActive(double start, double end, double sourceDuration) =>
        sourceDuration > 0 && (start > 0.001 || end < sourceDuration - 0.001);

    public static (double Start, double End) Full(double sourceDuration) => (0, Math.Max(0, sourceDuration));

    public static (double Start, double End) ClampStart(double start, double end, double sourceDuration)
    {
        var duration = Math.Max(0, sourceDuration);
        if (duration <= 0) return (0, 0);
        end = Math.Clamp(end, 0, duration);
        start = Snap(start, duration);
        var maxStart = Math.Max(0, end - MinimumKept(duration));
        return (Math.Clamp(start, 0, maxStart), end);
    }

    public static (double Start, double End) ClampEnd(double start, double end, double sourceDuration)
    {
        var duration = Math.Max(0, sourceDuration);
        if (duration <= 0) return (0, 0);
        start = Math.Clamp(start, 0, duration);
        end = Snap(end, duration);
        var minEnd = Math.Min(duration, start + MinimumKept(duration));
        return (start, Math.Clamp(end, minEnd, duration));
    }

    private static double MinimumKept(double duration) => Math.Min(MinimumKeptSeconds, duration);

    private static double Snap(double value, double duration)
    {
        if (value <= EdgeSnapSeconds) return 0;
        if (Math.Abs(value - duration) <= EdgeSnapSeconds) return duration;
        return Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), 0, duration);
    }
}
