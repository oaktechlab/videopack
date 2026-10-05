using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoPack.Infrastructure;
using VideoPack.Models;
using VideoPack.Services;
using VideoPack.ViewModels;

namespace VideoPack;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly PreviewMediaBinder _preview;
    private int _seenRevision;
    private bool _previewFailed;
    private TrimDrag _trimDrag;

    private enum TrimDrag { None, Start, End }

    public MainWindow()
    {
        InitializeComponent();
        Title = $"VideoPack {AppVersion.Label}";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height * 0.94));
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _preview = new PreviewMediaBinder(PreviewMedia);
        _preview.Volume = VolumeSlider.Value;
        _preview.Changed += UpdateTransportChrome;
        _preview.Failed += message =>
        {
            _previewFailed = true;
            _viewModel.SetPreviewError(message);
            UpdateTransportChrome();
        };
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        PreviewMedia.Stretch = _viewModel.PreviewStretch;
        _preview.Volume = VolumeSlider.Value;
        UpdateWatermarkPreview();
        UpdateTransportChrome();
        LayoutTrimBar();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.PreviewStretch))
            PreviewMedia.Stretch = _viewModel.PreviewStretch;
        if (e.PropertyName is nameof(MainViewModel.WatermarkPosition) or nameof(MainViewModel.PreviewFrameWidth) or nameof(MainViewModel.PreviewFrameHeight))
        {
            FitPreviewFrame();
            UpdateWatermarkPreview();
        }
        if (e.PropertyName == nameof(MainViewModel.Timeline) && _viewModel.MediaRevision == _seenRevision)
            _preview.UpdateTimeline(_viewModel.Timeline);
        if (e.PropertyName == nameof(MainViewModel.MediaRevision))
        {
            _seenRevision = _viewModel.MediaRevision;
            _previewFailed = false;
            PreviewMedia.Stretch = _viewModel.PreviewStretch;
            _preview.Load(_viewModel.Timeline);
        }
        if (e.PropertyName is nameof(MainViewModel.TrimStartSeconds) or nameof(MainViewModel.TrimEndSeconds) or nameof(MainViewModel.HasVideo))
            LayoutTrimBar();
        if (e.PropertyName == nameof(MainViewModel.IsCustomPreset) && !_viewModel.IsCustomPreset)
            AdvancedOptionsExpander.IsExpanded = false;
    }

    // Advanced parameters only open when the user explicitly asks for a custom setup.
    private void PresetTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UiOption { Value: QualityPreset.Custom } })
            AdvancedOptionsExpander.IsExpanded = true;
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_preview is null) return;
        FitPreviewFrame();
        UpdateWatermarkPreview();
    }

    private void FitPreviewFrame()
    {
        var availableWidth = PreviewHost.ActualWidth;
        var availableHeight = PreviewHost.ActualHeight;
        var designWidth = _viewModel.PreviewFrameWidth;
        var designHeight = _viewModel.PreviewFrameHeight;
        if (availableWidth <= 1 || availableHeight <= 1 || designWidth <= 0 || designHeight <= 0) return;
        var scale = Math.Min(availableWidth / designWidth, availableHeight / designHeight);
        PreviewFrame.Width = Math.Max(1, designWidth * scale);
        PreviewFrame.Height = Math.Max(1, designHeight * scale);
    }

    private void UpdateWatermarkPreview()
    {
        (PreviewWatermark.HorizontalAlignment, PreviewWatermark.VerticalAlignment) = _viewModel.WatermarkPosition switch
        {
            WatermarkPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top),
            WatermarkPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top),
            WatermarkPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            _ => (HorizontalAlignment.Right, VerticalAlignment.Bottom)
        };
        var scale = _viewModel.PreviewFrameWidth > 0 && PreviewFrame.Width > 0
            ? PreviewFrame.Width / _viewModel.PreviewFrameWidth
            : 1;
        PreviewWatermark.Width = _viewModel.PreviewWatermarkWidth * scale;
        PreviewWatermark.Margin = new Thickness(_viewModel.PreviewSafeMargin * scale);
    }

    private void UpdateTransportChrome()
    {
        var duration = _preview.Playback.Timeline.DurationSeconds;
        var position = _preview.Playback.PositionSeconds;
        PositionSlider.Maximum = Math.Max(duration, 0.1);
        if (!PositionSlider.IsMouseCaptureWithin)
            PositionSlider.Value = Math.Clamp(position, 0, PositionSlider.Maximum);
        PositionLabel.Text = TimeFormat.Clock(position);
        DurationLabel.Text = TimeFormat.Clock(duration);
        PlayGlyph.Text = _preview.Playback.IsPlaying ? "\uE769" : "\uE768";
        var showVideo = _viewModel.HasVideo && !_previewFailed && _preview.Playback.Timeline.Segments.Count > 0;
        PreviewEmptyState.Visibility = showVideo ? Visibility.Collapsed : Visibility.Visible;
        _viewModel.SetActivePreviewSegment(_preview.Playback.ActiveSegment?.Kind ?? PreviewSegmentKind.Main);
        UpdatePlayhead();
    }

    private void UpdatePlayhead()
    {
        var width = CompositionCanvas.ActualWidth;
        var duration = _preview.Playback.Timeline.DurationSeconds;
        if (width <= 0 || duration <= 0 || !_viewModel.HasVideo)
        {
            CompositionPlayhead.Visibility = Visibility.Collapsed;
            return;
        }

        var x = Math.Clamp(_preview.Playback.PositionSeconds / duration, 0, 1) * Math.Max(0, width - 2);
        Canvas.SetLeft(CompositionPlayhead, x);
        CompositionPlayhead.Visibility = Visibility.Visible;
    }

    private void CompositionCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePlayhead();

    private async void SelectVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecciona un vídeo",
            Filter = "Vídeos|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm|Todos los archivos|*.*",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) await _viewModel.LoadVideoAsync(dialog.FileName);
    }

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (_viewModel.IsIdle && !HelpOverlay.IsVisible && e.Data.GetDataPresent(DataFormats.FileDrop)) DropOverlay.Visibility = Visibility.Visible;
    }

    private void DropOverlay_DragLeave(object sender, DragEventArgs e) => DropOverlay.Visibility = Visibility.Collapsed;

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = _viewModel.IsIdle && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (!_viewModel.IsIdle || HelpOverlay.IsVisible) return;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files && File.Exists(files[0]))
            await _viewModel.LoadVideoAsync(files[0]);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Selecciona la carpeta de destino", InitialDirectory = _viewModel.OutputFolder };
        if (dialog.ShowDialog(this) == true) _viewModel.OutputFolder = dialog.FolderName;
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Timeline.Segments.Count == 0) return;
        _preview.Toggle();
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PositionSlider.IsMouseCaptureWithin)
            PositionLabel.Text = TimeFormat.Clock(e.NewValue);
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.Timeline.Segments.Count == 0) return;
        _preview.Seek(PositionSlider.Value);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_preview is not null) _preview.Volume = e.NewValue;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Timeline.Segments.Count == 0) return;
        var position = _preview.Playback.PositionSeconds;
        if (_preview.Playback.IsPlaying) _preview.Toggle();

        var media = new MediaElement { Stretch = _viewModel.PreviewStretch };
        var host = new Window
        {
            Title = "Vista previa",
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            Background = Brushes.Black,
            Content = media
        };
        var binder = new PreviewMediaBinder(media) { Volume = _preview.Volume };
        host.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) host.Close();
        };
        host.Loaded += (_, _) => binder.PlayFrom(_viewModel.Timeline, position);
        host.Closed += (_, _) => binder.Dispose();
        host.ShowDialog();
    }

    private void TrimCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutTrimBar();

    private void TrimCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _trimDrag = HitTrimHandle(e.GetPosition(TrimCanvas).X);
        if (_trimDrag == TrimDrag.None) return;
        TrimCanvas.CaptureMouse();
        MoveTrim(e.GetPosition(TrimCanvas).X);
        e.Handled = true;
    }

    private void TrimCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var x = e.GetPosition(TrimCanvas).X;
        TrimCanvas.Cursor = HitTrimHandle(x) == TrimDrag.None ? Cursors.Arrow : Cursors.SizeWE;
        if (_trimDrag == TrimDrag.None || e.LeftButton != MouseButtonState.Pressed) return;
        MoveTrim(x);
    }

    private void TrimCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_trimDrag == TrimDrag.None) return;
        _trimDrag = TrimDrag.None;
        if (TrimCanvas.IsMouseCaptured) TrimCanvas.ReleaseMouseCapture();
    }

    private void TrimCanvas_LostMouseCapture(object sender, MouseEventArgs e) => _trimDrag = TrimDrag.None;

    private void MoveTrim(double x)
    {
        var seconds = SecondsAt(x);
        if (_trimDrag == TrimDrag.Start) _viewModel.SetTrimStart(seconds);
        else if (_trimDrag == TrimDrag.End) _viewModel.SetTrimEnd(seconds);
    }

    private TrimDrag HitTrimHandle(double x)
    {
        if (!_viewModel.HasVideo || _viewModel.SourceDurationSeconds <= 0 || TrimCanvas.ActualWidth <= 1) return TrimDrag.None;
        var (startX, endX) = HandleCenters();
        var startDistance = Math.Abs(x - startX);
        var endDistance = Math.Abs(x - endX);
        const double slop = 18;
        if (startDistance > slop && endDistance > slop) return TrimDrag.None;
        return startDistance <= endDistance ? TrimDrag.Start : TrimDrag.End;
    }

    private void LayoutTrimBar()
    {
        if (TrimRail is null || !_viewModel.HasVideo) return;
        var width = TrimCanvas.ActualWidth;
        if (width <= 1 || _viewModel.SourceDurationSeconds <= 0) return;
        const double handle = 16;
        var railWidth = Math.Max(0, width - handle);
        var (startX, endX) = HandleCenters();
        TrimRail.Width = railWidth;
        Canvas.SetLeft(TrimRail, handle / 2);
        Canvas.SetLeft(TrimKept, startX);
        TrimKept.Width = Math.Max(0, endX - startX);
        Canvas.SetLeft(TrimStartHandle, startX - handle / 2);
        Canvas.SetLeft(TrimEndHandle, endX - handle / 2);
    }

    private (double Start, double End) HandleCenters()
    {
        const double handle = 16;
        var railWidth = Math.Max(1, TrimCanvas.ActualWidth - handle);
        var duration = Math.Max(_viewModel.SourceDurationSeconds, 0.001);
        var start = handle / 2 + railWidth * (_viewModel.TrimStartSeconds / duration);
        var end = handle / 2 + railWidth * (_viewModel.TrimEndSeconds / duration);
        return (start, end);
    }

    private double SecondsAt(double x)
    {
        const double handle = 16;
        var railWidth = Math.Max(1, TrimCanvas.ActualWidth - handle);
        var ratio = Math.Clamp((x - handle / 2) / railWidth, 0, 1);
        return ratio * _viewModel.SourceDurationSeconds;
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e) => ThemeService.Toggle();

    private void Help_Click(object sender, RoutedEventArgs e)
    {
        HelpOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => HelpDoneButton.Focus(), DispatcherPriority.Input);
    }

    private void CloseHelp()
    {
        if (HelpOverlay.Visibility != Visibility.Visible) return;
        HelpOverlay.Visibility = Visibility.Collapsed;
        HelpButton.Focus();
    }

    private void CloseHelp_Click(object sender, RoutedEventArgs e) => CloseHelp();

    private void HelpOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseHelp();

    // Clicks inside the card must not reach the veil, which closes the modal.
    private void HelpCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void HelpOverlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseHelp();
        e.Handled = true;
    }
}
