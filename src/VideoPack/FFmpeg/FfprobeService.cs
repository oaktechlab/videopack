using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using VideoPack.Models;

namespace VideoPack.FFmpeg;

public sealed class FfprobeService(string executablePath)
{
    public async Task<VideoMetadata> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executablePath)) throw new FileNotFoundException("No se encuentra FFprobe junto a la aplicación.", executablePath);
        if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el vídeo seleccionado.", path);

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-v", "error", "-show_format", "-show_streams", "-of", "json", path })
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("No se pudo iniciar FFprobe.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0) throw new InvalidDataException(string.IsNullOrWhiteSpace(error) ? "FFprobe no ha podido leer el archivo." : error.Trim());

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var streams = root.GetProperty("streams").EnumerateArray().ToList();
        var video = streams.FirstOrDefault(x => GetString(x, "codec_type") == "video");
        if (video.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException("El archivo no contiene una pista de vídeo compatible.");
        var audio = streams.FirstOrDefault(x => GetString(x, "codec_type") == "audio");
        var format = root.TryGetProperty("format", out var formatValue) ? formatValue : default;
        var duration = GetDouble(format, "duration", GetDouble(video, "duration", 0));
        var bitrate = GetLong(video, "bit_rate", GetLong(format, "bit_rate", 0));
        var frameRate = ParseRate(GetString(video, "avg_frame_rate"));
        if (frameRate <= 0) frameRate = ParseRate(GetString(video, "r_frame_rate"));

        return new VideoMetadata(
            path,
            GetInt(video, "width"),
            GetInt(video, "height"),
            frameRate,
            duration,
            GetString(video, "codec_name") ?? "Desconocido",
            bitrate,
            audio.ValueKind != JsonValueKind.Undefined,
            audio.ValueKind == JsonValueKind.Undefined ? "Sin audio" : GetString(audio, "codec_name") ?? "Desconocido",
            new FileInfo(path).Length);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value.ToString() : null;

    private static int GetInt(JsonElement element, string property) =>
        int.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static long GetLong(JsonElement element, string property, long fallback) =>
        long.TryParse(GetString(element, property), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double GetDouble(JsonElement element, string property, double fallback) =>
        double.TryParse(GetString(element, property), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static double ParseRate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var parts = value.Split('/');
        if (parts.Length == 2 && double.TryParse(parts[0], CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], CultureInfo.InvariantCulture, out var denominator) && denominator != 0)
            return numerator / denominator;
        return double.TryParse(value, CultureInfo.InvariantCulture, out var rate) ? rate : 0;
    }
}