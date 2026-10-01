using System.Text.Json;
using VideoPack.Models;

namespace VideoPack.Services;

public sealed class PresetService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    public AppConfiguration Configuration { get; private set; } = new();
    public IReadOnlyList<PresetDefinition> Presets { get; private set; } = [];

    public async Task LoadAsync()
    {
        if (File.Exists(paths.ConfigurationPath))
        {
            await using var configStream = File.OpenRead(paths.ConfigurationPath);
            Configuration = await JsonSerializer.DeserializeAsync<AppConfiguration>(configStream, JsonOptions) ?? new();
        }
        if (File.Exists(paths.PresetsPath))
        {
            await using var presetsStream = File.OpenRead(paths.PresetsPath);
            Presets = await JsonSerializer.DeserializeAsync<List<PresetDefinition>>(presetsStream, JsonOptions) ?? [];
        }
    }

    public PresetDefinition? Get(QualityPreset preset) => Presets.FirstOrDefault(x =>
        string.Equals(x.Name, preset.ToString(), StringComparison.OrdinalIgnoreCase));

    public static (int Width, int Height) GetResolution(int longEdge, OutputFormat format) => format switch
    {
        OutputFormat.Landscape => (longEdge, longEdge * 9 / 16),
        OutputFormat.Portrait => (longEdge * 9 / 16, longEdge),
        _ => (longEdge, longEdge)
    };

    public static long EstimateBytes(double durationSeconds, int videoBitrateKbps, int audioBitrateKbps) =>
        (long)Math.Ceiling(Math.Max(0, durationSeconds) * Math.Max(0, videoBitrateKbps + audioBitrateKbps) * 1000d / 8d);
}