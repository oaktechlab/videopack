using System.Reflection;

namespace VideoPack;

/// <summary>
/// Product version read from the assembly. Set it with the Version property in VideoPack.csproj.
/// </summary>
public static class AppVersion
{
    public static string Number { get; } = ReadNumber();

    public static string Label => $"v{Number}";

    public static string About => $"Versión {Number}";

    private static string ReadNumber()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            var number = (plus >= 0 ? informational[..plus] : informational).Trim();
            if (number.Length > 0) return number;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        if (version is null) return "0.0.0";
        var build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{version.Minor}.{build}";
    }
}
