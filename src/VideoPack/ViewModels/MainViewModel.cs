using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Media.Imaging;
using VideoPack.FFmpeg;
using VideoPack.Infrastructure;
using VideoPack.Models;
using VideoPack.Services;

namespace VideoPack.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppPaths _paths;
    private readonly PresetService _presetService;
    private readonly FfprobeService _probe;
    private readonly ExportService _exportService;
    private CancellationTokenSource? _exportCancellation;
    private VideoMetadata? _video;
    private LogoOption? _selectedLogo;
    private OutputFormat _format = OutputFormat.Landscape;
    private FramingMode _framing = FramingMode.Fit;
    private QualityPreset _preset = QualityPreset.Youtube;
    private WatermarkPosition _watermarkPosition = WatermarkPosition.TopRight;
    private bool _addIntro = true;
    private bool _addOutro = true;
    private bool _watermarkEnabled = true;
    private bool _isBusy;
    private bool _isComplete;
    private double _progress;
    private string _statusMessage = "Selecciona o arrastra tu vídeo aquí";
    private string _errorMessage = string.Empty;
    private string _errorDetails = string.Empty;
    private string? _lastLogPath;
    private string _outputFolder = string.Empty;
    private string _outputFileName = string.Empty;
    private string _videoBitrate = "5000";
    private string _outputWidth = "1920";
    private string _outputHeight = "1080";
    private string _fps = "25";
    private string _videoCodec = "libx264";
    private string _audioCodec = "aac";
    private string _audioBitrate = "192";
    private double _introDuration;
    private double _outroLandscapeDuration;
    private double _outroPortraitDuration;
    private BitmapImage? _thumbnailImage;
    private bool _applyingPreset;
    private bool _isAnalyzing;
    private bool _isExporting;
    private bool _isCancelled;
    private string _previewErrorMessage = string.Empty;
    private ErrorArea _errorArea;

    private enum ErrorArea { None, Load, Export }

    public ObservableCollection<UiOption> Formats { get; } =
    [
        new(OutputFormat.Landscape, "Horizontal", "16:9", "landscape", "YouTube, web y pantallas"),
        new(OutputFormat.Portrait, "Vertical", "9:16", "portrait", "Reels, TikTok y Stories"),
        new(OutputFormat.Square, "Cuadrado", "1:1", "square", "Publicaciones en el feed")
    ];
    public ObservableCollection<UiOption> Framings { get; } =
    [
        new(FramingMode.Fit, "Encuadrar", "Conserva todo el vídeo", "fit"),
        new(FramingMode.Crop, "Recortar", "Llena el encuadre", "crop")
    ];
    public ObservableCollection<UiOption> Presets { get; } =
    [
        new(QualityPreset.Master, "Master", "Máxima calidad para archivar", "\uE734", "4K · 16 Mbps"),
        new(QualityPreset.Youtube, "YouTube", "Publicar en YouTube o en la web", "\uE768", "1080p · 5 Mbps"),
        new(QualityPreset.Social, "Redes", "Vertical para móvil y redes sociales", "\uE8EA", "720p · 2 Mbps"),
        new(QualityPreset.Custom, "Personalizado", "Ajusta tú cada parámetro", "\uE9E9", "A tu medida")
    ];
    public ObservableCollection<UiOption> Positions { get; } =
    [
        new(WatermarkPosition.TopLeft, "Superior izquierda", "", "↖"),
        new(WatermarkPosition.TopRight, "Superior derecha", "", "↗"),
        new(WatermarkPosition.BottomLeft, "Inferior izquierda", "", "↙"),
        new(WatermarkPosition.BottomRight, "Inferior derecha", "", "↘")
    ];
    public ObservableCollection<LogoOption> Logos { get; } = [];
    public ObservableCollection<SummaryItem> Summary { get; } = [];

    public RelayCommand SelectFormatCommand { get; }
    public RelayCommand SelectFramingCommand { get; }
    public RelayCommand SelectPresetCommand { get; }
    public RelayCommand SelectPositionCommand { get; }
    public RelayCommand CancelExportCommand { get; }
    public RelayCommand OpenOutputCommand { get; }
    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand NewPreparationCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }

    public MainViewModel()
    {
        _paths = new AppPaths();
        _presetService = new PresetService(_paths);
        _probe = new FfprobeService(_paths.FfprobePath);
        _exportService = new ExportService(_paths, _presetService);
        SelectFormatCommand = new RelayCommand(value => SelectFormat((OutputFormat)value!));
        SelectFramingCommand = new RelayCommand(value => SelectFraming((FramingMode)value!));
        SelectPresetCommand = new RelayCommand(value => SelectPreset((QualityPreset)value!));
        SelectPositionCommand = new RelayCommand(value => SelectPosition((WatermarkPosition)value!));
        CancelExportCommand = new RelayCommand(_ => _exportCancellation?.Cancel(), _ => IsBusy);
        OpenOutputCommand = new RelayCommand(_ => OpenPath(OutputPath), _ => IsComplete && File.Exists(OutputPath));
        OpenFolderCommand = new RelayCommand(_ => OpenPath(OutputFolder), _ => IsComplete && Directory.Exists(OutputFolder));
        NewPreparationCommand = new RelayCommand(_ => Reset(), _ => IsComplete || Video is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, CanExport);
        RefreshSelections();
    }

    public async Task InitializeAsync()
    {
        await _presetService.LoadAsync();
        Logos.Clear();
        foreach (var logo in _presetService.Configuration.Logos)
            Logos.Add(new LogoOption(logo.Name, logo.FileName, _paths.ResolveAsset(Path.Combine("assets", "watermarks", logo.FileName))));
        SelectedLogo = Logos.FirstOrDefault(x => x.FileName == "tecnalia_white_color.png") ?? Logos.FirstOrDefault();
        ApplyPreset(QualityPreset.Youtube, markCustom: false);
        await LoadBumperDurationsAsync();
        RefreshSummary();
    }

    public VideoMetadata? Video { get => _video; private set { if (SetProperty(ref _video, value)) { OnPropertyChanged(nameof(HasVideo)); OnPropertyChanged(nameof(VideoTechnicalDetails)); OnPropertyChanged(nameof(PreviewPath)); OnPropertyChanged(nameof(OutputPath)); RefreshSummary(); ExportCommand.RaiseCanExecuteChanged(); } } }
    public bool HasVideo => Video is not null;
    public bool ShowIntroInTimeline => Format == OutputFormat.Landscape && AddIntro;
    public bool ShowOutroInTimeline => AddOutro;
    public bool ShowVideoInTimeline => Video is not null;
    public bool IsCustomPreset => Preset == QualityPreset.Custom;
    public BitmapImage? ThumbnailImage { get => _thumbnailImage; private set => SetProperty(ref _thumbnailImage, value); }
    public string VideoName => Video is null ? "" : Path.GetFileName(Video.Path);
    public string VideoTechnicalDetails => Video is null ? "" : $"{Video.Resolution}  ·  {Video.AspectRatio}  ·  {Video.FramesPerSecond:0.##} fps  ·  {Video.VideoCodec}  ·  {Video.FileSizeLabel}  ·  {Video.DurationLabel}";
    public string PreviewPath => Video?.Path ?? "";
    public string SelectedLogoPath => SelectedLogo?.ImagePath ?? "";
    public string OutputPath => string.IsNullOrWhiteSpace(OutputFolder) || string.IsNullOrWhiteSpace(OutputFileName) ? "" : Path.Combine(OutputFolder, OutputFileName);
    public string FormatLabel => Format switch { OutputFormat.Portrait => "9:16 (Vertical)", OutputFormat.Square => "1:1 (Cuadrado)", _ => "16:9 (Horizontal)" };
    public string FramingLabel => Framing == FramingMode.Fit ? "Encuadrar" : "Recortar";
    public string PresetLabel => Preset switch { QualityPreset.Master => "master", QualityPreset.Youtube => "youtube", QualityPreset.Social => "redes", _ => "personalizado" };
    public bool ShowIntroOption => VideoRules.IsIntroAvailable(Format);
    public string IntroAvailabilityMessage => ShowIntroOption ? "" : "Cortinilla inicial disponible solo en formato 16:9.";
    public string OutputResolution => $"{OutputWidth} × {OutputHeight}";
    public string TotalDurationLabel => TimeSpan.FromSeconds(TotalDurationSeconds).ToString("m\\:ss");
    public double TotalDurationSeconds => (Video?.DurationSeconds ?? 0)
        + (Format == OutputFormat.Landscape && AddIntro ? _introDuration : 0)
        + (AddOutro ? Format == OutputFormat.Landscape ? _outroLandscapeDuration : _outroPortraitDuration : 0);
    public string EstimatedSizeLabel => $"~ {PresetService.EstimateBytes(TotalDurationSeconds, Parse(VideoBitrate), Parse(AudioBitrate)) / 1_000_000d:0} MB (estimación)";
    public string PreviewAspectName => Format switch { OutputFormat.Portrait => "9:16", OutputFormat.Square => "1:1", _ => "16:9" };
    public double PreviewFrameWidth => Format == OutputFormat.Portrait ? 190 : 380;
    public double PreviewFrameHeight => Format == OutputFormat.Landscape ? 214 : Format == OutputFormat.Portrait ? 338 : 380;
    public double PreviewWatermarkWidth => PreviewFrameWidth * _presetService.Configuration.WatermarkWidthRatio;
    public double PreviewSafeMargin => Math.Min(PreviewFrameWidth, PreviewFrameHeight) * _presetService.Configuration.SafeMarginRatio;
    public System.Windows.Media.Stretch PreviewStretch => Framing == FramingMode.Crop ? System.Windows.Media.Stretch.UniformToFill : System.Windows.Media.Stretch.Uniform;

    public bool IsIdle => !IsBusy;
    public bool IsAnalyzing { get => _isAnalyzing; private set => SetProperty(ref _isAnalyzing, value); }
    public bool IsExporting { get => _isExporting; private set => SetProperty(ref _isExporting, value); }
    public bool IsCancelled { get => _isCancelled; private set => SetProperty(ref _isCancelled, value); }
    public bool CanStartExport => !IsExporting && !IsComplete;
    public bool ShowDestination => HasVideo && !IsComplete;
    public bool HasLoadError => _errorArea == ErrorArea.Load && !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasExportError => _errorArea == ErrorArea.Export && !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasErrorDetails => !string.IsNullOrWhiteSpace(ErrorDetails);
    public string PreviewErrorMessage { get => _previewErrorMessage; private set => SetProperty(ref _previewErrorMessage, value); }

    public string PresetDisplayName => Preset switch { QualityPreset.Master => "Master", QualityPreset.Youtube => "YouTube", QualityPreset.Social => "Redes", _ => "Personalizado" };
    public string FormatDisplayName => Format switch { OutputFormat.Portrait => "Vertical", OutputFormat.Square => "Cuadrado", _ => "Horizontal" };
    public string SummaryHeadline => $"{PresetDisplayName}  ·  {FormatDisplayName} {PreviewAspectName}";
    public string SummaryFacts
    {
        get
        {
            var facts = new List<string> { $"{Parse(OutputWidth)} × {Parse(OutputHeight)}" };
            if (Video is not null)
            {
                facts.Add(TotalDurationLabel);
                facts.Add($"≈ {PresetService.EstimateBytes(TotalDurationSeconds, Parse(VideoBitrate), Parse(AudioBitrate)) / 1_000_000d:0} MB");
            }
            return string.Join("   ·   ", facts);
        }
    }
    public string BumpersSummary => (ShowIntroInTimeline, AddOutro) switch
    {
        (true, true) => "Cortinilla inicial y final",
        (true, false) => "Solo cortinilla inicial",
        (false, true) => "Solo cortinilla final",
        _ => "Sin cortinillas"
    };
    public bool HasAnyBumper => ShowIntroInTimeline || AddOutro;
    public string WatermarkSummary => WatermarkEnabled ? $"Mosca {SelectedLogo?.Name ?? ""}, {PositionLabel(WatermarkPosition).ToLowerInvariant()}" : "Sin mosca";
    public string WatermarkPositionLabel => PositionLabel(WatermarkPosition);
    public string IntroStatusText => ShowIntroOption ? "Presentación de marca al empezar" : "Solo disponible en formato horizontal 16:9";
    public string OutroStatusText => "Cierre de marca, adaptado al formato";

    public string VideoPrimaryDetails => Video is null ? "" : $"{Video.Width} × {Video.Height}   ·   {FriendlyAspect(Video.Width, Video.Height)}   ·   {Video.DurationLabel}";
    public string VideoSecondaryDetails => Video is null ? "" : $"{Video.FramesPerSecond:0.##} fps  ·  {CodecName(Video.VideoCodec)}  ·  {Video.FileSizeLabel}{(Video.HasAudio ? "" : "  ·  Sin audio")}";
    public string VideoDurationLabel => Video?.DurationLabel ?? "";

    private double OutputAspect => Format switch { OutputFormat.Portrait => 9d / 16, OutputFormat.Square => 1d, _ => 16d / 9 };
    private double SourceAspect => Video is { Width: > 0, Height: > 0 } video ? (double)video.Width / video.Height : Format == OutputFormat.Landscape ? 9d / 16 : 16d / 9;
    public bool FramingIsRelevant => Math.Abs(SourceAspect - OutputAspect) > 0.02;
    public string FramingHint => Video is null
        ? "Cómo adaptar un vídeo con otra proporción al formato elegido."
        : FramingIsRelevant
            ? $"Tu vídeo es {FriendlyAspect(Video.Width, Video.Height)} y la salida {PreviewAspectName}. Elige cómo adaptarlo."
            : $"Tu vídeo ya es {PreviewAspectName}: se verá completo, sin bandas ni recortes.";
    public string FitDescription => !FramingIsRelevant ? "Se ve todo el vídeo"
        : SourceAspect > OutputAspect ? "Mismo ancho. Se ve todo, con bandas arriba y abajo." : "Mismo alto. Se ve todo, con bandas a los lados.";
    public string CropDescription => !FramingIsRelevant ? "Llena todo el marco"
        : SourceAspect > OutputAspect ? "Mismo alto. Llena el marco, se pierden los laterales." : "Mismo ancho. Llena el marco, se pierde arriba y abajo.";
    public double FramingFrameWidth => Format switch { OutputFormat.Portrait => 36, OutputFormat.Square => 50, _ => 72 };
    public double FramingFrameHeight => Format switch { OutputFormat.Portrait => 64, OutputFormat.Square => 50, _ => 40.5 };
    public double FitContentWidth => SourceAspect >= OutputAspect ? FramingFrameWidth : FramingFrameHeight * SourceAspect;
    public double FitContentHeight => SourceAspect >= OutputAspect ? FramingFrameWidth / SourceAspect : FramingFrameHeight;
    public double CropContentWidth => SourceAspect >= OutputAspect ? FramingFrameHeight * SourceAspect : FramingFrameWidth;
    public double CropContentHeight => SourceAspect >= OutputAspect ? FramingFrameHeight : FramingFrameWidth / SourceAspect;
    public double PositionFrameWidth => Format switch { OutputFormat.Portrait => 66, OutputFormat.Square => 104, _ => 168 };
    public double PositionFrameHeight => Format switch { OutputFormat.Portrait => 117, OutputFormat.Square => 104, _ => 94.5 };

    public LogoOption? SelectedLogo
    {
        get => _selectedLogo;
        set { if (SetProperty(ref _selectedLogo, value)) { OnPropertyChanged(nameof(SelectedLogoPath)); RefreshSummary(); } }
    }
    public OutputFormat Format { get => _format; private set => SetProperty(ref _format, value); }
    public FramingMode Framing { get => _framing; private set => SetProperty(ref _framing, value); }
    public QualityPreset Preset { get => _preset; private set { if (SetProperty(ref _preset, value)) { OnPropertyChanged(nameof(IsCustomPreset)); OnPropertyChanged(nameof(PresetDisplayName)); OnPropertyChanged(nameof(SummaryHeadline)); } } }
    public bool AddIntro { get => _addIntro; set { if (SetProperty(ref _addIntro, value)) { OnPropertyChanged(nameof(ShowIntroInTimeline)); RefreshSummary(); } } }
    public bool AddOutro { get => _addOutro; set { if (SetProperty(ref _addOutro, value)) { OnPropertyChanged(nameof(ShowOutroInTimeline)); RefreshSummary(); } } }
    public bool WatermarkEnabled { get => _watermarkEnabled; set { if (SetProperty(ref _watermarkEnabled, value)) RefreshSummary(); } }
    public WatermarkPosition WatermarkPosition { get => _watermarkPosition; private set => SetProperty(ref _watermarkPosition, value); }
    public string OutputFolder { get => _outputFolder; set { if (SetProperty(ref _outputFolder, value)) { ClearExportError(); OnPropertyChanged(nameof(OutputPath)); ExportCommand.RaiseCanExecuteChanged(); } } }
    public string OutputFileName { get => _outputFileName; set { if (SetProperty(ref _outputFileName, value)) { ClearExportError(); OnPropertyChanged(nameof(OutputPath)); ExportCommand.RaiseCanExecuteChanged(); } } }
    public string VideoBitrate { get => _videoBitrate; set { if (SetProperty(ref _videoBitrate, value)) ParametersChanged(); } }
    public string OutputWidth { get => _outputWidth; set { if (SetProperty(ref _outputWidth, value)) ParametersChanged(); } }
    public string OutputHeight { get => _outputHeight; set { if (SetProperty(ref _outputHeight, value)) ParametersChanged(); } }
    public string Fps { get => _fps; set { if (SetProperty(ref _fps, value)) ParametersChanged(); } }
    public string VideoCodec { get => _videoCodec; set { if (SetProperty(ref _videoCodec, value)) ParametersChanged(); } }
    public string AudioCodec { get => _audioCodec; set { if (SetProperty(ref _audioCodec, value)) ParametersChanged(); } }
    public string AudioBitrate { get => _audioBitrate; set { if (SetProperty(ref _audioBitrate, value)) ParametersChanged(); } }
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(IsIdle)); CancelExportCommand.RaiseCanExecuteChanged(); ExportCommand.RaiseCanExecuteChanged(); } } }
    public bool IsComplete { get => _isComplete; private set { if (SetProperty(ref _isComplete, value)) { OnPropertyChanged(nameof(CanStartExport)); OnPropertyChanged(nameof(ShowDestination)); OpenOutputCommand.RaiseCanExecuteChanged(); OpenFolderCommand.RaiseCanExecuteChanged(); NewPreparationCommand.RaiseCanExecuteChanged(); } } }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) { OnPropertyChanged(nameof(HasLoadError)); OnPropertyChanged(nameof(HasExportError)); } } }
    public string ErrorDetails { get => _errorDetails; private set { if (SetProperty(ref _errorDetails, value)) OnPropertyChanged(nameof(HasErrorDetails)); } }
    public string? LastLogPath { get => _lastLogPath; private set => SetProperty(ref _lastLogPath, value); }

    public async Task LoadVideoAsync(string path)
    {
        ErrorMessage = ErrorDetails = string.Empty;
        PreviewErrorMessage = string.Empty;
        IsCancelled = false;
        IsBusy = true;
        IsAnalyzing = true;
        StatusMessage = "Analizando vídeo…";
        try
        {
            Video = await _probe.ProbeAsync(path);
            ThumbnailImage = await GenerateThumbnailAsync(path);
            OutputFolder = Path.GetDirectoryName(path) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            UpdateOutputName();
            StatusMessage = "Vídeo listo para preparar";
            IsComplete = false;
        }
        catch (Exception exception)
        {
            Video = null;
            ThumbnailImage = null;
            StatusMessage = "No se ha podido analizar el vídeo";
            SetError(ErrorArea.Load, "No se ha podido leer este vídeo. Comprueba que el archivo sea un vídeo válido y que FFmpeg esté preparado.", exception.Message);
        }
        finally { IsAnalyzing = false; IsBusy = false; }
    }

    public void SetPreviewError(string message)
    {
        if (string.IsNullOrWhiteSpace(PreviewErrorMessage)) PreviewErrorMessage = message;
    }

    private void SetError(ErrorArea area, string message, string details = "")
    {
        _errorArea = area;
        ErrorDetails = details;
        if (ErrorMessage == message) { OnPropertyChanged(nameof(HasLoadError)); OnPropertyChanged(nameof(HasExportError)); }
        ErrorMessage = message;
    }

    private void ClearExportError()
    {
        if (_errorArea == ErrorArea.Export && !IsBusy) ErrorMessage = ErrorDetails = string.Empty;
    }

    private void SelectFormat(OutputFormat format)
    {
        if (Format == format) return;
        Format = format;
        if (format != OutputFormat.Landscape) AddIntro = false;
        RecalculateResolution();
        MarkCustom();
        RefreshSelections();
        OnPropertyChanged(nameof(ShowIntroOption));
        OnPropertyChanged(nameof(IntroAvailabilityMessage));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(PreviewAspectName));
        OnPropertyChanged(nameof(PreviewFrameWidth));
        OnPropertyChanged(nameof(PreviewFrameHeight));
        OnPropertyChanged(nameof(PreviewWatermarkWidth));
        OnPropertyChanged(nameof(PreviewSafeMargin));
        RefreshSummary();
    }

    private void SelectFraming(FramingMode framing)
    {
        Framing = framing;
        MarkCustom();
        RefreshSelections();
        OnPropertyChanged(nameof(FramingLabel));
        OnPropertyChanged(nameof(PreviewStretch));
        RefreshSummary();
    }

    private void SelectPreset(QualityPreset preset)
    {
        if (preset == QualityPreset.Custom) { Preset = preset; RefreshSelections(); UpdateOutputName(); return; }
        ApplyPreset(preset, markCustom: false);
        IsComplete = false;
        UpdateOutputName();
    }

    private void SelectPosition(WatermarkPosition position)
    {
        WatermarkPosition = position;
        RefreshSelections();
        RefreshSummary();
    }

    private void ApplyPreset(QualityPreset preset, bool markCustom)
    {
        var definition = _presetService.Get(preset);
        if (definition is null) return;
        _applyingPreset = true;
        Preset = preset;
        Format = Enum.Parse<OutputFormat>(definition.Format, true);
        Framing = Enum.Parse<FramingMode>(definition.Framing, true);
        VideoBitrate = definition.VideoBitrateKbps.ToString(CultureInfo.InvariantCulture);
        Fps = definition.Fps.ToString(CultureInfo.InvariantCulture);
        VideoCodec = definition.VideoCodec;
        AudioCodec = definition.AudioCodec;
        AudioBitrate = definition.AudioBitrateKbps.ToString(CultureInfo.InvariantCulture);
        var size = PresetService.GetResolution(definition.Height, Format);
        OutputWidth = size.Width.ToString(CultureInfo.InvariantCulture);
        OutputHeight = size.Height.ToString(CultureInfo.InvariantCulture);
        if (Format != OutputFormat.Landscape) AddIntro = false;
        _applyingPreset = false;
        if (markCustom) MarkCustom();
        RefreshSelections();
        OnPropertyChanged(nameof(ShowIntroOption));
        OnPropertyChanged(nameof(IntroAvailabilityMessage));
        OnPropertyChanged(nameof(FormatLabel));
        OnPropertyChanged(nameof(FramingLabel));
        OnPropertyChanged(nameof(PreviewAspectName));
        OnPropertyChanged(nameof(PreviewFrameWidth));
        OnPropertyChanged(nameof(PreviewFrameHeight));
        OnPropertyChanged(nameof(PreviewWatermarkWidth));
        OnPropertyChanged(nameof(PreviewSafeMargin));
        OnPropertyChanged(nameof(PreviewStretch));
        OnPropertyChanged(nameof(OutputResolution));
        RefreshSummary();
    }

    private void RecalculateResolution()
    {
        var longEdge = Math.Max(Parse(OutputWidth), Parse(OutputHeight));
        var size = PresetService.GetResolution(longEdge, Format);
        _applyingPreset = true;
        OutputWidth = size.Width.ToString(CultureInfo.InvariantCulture);
        OutputHeight = size.Height.ToString(CultureInfo.InvariantCulture);
        _applyingPreset = false;
        OnPropertyChanged(nameof(OutputResolution));
    }

    private void ParametersChanged()
    {
        OnPropertyChanged(nameof(OutputResolution));
        OnPropertyChanged(nameof(EstimatedSizeLabel));
        if (!_applyingPreset) MarkCustom();
        RefreshSummary();
        ExportCommand.RaiseCanExecuteChanged();
    }

    private void MarkCustom()
    {
        if (_applyingPreset) return;
        Preset = VideoRules.AfterManualChange();
        UpdateOutputName();
        RefreshSelections();
    }

    private void RefreshSelections()
    {
        foreach (var option in Formats) option.IsSelected = Equals(option.Value, Format);
        foreach (var option in Framings) option.IsSelected = Equals(option.Value, Framing);
        foreach (var option in Presets) option.IsSelected = Equals(option.Value, Preset);
        foreach (var option in Positions) option.IsSelected = Equals(option.Value, WatermarkPosition);
    }

    private void UpdateOutputName()
    {
        if (Video is null) return;
        var stem = Path.GetFileNameWithoutExtension(Video.Path);
        var suffix = Preset switch { QualityPreset.Master => "master", QualityPreset.Youtube => "youtube", QualityPreset.Social => "redes", _ => "personalizado" };
        var candidate = $"{stem}_final_{suffix}.mp4";
        var number = 2;
        while (!string.IsNullOrWhiteSpace(OutputFolder) && File.Exists(Path.Combine(OutputFolder, candidate)))
            candidate = $"{stem}_final_{suffix}_{number++}.mp4";
        OutputFileName = candidate;
    }

    private async Task LoadBumperDurationsAsync()
    {
        if (!File.Exists(_paths.FfprobePath)) return;
        var intro = _paths.ResolveAsset(_presetService.Configuration.IntroLandscape);
        var landscapeOutro = _paths.ResolveAsset(_presetService.Configuration.OutroLandscape);
        var portraitOutro = _paths.ResolveAsset(_presetService.Configuration.OutroPortrait);
        if (File.Exists(intro)) try { _introDuration = (await _probe.ProbeAsync(intro)).DurationSeconds; } catch (Exception) { }
        if (File.Exists(landscapeOutro)) try { _outroLandscapeDuration = (await _probe.ProbeAsync(landscapeOutro)).DurationSeconds; } catch (Exception) { }
        if (File.Exists(portraitOutro)) try { _outroPortraitDuration = (await _probe.ProbeAsync(portraitOutro)).DurationSeconds; } catch (Exception) { }
        OnPropertyChanged(nameof(TotalDurationSeconds));
        OnPropertyChanged(nameof(TotalDurationLabel));
        RefreshSummary();
    }

    private async Task<BitmapImage?> GenerateThumbnailAsync(string videoPath)
    {
        if (!File.Exists(_paths.FfmpegPath)) return null;
        Directory.CreateDirectory(_paths.TempDirectory);
        var thumbnail = Path.Combine(_paths.TempDirectory, $"preview_{Guid.NewGuid():N}.jpg");
        var startInfo = new ProcessStartInfo(_paths.FfmpegPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-ss", "0.5", "-i", videoPath, "-frames:v", "1", "-vf", "scale=320:180:force_original_aspect_ratio=decrease,pad=320:180:(ow-iw)/2:(oh-ih)/2", "-y", thumbnail })
            startInfo.ArgumentList.Add(argument);
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            await process.WaitForExitAsync();
            if (process.ExitCode != 0 || !File.Exists(thumbnail)) return null;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(thumbnail, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception) { return null; }
        finally
        {
            if (File.Exists(thumbnail))
                try { File.Delete(thumbnail); } catch (IOException) { }
        }
    }

    private void RefreshSummary()
    {
        // Changing any setting after a finished export prepares a new, non-overwriting export.
        if (IsComplete && !IsBusy && Video is not null) { IsComplete = false; UpdateOutputName(); }
        Summary.Clear();
        Summary.Add(new("Ajuste al formato", FramingLabel));
        Summary.Add(new("Códec de vídeo", CodecName(VideoCodec)));
        Summary.Add(new("Bitrate de vídeo", $"{Parse(VideoBitrate) / 1000d:0.#} Mbps  ({Parse(VideoBitrate)} kbps)"));
        Summary.Add(new("Fotogramas por segundo", Fps));
        Summary.Add(new("Audio", $"{(AudioCodec == "libmp3lame" ? "MP3" : AudioCodec.ToUpperInvariant())} estéreo  ·  {AudioBitrate} kbps"));
        if (Video is not null) Summary.Add(new("Tamaño estimado", EstimatedSizeLabel));
        OnPropertyChanged(nameof(EstimatedSizeLabel));
        OnPropertyChanged(nameof(TotalDurationLabel));
        foreach (var name in DisplayProperties) OnPropertyChanged(name);
    }

    private static readonly string[] DisplayProperties =
    [
        nameof(PresetDisplayName), nameof(FormatDisplayName), nameof(SummaryHeadline), nameof(SummaryFacts),
        nameof(BumpersSummary), nameof(HasAnyBumper), nameof(WatermarkSummary), nameof(WatermarkPositionLabel),
        nameof(IntroStatusText), nameof(VideoName), nameof(VideoPrimaryDetails), nameof(VideoSecondaryDetails), nameof(VideoDurationLabel),
        nameof(FramingIsRelevant), nameof(FramingHint), nameof(FitDescription), nameof(CropDescription),
        nameof(FramingFrameWidth), nameof(FramingFrameHeight), nameof(FitContentWidth), nameof(FitContentHeight),
        nameof(CropContentWidth), nameof(CropContentHeight), nameof(PositionFrameWidth), nameof(PositionFrameHeight),
        nameof(ShowVideoInTimeline), nameof(ShowIntroInTimeline), nameof(ShowOutroInTimeline), nameof(ShowDestination)
    ];

    private static string CodecName(string codec) => codec.ToLowerInvariant() switch
    {
        "libx264" or "h264" => "H.264",
        "libx265" or "hevc" or "h265" => "H.265 (HEVC)",
        _ => codec.ToUpperInvariant()
    };

    private static string FriendlyAspect(int width, int height)
    {
        if (width <= 0 || height <= 0) return "desconocido";
        var ratio = (double)width / height;
        foreach (var (label, value) in new[] { ("16:9", 16d / 9), ("9:16", 9d / 16), ("1:1", 1d), ("4:3", 4d / 3), ("3:4", 3d / 4), ("4:5", 4d / 5), ("21:9", 21d / 9) })
            if (Math.Abs(ratio - value) < 0.02) return label;
        return ratio > 1 ? "horizontal" : "vertical";
    }

    private static string PositionLabel(WatermarkPosition position) => position switch
    {
        WatermarkPosition.TopLeft => "Superior izquierda",
        WatermarkPosition.TopRight => "Superior derecha",
        WatermarkPosition.BottomLeft => "Inferior izquierda",
        _ => "Inferior derecha"
    };

    private bool CanExport() => Video is not null && !IsBusy && !string.IsNullOrWhiteSpace(OutputPath)
        && int.TryParse(VideoBitrate, out var bitrate) && bitrate > 0
        && int.TryParse(OutputWidth, out var width) && width > 0
        && int.TryParse(OutputHeight, out var height) && height > 0
        && int.TryParse(Fps, out var fps) && fps > 0;

    private async Task ExportAsync()
    {
        if (!CanExport() || Video is null) return;
        IsCancelled = false;
        if (File.Exists(OutputPath))
        {
            SetError(ErrorArea.Export, "Ya existe un archivo con ese nombre en la carpeta de destino. Cambia el nombre antes de exportar.");
            return;
        }
        ErrorMessage = ErrorDetails = string.Empty;
        IsBusy = true;
        IsExporting = true;
        OnPropertyChanged(nameof(CanStartExport));
        IsComplete = false;
        Progress = 0;
        StatusMessage = "Preparando vídeo…";
        _exportCancellation = new CancellationTokenSource();
        var progress = new Progress<double>(value => { Progress = value; StatusMessage = value < 99 ? "Codificando vídeo…" : "Finalizando…"; });
        try
        {
            var request = new ExportRequest(Video, Format, Framing, Parse(OutputWidth), Parse(OutputHeight), Parse(Fps),
                Parse(VideoBitrate), Parse(AudioBitrate), VideoCodec, AudioCodec,
                Format == OutputFormat.Landscape && AddIntro, AddOutro, WatermarkEnabled,
                SelectedLogoPath, WatermarkPosition, _presetService.Configuration.WatermarkWidthRatio,
                _presetService.Configuration.SafeMarginRatio, OutputPath);
            var result = await _exportService.ExportAsync(request, progress, _exportCancellation.Token);
            StatusMessage = "Vídeo preparado correctamente";
            Progress = 100;
            IsComplete = true;
            OnPropertyChanged(nameof(CompletionDetails));
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Exportación cancelada";
            Progress = 0;
            IsCancelled = true;
        }
        catch (ExportException exception)
        {
            StatusMessage = "No se ha podido exportar el vídeo";
            LastLogPath = exception.LogPath;
            SetError(ErrorArea.Export, exception.Message, exception.Details);
        }
        catch (Exception exception)
        {
            StatusMessage = "No se ha podido exportar el vídeo";
            SetError(ErrorArea.Export, "Se ha producido un error al preparar el vídeo.", exception.Message);
        }
        finally
        {
            _exportCancellation.Dispose();
            _exportCancellation = null;
            IsExporting = false;
            IsBusy = false;
            OnPropertyChanged(nameof(CanStartExport));
            NewPreparationCommand.RaiseCanExecuteChanged();
        }
    }

    public string CompletionDetails => File.Exists(OutputPath) ? $"{new FileInfo(OutputPath).Length / 1_000_000d:0} MB  ·  {TotalDurationLabel}" : "";

    private void Reset()
    {
        Video = null;
        ThumbnailImage = null;
        IsComplete = false;
        IsCancelled = false;
        ErrorMessage = ErrorDetails = string.Empty;
        PreviewErrorMessage = string.Empty;
        Progress = 0;
        StatusMessage = "Selecciona o arrastra tu vídeo aquí";
        OutputFileName = string.Empty;
    }

    private static int Parse(string value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    private static void OpenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}