using System.Globalization;

namespace VideoPack.Models;

public static class VideoRules
{
    public static OutputFormat DetectFormat(int width, int height) =>
        width == height ? OutputFormat.Square : width > height ? OutputFormat.Landscape : OutputFormat.Portrait;

    public static bool IsIntroAvailable(OutputFormat format) => format == OutputFormat.Landscape;

    public static string GetOutroAsset(OutputFormat format, AppConfiguration configuration) =>
        format == OutputFormat.Landscape ? configuration.OutroLandscape : configuration.OutroPortrait;

    public static QualityPreset AfterManualChange() => QualityPreset.Custom;

    public static (string X, string Y) GetWatermarkCoordinates(int width, int height, double safeMarginRatio, WatermarkPosition position)
    {
        var ratio = safeMarginRatio.ToString("0.####", CultureInfo.InvariantCulture);
        var margin = $"trunc(min({width}\\,{height})*{ratio})";
        var x = position is WatermarkPosition.TopRight or WatermarkPosition.BottomRight
            ? $"{width}-overlay_w-{margin}" : margin;
        var y = position is WatermarkPosition.BottomLeft or WatermarkPosition.BottomRight
            ? $"{height}-overlay_h-{margin}" : margin;
        return (x, y);
    }
}