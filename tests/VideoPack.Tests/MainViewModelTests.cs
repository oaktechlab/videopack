using VideoPack.Models;
using VideoPack.ViewModels;
using Xunit;

namespace VideoPack.Tests;

public sealed class MainViewModelTests
{
    [Fact]
    public void Manual_bitrate_change_switches_quality_to_custom()
    {
        var viewModel = new MainViewModel();

        viewModel.VideoBitrate = "8000";

        Assert.True(viewModel.IsCustomPreset);
        Assert.Equal(QualityPreset.Custom, viewModel.Presets.Single(x => x.IsSelected).Value);
    }

    [Fact]
    public void Selecting_portrait_disables_landscape_intro_and_keeps_quality_level()
    {
        var viewModel = new MainViewModel();

        viewModel.SelectFormatCommand.Execute(OutputFormat.Portrait);

        Assert.False(viewModel.AddIntro);
        Assert.False(viewModel.ShowIntroOption);
        Assert.Equal("1080", viewModel.OutputWidth);
        Assert.Equal("1920", viewModel.OutputHeight);
        Assert.Equal(QualityPreset.Custom, viewModel.Presets.Single(x => x.IsSelected).Value);
    }

    [Fact]
    public void Square_format_recalculates_dimensions_and_position_selection()
    {
        var viewModel = new MainViewModel();

        viewModel.SelectFormatCommand.Execute(OutputFormat.Square);
        viewModel.SelectPositionCommand.Execute(WatermarkPosition.BottomLeft);

        Assert.Equal("1920", viewModel.OutputWidth);
        Assert.Equal("1920", viewModel.OutputHeight);
        Assert.Equal(WatermarkPosition.BottomLeft, viewModel.Positions.Single(x => x.IsSelected).Value);
    }
}