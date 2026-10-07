using System.Text;
using System.Text.Json;
using VideoPack.FFmpeg;
using VideoPack.Models;

namespace VideoPack.Services;

public sealed class ExportException(string message, string details, string? logPath = null) : Exception(message)
{
    public string Details { get; } = details;
    public string? LogPath { get; } = logPath;
}

public sealed class ExportService(AppPaths paths, PresetService presetService)
{
    private readonly FfprobeService _probe = new(paths.FfprobePath);
    private readonly VideoCommandBuilder _builder = new();

    public async Task<ExportResult> ExportAsync(ExportRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        paths.EnsureDirectories();
        var segments = new List<VideoSegment>();
        string? stagingPath = null;
        string? textFile = null;
        string? commandLine = null;
        var diagnostics = new StringBuilder();
        var logPath = Path.Combine(paths.LogsDirectory, $"export_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.log");
        try
        {
            ValidateRequest(request);
            (request, textFile) = PrepareTextWatermark(request);
            if (request.AddIntro)
                segments.Add(await ProbeSegmentAsync(paths.ResolveAsset(presetService.Configuration.IntroLandscape), cancellationToken));
            var (keptDuration, trimStart, trimEnd) = ResolveTrim(request);
            var overlayOnMain = request.AddWatermark || request.AddTextWatermark;
            segments.Add(new VideoSegment(request.Source.Path, keptDuration, request.Source.HasAudio, overlayOnMain, trimStart, trimEnd));
            if (request.AddOutro)
            {
                var relativeOutro = VideoRules.GetOutroAsset(request.Format, presetService.Configuration);
                segments.Add(await ProbeSegmentAsync(paths.ResolveAsset(relativeOutro), cancellationToken));
            }

            var totalDuration = segments.Sum(x => x.DurationSeconds);
            var estimatedBytes = PresetService.EstimateBytes(totalDuration, request.VideoBitrateKbps, request.AudioBitrateKbps);
            EnsureDiskSpace(request.OutputPath, estimatedBytes);
            stagingPath = Path.Combine(paths.TempDirectory, $"export_{Guid.NewGuid():N}.partial.mp4");
            EnsureDiskSpace(stagingPath, estimatedBytes);
            var arguments = _builder.Build(request, segments, stagingPath);
            var runner = new FfmpegRunner(paths.FfmpegPath);
            var run = await runner.RunAsync(arguments, totalDuration, progress, cancellationToken);
            commandLine = run.CommandLine;
            diagnostics.AppendLine(run.StandardError);
            if (run.ExitCode != 0 || !File.Exists(stagingPath))
                throw new InvalidOperationException("FFmpeg ha informado de un error durante la codificación.");

            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            if (string.Equals(Path.GetPathRoot(stagingPath), Path.GetPathRoot(request.OutputPath), StringComparison.OrdinalIgnoreCase))
                File.Move(stagingPath, request.OutputPath);
            else
            {
                File.Copy(stagingPath, request.OutputPath);
                File.Delete(stagingPath);
            }
            var result = new ExportResult(request.OutputPath, totalDuration, new FileInfo(request.OutputPath).Length);
            await WriteLogAsync(logPath, request, commandLine, diagnostics.ToString(), null);
            return result;
        }
        catch (OperationCanceledException)
        {
            if (stagingPath is not null && File.Exists(stagingPath)) File.Delete(stagingPath);
            await WriteLogAsync(logPath, request, commandLine, diagnostics.ToString(), "Exportación cancelada por el usuario.");
            throw;
        }
        catch (Exception exception)
        {
            await WriteLogAsync(logPath, request, commandLine, diagnostics.ToString(), exception.ToString());
            throw new ExportException("No se ha podido exportar el vídeo.",
                $"{exception.Message}{Environment.NewLine}{diagnostics}", logPath);
        }
        finally
        {
            if (textFile is not null && File.Exists(textFile))
                try { File.Delete(textFile); } catch (IOException) { }
        }
    }

    private async Task<VideoSegment> ProbeSegmentAsync(string assetPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(assetPath)) throw new FileNotFoundException("Falta una cortinilla necesaria para esta exportación.", assetPath);
        var metadata = await _probe.ProbeAsync(assetPath, cancellationToken);
        return new VideoSegment(assetPath, metadata.DurationSeconds, metadata.HasAudio);
    }

    private void ValidateRequest(ExportRequest request)
    {
        if (!File.Exists(paths.FfmpegPath) || !File.Exists(paths.FfprobePath))
            throw new FileNotFoundException("FFmpeg no está disponible. Ejecuta build-portable.ps1 para preparar la distribución.");
        if (!File.Exists(request.Source.Path)) throw new FileNotFoundException("No se encuentra el vídeo seleccionado.", request.Source.Path);
        if (request.Width <= 0 || request.Height <= 0 || request.Fps <= 0 || request.VideoBitrateKbps <= 0)
            throw new InvalidDataException("Revisa la resolución, los FPS y el bitrate de salida.");
        if (request.AddIntro && !VideoRules.IsIntroAvailable(request.Format))
            throw new InvalidDataException("La cortinilla inicial solo está disponible en formato 16:9.");
        if (request.AddWatermark && (string.IsNullOrWhiteSpace(request.WatermarkPath) || !File.Exists(request.WatermarkPath)))
            throw new FileNotFoundException("No se encuentra el logo seleccionado.", request.WatermarkPath);
        if (request.AddTextWatermark && VideoRules.TextWatermarkHasContent(request.TextWatermark))
        {
            if (string.IsNullOrWhiteSpace(request.TextWatermarkFontPath) || !File.Exists(request.TextWatermarkFontPath))
                throw new FileNotFoundException("No se encuentra la fuente de la mosca de texto.", request.TextWatermarkFontPath);
            if (request.AddWatermark && request.WatermarkPosition == request.TextWatermarkPosition)
                throw new InvalidDataException("Las dos moscas no pueden ocupar la misma esquina.");
        }
        if (request.TrimEndSeconds is double trimEnd)
        {
            if (request.TrimStartSeconds < -0.001 || trimEnd <= request.TrimStartSeconds)
                throw new InvalidDataException("El recorte del vídeo no es válido.");
            if (trimEnd > request.Source.DurationSeconds + 0.05)
                throw new InvalidDataException("El recorte sobrepasa la duración del vídeo.");
            var minimum = Math.Min(VideoTrim.MinimumKeptSeconds, request.Source.DurationSeconds);
            if (trimEnd - request.TrimStartSeconds < minimum - 0.05)
                throw new InvalidDataException("El vídeo resultante es demasiado corto.");
        }
        if (File.Exists(request.OutputPath)) throw new IOException("Ya existe un archivo con ese nombre en la carpeta de destino.");
    }

    private (ExportRequest Request, string? TextFile) PrepareTextWatermark(ExportRequest request)
    {
        if (!request.AddTextWatermark || !VideoRules.TextWatermarkHasContent(request.TextWatermark))
            return (request with { AddTextWatermark = false }, null);

        var text = VideoRules.NormalizeTextWatermark(request.TextWatermark);
        var textFile = Path.Combine(paths.TempDirectory, $"mosca_{Guid.NewGuid():N}.txt");
        File.WriteAllText(textFile, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return (request with { TextWatermark = text, TextWatermarkFilePath = textFile }, textFile);
    }

    private static (double Kept, double Start, double? End) ResolveTrim(ExportRequest request)
    {
        if (request.TrimEndSeconds is not double end)
            return (request.Source.DurationSeconds, 0, null);
        var start = Math.Clamp(request.TrimStartSeconds, 0, request.Source.DurationSeconds);
        end = Math.Clamp(end, start, request.Source.DurationSeconds);
        if (!VideoTrim.IsActive(start, end, request.Source.DurationSeconds))
            return (request.Source.DurationSeconds, 0, null);
        return (Math.Max(0.01, end - start), start, end);
    }

    private static void EnsureDiskSpace(string outputPath, long estimatedBytes)
    {
        var destinationRoot = Path.GetPathRoot(Path.GetFullPath(outputPath));
        if (string.IsNullOrWhiteSpace(destinationRoot)) return;
        var drive = new DriveInfo(destinationRoot);
        if (drive.IsReady && drive.AvailableFreeSpace < estimatedBytes * 1.15)
            throw new IOException("No hay espacio suficiente en la unidad de destino.");
    }

    private static async Task WriteLogAsync(string logPath, ExportRequest request, string? command, string output, string? error)
    {
        try
        {
            var log = new StringBuilder()
                .AppendLine($"Fecha: {DateTimeOffset.Now:O}")
                .AppendLine($"Archivo: {request.Source.Path}")
                .AppendLine("Configuración:")
                .AppendLine(JsonSerializer.Serialize(request, new JsonSerializerOptions { WriteIndented = true }))
                .AppendLine("Comando FFmpeg:")
                .AppendLine(command ?? "No iniciado")
                .AppendLine("Salida FFmpeg:")
                .AppendLine(output)
                .AppendLine("Error:")
                .AppendLine(error ?? "Ninguno");
            await File.WriteAllTextAsync(logPath, log.ToString(), Encoding.UTF8);
        }
        catch (IOException) { }
    }
}