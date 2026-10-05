namespace VideoPack.Models;

public static class TimeFormat
{
    public static string Clock(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var whole = (int)Math.Round(seconds, MidpointRounding.AwayFromZero);
        var hours = whole / 3600;
        var minutes = whole % 3600 / 60;
        var secs = whole % 60;
        return hours > 0
            ? $"{hours:00}:{minutes:00}:{secs:00}"
            : $"{minutes:00}:{secs:00}";
    }
}
