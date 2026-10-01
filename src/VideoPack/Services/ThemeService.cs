using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VideoPack.Services;

/// <summary>Swaps the palette dictionary merged at slot 0 of App.xaml and remembers the user's choice.</summary>
public static class ThemeService
{
    private const int DwmUseImmersiveDarkMode = 20;

    private static readonly string PreferencePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VideoPack", "theme.txt");

    public static bool IsDark { get; private set; }

    public static void Initialize() => Apply(LoadPreference() ?? false);

    public static void Toggle()
    {
        Apply(!IsDark);
        SavePreference();
    }

    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var enabled = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref enabled, sizeof(int));
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var palette = new ResourceDictionary { Source = new Uri($"pack://application:,,,/VideoPack;component/Themes/{(dark ? "Dark" : "Light")}.xaml") };
        Application.Current.Resources.MergedDictionaries[0] = palette;
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
    }

    private static bool? LoadPreference()
    {
        try
        {
            return File.Exists(PreferencePath) ? File.ReadAllText(PreferencePath).Trim() switch { "dark" => true, "light" => false, _ => null } : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static void SavePreference()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, IsDark ? "dark" : "light");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
