using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using VideoPack.Models;
using VideoPack.Services;
using VideoPack.ViewModels;
using Xunit;

namespace VideoPack.Tests;

public sealed class VisualPreviewTests
{
    [Fact]
    public void Renders_the_main_preview_states()
    {
        if (Environment.GetEnvironmentVariable("VIDEOPACK_VISUAL") != "1") return;
        var paths = new AppPaths();
        var folder = Path.Combine(paths.TempDirectory, "visual");
        Directory.CreateDirectory(folder);
        var video = Path.Combine(folder, "sample.mp4");
        if (!File.Exists(video)) CreateVideo(paths.FfmpegPath, video);
        Exception? failure = null;
        var done = new ManualResetEventSlim(false);
        var thread = new Thread(() =>
        {
            try { Render(video, folder); }
            catch (Exception exception) { failure = exception; }
            finally { done.Set(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(done.Wait(TimeSpan.FromSeconds(90)), failure?.ToString() ?? "La ventana no terminó a tiempo.");
        if (failure is not null) throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void Render(string video, string folder)
    {
        var application = new VideoPack.App();
        application.InitializeComponent();
        typeof(Application).GetField("_startupUri", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(application, null);
        var window = new MainWindow
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 20,
            Top = 20,
            Width = 1440,
            Height = 900
        };
        var viewModel = (MainViewModel)window.DataContext;
        window.Loaded += async (_, _) =>
        {
            try
            {
                for (var attempt = 0; attempt < 50 && viewModel.Logos.Count == 0; attempt++)
                    await Task.Delay(100);
                await viewModel.LoadVideoAsync(video);
                for (var attempt = 0; attempt < 40 && !viewModel.Timeline.Segments.Any(segment => segment.Kind == PreviewSegmentKind.Intro); attempt++)
                    await Task.Delay(100);
                await Task.Delay(1200);
                Save(window, Path.Combine(folder, "01-first-video.png"));

                viewModel.SetTrimStart(1);
                viewModel.SetTrimEnd(viewModel.SourceDurationSeconds - 1);
                await Task.Delay(500);
                Save(window, Path.Combine(folder, "02-trim.png"));

                Click(window, "PlayPauseButton");
                await Task.Delay(1200);
                Save(window, Path.Combine(folder, "03-playing.png"));
                Click(window, "PlayPauseButton");

                viewModel.SelectFormatCommand.Execute(OutputFormat.Portrait);
                await Task.Delay(600);
                Save(window, Path.Combine(folder, "04-portrait.png"));

                viewModel.SelectFormatCommand.Execute(OutputFormat.Square);
                await Task.Delay(600);
                Save(window, Path.Combine(folder, "05-square.png"));

                viewModel.SelectFormatCommand.Execute(OutputFormat.Landscape);
                viewModel.AddIntro = true;
                viewModel.AddOutro = true;
                viewModel.ResetTrim();
                await Task.Delay(800);
                Save(window, Path.Combine(folder, "06-intro-outro.png"));
            }
            finally
            {
                window.Close();
            }
        };
        application.Run(window);
    }

    private static void Click(DependencyObject root, string automationId)
    {
        var button = FindButton(root, automationId) ?? throw new InvalidOperationException($"No está el botón {automationId}.");
        button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    }

    private static System.Windows.Controls.Button? FindButton(DependencyObject root, string automationId)
    {
        if (root is System.Windows.Controls.Button button && System.Windows.Automation.AutomationProperties.GetAutomationId(button) == automationId)
            return button;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var found = FindButton(VisualTreeHelper.GetChild(root, index), automationId);
            if (found is not null) return found;
        }
        return null;
    }

    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        var source = PresentationSource.FromVisual(window);
        var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
        var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
        var width = Math.Max(1, (int)(window.ActualWidth * scaleX));
        var height = Math.Max(1, (int)(window.ActualHeight * scaleY));
        var bitmap = new RenderTargetBitmap(width, height, 96 * scaleX, 96 * scaleY, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void CreateVideo(string ffmpeg, string path)
    {
        var start = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
        {
            "-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "color=c=0xC45C26:s=1280x720:r=25:d=8",
            "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000:duration=8",
            "-shortest", "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-t", "8", "-y", path
        })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar FFmpeg.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException(error);
    }
}
