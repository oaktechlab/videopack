namespace VideoPack.Models;

public enum OutputFormat { Landscape, Portrait, Square }
public enum FramingMode { Fit, Crop }
public enum QualityPreset { Master, Youtube, Social, Custom }
public enum WatermarkPosition { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed record VideoMetadata(
    string Path,
    int Width,
    int Height,
    double FramesPerSecond,
    double DurationSeconds,
    string VideoCodec,
    long VideoBitrate,
    bool HasAudio,
    string AudioCodec,
    long FileSizeBytes)
{
    public string Resolution => $"{Width} x {Height}";
    public string AspectRatio => Width > 0 && Height > 0 ? SimplifyRatio(Width, Height) : "Desconocido";
    public string DurationLabel => TimeSpan.FromSeconds(Math.Max(0, DurationSeconds)).ToString(DurationSeconds >= 3600 ? "h\\:mm\\:ss" : "m\\:ss");
    public string FileSizeLabel => FileSizeBytes >= 1_000_000_000
        ? $"{FileSizeBytes / 1_000_000_000d:0.0} GB"
        : $"{FileSizeBytes / 1_000_000d:0} MB";

    private static string SimplifyRatio(int width, int height)
    {
        var a = width;
        var b = height;
        while (b != 0) (a, b) = (b, a % b);
        return $"{width / a}:{height / a}";
    }
}

public sealed record LogoAsset(string Name, string FileName);

public sealed class AppConfiguration
{
    public string IntroLandscape { get; set; } = "assets/intros/bumper_tecnalia_in_landscape.mp4";
    public string OutroLandscape { get; set; } = "assets/outros/bumper_tecnalia_out_landscape.mp4";
    public string OutroPortrait { get; set; } = "assets/outros/bumper_tecnalia_out_portrait.mp4";
    public List<LogoAsset> Logos { get; set; } = [];
    public double WatermarkWidthRatio { get; set; } = 0.12;
    public double SafeMarginRatio { get; set; } = 0.07;
    public int AudioBitrateKbps { get; set; } = 192;
    public int DefaultFps { get; set; } = 25;
    public string VideoCodec { get; set; } = "libx264";
    public string AudioCodec { get; set; } = "aac";
}

public sealed record PresetDefinition(
    string Name,
    string DisplayName,
    int Height,
    int VideoBitrateKbps,
    string Format,
    string Framing,
    int Fps,
    string VideoCodec,
    string AudioCodec,
    int AudioBitrateKbps,
    string Description);

public sealed record ExportRequest(
    VideoMetadata Source,
    OutputFormat Format,
    FramingMode Framing,
    int Width,
    int Height,
    int Fps,
    int VideoBitrateKbps,
    int AudioBitrateKbps,
    string VideoCodec,
    string AudioCodec,
    bool AddIntro,
    bool AddOutro,
    bool AddWatermark,
    string? WatermarkPath,
    WatermarkPosition WatermarkPosition,
    double WatermarkWidthRatio,
    double SafeMarginRatio,
    string OutputPath,
    double TrimStartSeconds = 0,
    double? TrimEndSeconds = null);

public sealed record ExportResult(string OutputPath, double DurationSeconds, long FileSizeBytes);