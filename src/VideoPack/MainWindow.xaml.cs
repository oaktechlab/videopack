using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using VideoPack.Services;
using VideoPack.ViewModels;

namespace VideoPack;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _playerTimer;
    private bool _mediaOpened;
    private bool _isPlaying;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"VideoPack {AppVersion.Label}";
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(Width, workArea.Width * 0.94));
        Height = Math.Max(MinHeight, Math.Min(Height, workArea.Height * 0.94));
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _playerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _playerTimer.Tick += PlayerTimer_Tick;
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        UpdatePreview();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.PreviewPath) or nameof(MainViewModel.PreviewStretch)) UpdatePreview();
        if (e.PropertyName is nameof(MainViewModel.WatermarkPosition) or nameof(MainViewModel.PreviewFrameWidth) or nameof(MainViewModel.PreviewFrameHeight)) UpdateWatermarkPreview();
        if (e.PropertyName == nameof(MainViewModel.IsCustomPreset) && !_viewModel.IsCustomPreset) AdvancedOptionsExpander.IsExpanded = false;
    }

    // Advanced parameters only open when the user explicitly asks for a custom setup.
    private void PresetTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: UiOption { Value: Models.QualityPreset.Custom } })
            AdvancedOptionsExpander.IsExpanded = true;
    }

    private void UpdatePreview()
    {
        PreviewMedia.Stretch = _viewModel.PreviewStretch;
        UpdateWatermarkPreview();
        var source = _viewModel.PreviewPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            PreviewMedia.Stop();
            PreviewMedia.Source = null;
            _mediaOpened = false;
            _isPlaying = false;
            _playerTimer.Stop();
            PreviewEmptyState.Visibility = Visibility.Visible;
            PositionLabel.Text = DurationLabel.Text = "0:00";
            PositionSlider.Value = 0;
            return;
        }

        try
        {
            PreviewMedia.Stop();
            PreviewMedia.Source = new Uri(source, UriKind.Absolute);
            _mediaOpened = false;
            _isPlaying = false;
            PlayGlyph.Text = "\uE768";
            PreviewEmptyState.Visibility = Visibility.Collapsed;
        }
        catch (UriFormatException)
        {
            PreviewEmptyState.Visibility = Visibility.Visible;
        }
    }

    private void UpdateWatermarkPreview()
    {
        (PreviewWatermark.HorizontalAlignment, PreviewWatermark.VerticalAlignment) = _viewModel.WatermarkPosition switch
        {
            Models.WatermarkPosition.TopLeft => (HorizontalAlignment.Left, VerticalAlignment.Top),
            Models.WatermarkPosition.TopRight => (HorizontalAlignment.Right, VerticalAlignment.Top),
            Models.WatermarkPosition.BottomLeft => (HorizontalAlignment.Left, VerticalAlignment.Bottom),
            _ => (HorizontalAlignment.Right, VerticalAlignment.Bottom)
        };
        PreviewWatermark.Margin = new Thickness(_viewModel.PreviewSafeMargin);
    }

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
        if (!_mediaOpened) return;
        if (_isPlaying)
        {
            PreviewMedia.Pause();
            _playerTimer.Stop();
            PlayGlyph.Text = "\uE768";
        }
        else
        {
            PreviewMedia.Play();
            _playerTimer.Start();
            PlayGlyph.Text = "\uE769";
        }
        _isPlaying = !_isPlaying;
    }

    private void PreviewMedia_MediaOpened(object sender, RoutedEventArgs e)
    {
        _mediaOpened = true;
        PositionSlider.Maximum = Math.Max(1, PreviewMedia.NaturalDuration.TimeSpan.TotalSeconds);
        DurationLabel.Text = FormatTime(PreviewMedia.NaturalDuration.TimeSpan);
        PreviewMedia.Pause();
        // Tiny seek so the paused player paints the first frame instead of black.
        PreviewMedia.Position = TimeSpan.FromMilliseconds(1);
        _isPlaying = false;
        PlayGlyph.Text = "\uE768";
    }

    private void PreviewMedia_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        PreviewEmptyState.Visibility = Visibility.Visible;
        _viewModel.SetPreviewError("Windows no ha podido reproducir este formato en la vista previa. La exportación puede seguir funcionando.");
    }

    private void PlayerTimer_Tick(object? sender, EventArgs e)
    {
        if (!_mediaOpened) return;
        if (!PositionSlider.IsMouseCaptureWithin) PositionSlider.Value = PreviewMedia.Position.TotalSeconds;
        PositionLabel.Text = FormatTime(PreviewMedia.Position);
    }

    private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_mediaOpened && !PositionSlider.IsMouseCaptureWithin)
            PositionLabel.Text = FormatTime(TimeSpan.FromSeconds(e.NewValue));
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mediaOpened) return;
        PreviewMedia.Position = TimeSpan.FromSeconds(PositionSlider.Value);
        PositionLabel.Text = FormatTime(PreviewMedia.Position);
    }

    private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewMedia is not null) PreviewMedia.Volume = e.NewValue;
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e)
    {
        if (!_mediaOpened) return;
        var fullScreen = new Window
        {
            WindowStyle = WindowStyle.None,
            WindowState = WindowState.Maximized,
            Background = Brushes.Black,
            Content = new MediaElement { Source = PreviewMedia.Source, LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Stop, Stretch = Stretch.Uniform, Volume = PreviewMedia.Volume }
        };
        fullScreen.KeyDown += (_, args) => { if (args.Key == Key.Escape) fullScreen.Close(); };
        fullScreen.Loaded += (_, _) => ((MediaElement)fullScreen.Content).Play();
        fullScreen.ShowDialog();
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

    private static string FormatTime(TimeSpan value) => value.TotalHours >= 1 ? value.ToString("h\\:mm\\:ss") : value.ToString("m\\:ss");
}