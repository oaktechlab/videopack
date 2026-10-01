namespace VideoPack.ViewModels;

public sealed class HelpStep
{
    public int Number { get; set; }
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsLast { get; set; }
}
