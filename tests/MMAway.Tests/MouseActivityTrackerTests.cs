using Avalonia;
using MMAway.Input;

namespace MMAway.Tests;

public sealed class MouseActivityTrackerTests
{
    [Fact]
    public async Task DelayedUnmarkedRemoteEchoDoesNotCountAsUserInput()
    {
        var tracker = new MouseActivityTracker();
        var first = new PixelPoint(100, 100);
        var second = new PixelPoint(110, 110);
        tracker.RecordInjection(first);
        tracker.RecordMouseEvent(first, injected: false, movement: true);
        tracker.RecordInjection(second);
        await Task.Delay(150);
        tracker.RecordMouseEvent(first, injected: false, movement: true);
        tracker.RecordMouseEvent(second, injected: false, movement: true);
        Assert.Equal(0u, tracker.Version);

        tracker.RecordMouseEvent(new PixelPoint(200, 300), injected: false, movement: true);
        Assert.Equal(1u, tracker.Version);
    }

    [Fact]
    public void RealClickAtInjectedPositionStillCountsAsUserInput()
    {
        var tracker = new MouseActivityTracker();
        var position = new PixelPoint(100, 100);
        tracker.RecordInjection(position);
        tracker.RecordMouseEvent(position, injected: true, movement: false);
        Assert.Equal(0u, tracker.Version);
        tracker.RecordMouseEvent(position, injected: false, movement: false);
        Assert.Equal(1u, tracker.Version);
    }

    [Fact]
    public async Task OldInjectedPositionDoesNotHideLaterUserMovement()
    {
        var tracker = new MouseActivityTracker();
        var position = new PixelPoint(100, 100);
        tracker.RecordInjection(position);
        await Task.Delay(2100);
        tracker.RecordMouseEvent(position, injected: false, movement: true);
        Assert.Equal(1u, tracker.Version);
    }
}
