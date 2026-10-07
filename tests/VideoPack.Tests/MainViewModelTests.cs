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

        viewModel.TextWatermarkEnabled = false;
        viewModel.SelectFormatCommand.Execute(OutputFormat.Square);
        viewModel.SelectPositionCommand.Execute(WatermarkPosition.BottomLeft);

        Assert.Equal("1920", viewModel.OutputWidth);
        Assert.Equal("1920", viewModel.OutputHeight);
        Assert.Equal(WatermarkPosition.BottomLeft, viewModel.Positions.Single(x => x.IsSelected).Value);
    }

    [Fact]
    public void Watermarks_start_in_opposite_bottom_corners()
    {
        var viewModel = new MainViewModel();

        Assert.True(viewModel.WatermarkEnabled);
        Assert.Equal(WatermarkPosition.BottomRight, viewModel.WatermarkPosition);
        Assert.True(viewModel.TextWatermarkEnabled);
        Assert.Equal(WatermarkPosition.BottomLeft, viewModel.TextWatermarkPosition);
        Assert.Equal(VideoRules.DefaultTextWatermark, viewModel.TextWatermark);
        Assert.False(viewModel.Positions.Single(x => Equals(x.Value, WatermarkPosition.BottomLeft)).IsEnabled);
        Assert.False(viewModel.TextPositions.Single(x => Equals(x.Value, WatermarkPosition.BottomRight)).IsEnabled);
    }

    [Fact]
    public async Task Logo_defaults_to_white_color_tecnalia()
    {
        var viewModel = new MainViewModel();

        await viewModel.InitializeAsync();

        Assert.Equal("Tecnalia blanco color", viewModel.SelectedLogo?.Name);
        Assert.Equal(WatermarkPosition.BottomRight, viewModel.WatermarkPosition);
    }

    [Fact]
    public void Occupied_corner_stays_blocked_until_the_other_mosca_is_off()
    {
        var viewModel = new MainViewModel();

        viewModel.SelectTextPositionCommand.Execute(WatermarkPosition.BottomRight);
        viewModel.SelectPositionCommand.Execute(WatermarkPosition.BottomLeft);

        Assert.Equal(WatermarkPosition.BottomLeft, viewModel.TextWatermarkPosition);
        Assert.Equal(WatermarkPosition.BottomRight, viewModel.WatermarkPosition);

        viewModel.WatermarkEnabled = false;
        viewModel.SelectTextPositionCommand.Execute(WatermarkPosition.BottomRight);

        Assert.Equal(WatermarkPosition.BottomRight, viewModel.TextWatermarkPosition);
        Assert.True(viewModel.TextPositions.Single(x => Equals(x.Value, WatermarkPosition.TopLeft)).IsEnabled);
    }

    [Fact]
    public void Enabling_a_mosca_moves_it_off_the_corner_already_taken()
    {
        var viewModel = new MainViewModel();

        viewModel.TextWatermarkEnabled = false;
        viewModel.SelectPositionCommand.Execute(WatermarkPosition.BottomLeft);
        viewModel.TextWatermarkEnabled = true;

        Assert.Equal(WatermarkPosition.BottomLeft, viewModel.WatermarkPosition);
        Assert.Equal(WatermarkPosition.BottomRight, viewModel.TextWatermarkPosition);
        Assert.NotEqual(viewModel.WatermarkPosition, viewModel.TextWatermarkPosition);
    }

    [Fact]
    public void Text_watermark_keeps_three_lines_of_thirty_characters()
    {
        var viewModel = new MainViewModel();

        viewModel.TextWatermark = new string('a', 31) + "\r\n" + new string('b', 40) + "\n" + "tercera\n" + "cuarta";

        Assert.Equal(new string('a', 30) + "\n" + new string('b', 30) + "\ntercera", viewModel.TextWatermark);
    }
}