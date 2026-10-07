using VideoPack.Infrastructure;

namespace VideoPack.ViewModels;

public sealed class UiOption(object value, string title, string description, string symbol = "", string hint = "") : ObservableObject
{
    private bool _isSelected;
    private bool _isEnabled = true;
    private string _toolTip = title;
    public object Value { get; } = value;
    public string Title { get; } = title;
    public string Description { get; } = description;
    public string Symbol { get; } = symbol;
    public string Hint { get; } = hint;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
    public string ToolTip { get => _toolTip; set => SetProperty(ref _toolTip, value); }
}

public sealed record LogoOption(string Name, string FileName, string ImagePath);
public sealed record SummaryItem(string Label, string Value);