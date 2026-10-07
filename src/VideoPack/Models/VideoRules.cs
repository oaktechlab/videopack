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

    public const int TextWatermarkMaxLines = 3;
    public const int TextWatermarkMaxLineLength = 30;
    public const string DefaultTextWatermark = "Nombre del Proyecto\nDoc Emmett Brown · Marty McFly";
    public const string TextWatermarkFontAsset = "assets/fonts/OpenSans_SemiCondensed-Light.ttf";
    public const double TextWatermarkFontShortEdgeRatio = 0.032;

    public static (string X, string Y) GetWatermarkCoordinates(int width, int height, double safeMarginRatio, WatermarkPosition position) =>
        GetCornerCoordinates(width, height, safeMarginRatio, position, "overlay_w", "overlay_h");

    public static (string X, string Y) GetTextWatermarkCoordinates(int width, int height, double safeMarginRatio, WatermarkPosition position) =>
        GetCornerCoordinates(width, height, safeMarginRatio, position, "text_w", "text_h");

    public static int TextWatermarkFontSize(int width, int height) =>
        Math.Max(12, (int)Math.Round(Math.Min(width, height) * TextWatermarkFontShortEdgeRatio, MidpointRounding.AwayFromZero));

    public static int TextWatermarkLineSpacing(int fontSize) =>
        Math.Max(0, (int)Math.Round(fontSize * 0.12, MidpointRounding.AwayFromZero));

    public static string TextWatermarkAlignment(WatermarkPosition position) =>
        position is WatermarkPosition.TopRight or WatermarkPosition.BottomRight ? "right" : "left";

    public static string NormalizeTextWatermark(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var lines = value.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > TextWatermarkMaxLines)
            lines = lines[..TextWatermarkMaxLines];
        for (var index = 0; index < lines.Length; index++)
        {
            if (lines[index].Length > TextWatermarkMaxLineLength)
                lines[index] = lines[index][..TextWatermarkMaxLineLength];
        }
        return string.Join('\n', lines);
    }

    public static bool TextWatermarkHasContent(string? value) =>
        !string.IsNullOrWhiteSpace(NormalizeTextWatermark(value).Replace("\n", ""));

    public static string EscapeFilterPath(string path) =>
        path.Replace('\\', '/').Replace(":", "\\:").Replace("'", "\\'");

    private static (string X, string Y) GetCornerCoordinates(
        int width, int height, double safeMarginRatio, WatermarkPosition position, string widthToken, string heightToken)
    {
        var ratio = safeMarginRatio.ToString("0.####", CultureInfo.InvariantCulture);
        var margin = $"trunc(min({width}\\,{height})*{ratio})";
        var x = position is WatermarkPosition.TopRight or WatermarkPosition.BottomRight
            ? $"{width}-{widthToken}-{margin}" : margin;
        var y = position is WatermarkPosition.BottomLeft or WatermarkPosition.BottomRight
            ? $"{height}-{heightToken}-{margin}" : margin;
        return (x, y);
    }
}