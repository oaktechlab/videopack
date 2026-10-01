using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace VideoPack.Infrastructure;

/// <summary>True → Visible. ConverterParameter "Invert" reverses the mapping.</summary>
public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true || (value is string text && !string.IsNullOrWhiteSpace(text));
        if (parameter is "Invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Binds a radio-style control to a string value: checked when value equals the parameter.</summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? Binding.DoNothing : Binding.DoNothing;
}
