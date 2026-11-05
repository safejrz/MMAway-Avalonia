using Avalonia;

namespace MMAway;

internal static class WindowPlacement
{
    internal static PixelPoint GetStartupPosition(PixelRect workingArea, Size windowSize, double scaling)
    {
        var width = (int)Math.Round(windowSize.Width * scaling);
        var height = (int)Math.Round(windowSize.Height * scaling);
        var maxX = Math.Max(0, workingArea.Width - width);
        var maxY = Math.Max(0, workingArea.Height - height);
        var x = (int)Math.Round(workingArea.Width * 0.875 - width / 2.0);
        var y = workingArea.Height / 5;

        return new PixelPoint(
            workingArea.X + Math.Clamp(x, 0, maxX),
            workingArea.Y + Math.Clamp(y, 0, maxY));
    }
}
