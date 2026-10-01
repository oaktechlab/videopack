namespace VideoPack.Services;

public sealed class AppPaths
{
    public string RootDirectory { get; }
    public string DataDirectory => Path.Combine(RootDirectory, "data");
    public string FfmpegPath => Path.Combine(DataDirectory, "ffmpeg", "ffmpeg.exe");
    public string FfprobePath => Path.Combine(DataDirectory, "ffmpeg", "ffprobe.exe");
    public string PresetsPath => Path.Combine(DataDirectory, "presets", "presets.json");
    public string ConfigurationPath => Path.Combine(DataDirectory, "presets", "appsettings.json");
    public string TempDirectory => Path.Combine(DataDirectory, "temp");
    public string LogsDirectory => Path.Combine(DataDirectory, "logs");

    public AppPaths()
    {
        var candidate = new DirectoryInfo(AppContext.BaseDirectory);
        while (candidate is not null)
        {
            if (File.Exists(Path.Combine(candidate.FullName, "data", "presets", "presets.json")))
            {
                RootDirectory = candidate.FullName;
                return;
            }
            candidate = candidate.Parent;
        }
        RootDirectory = AppContext.BaseDirectory;
    }

    public string ResolveAsset(string relativePath) => Path.GetFullPath(Path.Combine(DataDirectory, relativePath));

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(TempDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}