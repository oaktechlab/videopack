using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using VideoPack.Models;

namespace VideoPack.Infrastructure;

/// <summary>
/// Binds a manual <see cref="MediaElement"/> to a <see cref="PreviewPlayback"/>.
/// LoadedBehavior=Manual does not present a frame until Play() starts the media clock.
/// Stop() is also asynchronous: calling it immediately before Source closes the graph
/// that was just opened, so the first video stays blank and only the next one appears.
/// </summary>
public sealed class PreviewMediaBinder : IDisposable
{
    private readonly MediaElement _media;
    private readonly DispatcherTimer _timer;
    private int _generation;
    private bool _ready;
    private bool _hasSource;
    private bool _priming;
    private double _pendingPosition;
    private bool _pendingPlay;
    private double _volume = 0.8;

    public PreviewMediaBinder(MediaElement media)
    {
        _media = media;
        _media.LoadedBehavior = MediaState.Manual;
        _media.UnloadedBehavior = MediaState.Manual;
        _media.ScrubbingEnabled = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        _media.MediaOpened += OnOpened;
        _media.MediaEnded += OnEnded;
        _media.MediaFailed += OnFailed;
    }

    public PreviewPlayback Playback { get; } = new();
    public event Action? Changed;
    public event Action<string>? Failed;

    public double Volume
    {
        get => _volume;
        set
        {
            _volume = value;
            if (!_priming) _media.Volume = value;
        }
    }

    public void Load(PreviewTimeline timeline) => Apply(Playback.Load(timeline));

    public void UpdateTimeline(PreviewTimeline timeline) => Apply(Playback.Update(timeline));

    public void Toggle() => Apply(Playback.TogglePlay());

    public void Seek(double timelineSeconds) => Apply(Playback.Seek(timelineSeconds, Playback.IsPlaying));

    public void PlayFrom(PreviewTimeline timeline, double timelineSeconds)
    {
        _hasSource = false;
        Playback.Load(timeline);
        var transport = Playback.Seek(timelineSeconds, true);
        if (transport.Kind != PreviewTransportKind.Open && Playback.ActiveSegment is { } segment)
        {
            var source = segment.ToSource(Math.Min(Playback.PositionSeconds, Math.Max(segment.TimelineStartSeconds, segment.TimelineEndSeconds - 0.001)));
            transport = PreviewTransport.Open(segment, source, true, Playback.PositionSeconds);
        }
        Apply(transport);
    }

    public void Dispose()
    {
        _timer.Stop();
        _generation++;
        _media.MediaOpened -= OnOpened;
        _media.MediaEnded -= OnEnded;
        _media.MediaFailed -= OnFailed;
        _media.Source = null;
    }

    private void Apply(PreviewTransport transport)
    {
        switch (transport.Kind)
        {
            case PreviewTransportKind.None:
                return;
            case PreviewTransportKind.Hold:
                Changed?.Invoke();
                return;
            case PreviewTransportKind.Clear:
                Clear();
                Changed?.Invoke();
                return;
            case PreviewTransportKind.Open:
                Open(transport.Source!, transport.SourcePosition, transport.Play);
                Changed?.Invoke();
                return;
            case PreviewTransportKind.Seek:
                if (!_hasSource && Playback.ActiveSegment is { } segment)
                    Open(segment.Source, transport.SourcePosition, transport.Play);
                else
                    SeekMedia(transport.SourcePosition, transport.Play);
                Changed?.Invoke();
                return;
            case PreviewTransportKind.Play:
                _pendingPlay = true;
                if (_ready)
                {
                    _priming = false;
                    _media.Volume = _volume;
                    _media.Play();
                    _timer.Start();
                }
                Changed?.Invoke();
                return;
            case PreviewTransportKind.Pause:
            case PreviewTransportKind.Stop:
                _pendingPlay = false;
                _timer.Stop();
                if (_ready) _media.Pause();
                Changed?.Invoke();
                return;
        }
    }

    private void Open(string path, double sourcePosition, bool play)
    {
        var generation = ++_generation;
        _ready = false;
        _hasSource = true;
        _pendingPlay = play;
        _pendingPosition = sourcePosition <= 0.001 ? 0.001 : sourcePosition;
        _timer.Stop();
        var uri = new Uri(path, UriKind.Absolute);

        void Assign()
        {
            if (generation != _generation) return;
            if (!play)
            {
                _priming = true;
                _media.Volume = 0;
            }
            else
            {
                _priming = false;
                _media.Volume = _volume;
            }
            _media.Source = uri;
            _media.Play();
        }

        if (_media.Source is Uri current && current.Equals(uri))
        {
            _media.Source = null;
            _media.Dispatcher.BeginInvoke(Assign, DispatcherPriority.Loaded);
        }
        else Assign();
    }

    private void SeekMedia(double sourcePosition, bool play)
    {
        var position = sourcePosition <= 0.001 ? 0.001 : sourcePosition;
        _pendingPosition = position;
        _pendingPlay = play;
        if (!_ready) return;
        try { _media.Position = TimeSpan.FromSeconds(position); }
        catch (InvalidOperationException) { }

        if (play)
        {
            _priming = false;
            _media.Volume = _volume;
            _media.Play();
            _timer.Start();
        }
        else
        {
            _media.Pause();
            _timer.Stop();
        }
    }

    private void Clear()
    {
        _generation++;
        _ready = false;
        _hasSource = false;
        _pendingPlay = false;
        _priming = false;
        _timer.Stop();
        _media.Volume = _volume;
        _media.Source = null;
    }

    private void OnOpened(object? sender, RoutedEventArgs e)
    {
        var generation = _generation;
        _ready = true;
        _media.Dispatcher.BeginInvoke(() => PresentOpenedFrame(generation), DispatcherPriority.Render);
    }

    private void PresentOpenedFrame(int generation)
    {
        if (generation != _generation) return;
        var play = _pendingPlay;
        try { _media.Position = TimeSpan.FromSeconds(_pendingPosition); }
        catch (InvalidOperationException) { }

        if (play)
        {
            _priming = false;
            _media.Volume = _volume;
            _media.Play();
            _timer.Start();
            Changed?.Invoke();
            return;
        }

        _media.Pause();
        _media.Dispatcher.BeginInvoke(() =>
        {
            if (generation != _generation || _pendingPlay) return;
            try { _media.Position = TimeSpan.FromSeconds(_pendingPosition); }
            catch (InvalidOperationException) { }
            _priming = false;
            _media.Volume = _volume;
            Changed?.Invoke();
        }, DispatcherPriority.ApplicationIdle);
    }

    private void OnEnded(object? sender, RoutedEventArgs e)
    {
        if (!_ready || !Playback.IsPlaying) return;
        var transport = Playback.OnMediaEnded();
        if (transport.Kind == PreviewTransportKind.Open) _ready = false;
        Apply(transport);
    }

    private void OnFailed(object? sender, ExceptionRoutedEventArgs e)
    {
        _ready = false;
        _timer.Stop();
        Failed?.Invoke("Windows no ha podido reproducir este formato en la vista previa. La exportación puede seguir funcionando.");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_ready || !Playback.IsPlaying) return;
        Apply(Playback.OnSourcePosition(_media.Position.TotalSeconds));
    }
}
