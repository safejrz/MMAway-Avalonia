using Avalonia;

namespace MMAway.Tests;

public sealed class WindowPlacementTests
{
    [Fact]
    public void CentersWindowInRightmostQuarter()
    {
        var position = WindowPlacement.GetStartupPosition(
            new PixelRect(0, 0, 1600, 1000), new Size(350, 500), 1);

        Assert.Equal(new PixelPoint(1225, 200), position);
    }

    [Fact]
    public void AccountsForScalingAndWorkingAreaOffset()
    {
        var position = WindowPlacement.GetStartupPosition(
            new PixelRect(-2560, 40, 2560, 1040), new Size(350, 500), 1.5);

        Assert.Equal(new PixelPoint(-582, 248), position);
    }

    [Fact]
    public void KeepsWindowWithinSmallWorkingArea()
    {
        var position = WindowPlacement.GetStartupPosition(
            new PixelRect(10, 20, 800, 600), new Size(350, 500), 1);

        Assert.Equal(new PixelPoint(460, 120), position);
    }

    [Fact]
    public void AnchorsOversizedWindowAtWorkingAreaOrigin()
    {
        var position = WindowPlacement.GetStartupPosition(
            new PixelRect(10, 20, 300, 400), new Size(350, 500), 1);

        Assert.Equal(new PixelPoint(10, 20), position);
    }
}
