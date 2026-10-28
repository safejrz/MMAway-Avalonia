using System.Diagnostics;
using Avalonia;
using MMAway.Input;

namespace MMAway.Tests;

public sealed class ActivityEngineTests
{
    [Fact]
    public async Task CursorSynchronizationDoesNotBlockInitialPatternOrTyping()
    {
        using var input = new TestInput { CursorOffset = 1 };
        var characters = 0;
        var engine = CreateEngine(input, _ => Interlocked.Increment(ref characters));
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1 && Volatile.Read(ref characters) > 0);
            Assert.True(engine.IsRunning);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task PatternClickUsesProvidedTarget()
    {
        var target = new PixelPoint(175, 250);
        using var input = new TestInput();
        var engine = CreateEngine(input, clickTarget: target);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => input.ClickCount > 0);
            Assert.Equal(target, input.LastClickPosition);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task PatternClickIsSkippedWhenWindowCannotClick()
    {
        using var input = new TestInput();
        var engine = CreateEngine(input, canClick: false);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
            Assert.True(input.MoveCount > 0);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UserInputPausesAndResumesAfterIdle(bool monitorSupported)
    {
        using var input = new TestInput
        {
            MonitorSupported = monitorSupported,
            SimulateInputAfterMoveCount = 2
        };
        var engine = CreateEngine(input);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => input.MoveCount >= 2);
            await Task.Delay(250);
            Assert.Equal(2, input.MoveCount);
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.True(engine.IsRunning);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task StopCancelsIdleWaitAndSessionCanRestart()
    {
        using var input = new TestInput { SimulateInputAfterMoveCount = 2 };
        var engine = CreateEngine(input);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => input.MoveCount >= 2);
            await Task.Delay(250);
            Assert.Equal(2, input.MoveCount);
            await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False(engine.IsRunning);
            engine.Start();
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    private static ActivityEngine CreateEngine(TestInput input, Action<string>? append = null,
        PixelPoint? clickTarget = null, bool canClick = true) =>
        new(input, () => new PixelRect(0, 0, 350, 500),
            () => clickTarget ?? new PixelPoint(175, 250),
            () => canClick,
            () => new ActivitySettings
            {
                Aggressiveness = 3,
                UserIdleSeconds = 1,
                TypingIntervalMinimumSeconds = 1,
                TypingIntervalMaximumSeconds = 1,
                TypingBurstMinimumCharacters = 1,
                TypingBurstMaximumCharacters = 1,
                SelectedPatterns = [nameof(MovementPattern.Circle)]
            },
            append ?? (_ => { }), () => { }, () => { });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(8), "Timed out waiting for activity state.");
            await Task.Delay(10);
        }
    }

    private sealed class TestInput : IInputInjector
    {
        private readonly object gate = new();
        private PixelPoint position;
        private int version;
        private int moveCount;
        private int clickCount;

        public bool MonitorSupported { get; init; } = true;
        public int CursorOffset { get; init; }
        public int SimulateInputAfterMoveCount { get; init; }
        public uint? UserInputVersion => MonitorSupported ? unchecked((uint)Volatile.Read(ref version)) : null;
        public int MoveCount => Volatile.Read(ref moveCount);
        public int ClickCount => Volatile.Read(ref clickCount);
        public PixelPoint? LastClickPosition
        {
            get
            {
                lock (gate)
                    return lastClickPosition;
            }
        }

        private PixelPoint? lastClickPosition;

        public PixelPoint GetCursorPosition()
        {
            lock (gate)
                return new PixelPoint(position.X + CursorOffset, position.Y);
        }

        public void SetCursorPosition(PixelPoint target)
        {
            lock (gate)
                position = target;
            if (Interlocked.Increment(ref moveCount) == SimulateInputAfterMoveCount)
                SimulateUserInput();
        }

        public void LeftClick()
        {
            lock (gate)
                lastClickPosition = position;
            Interlocked.Increment(ref clickCount);
        }

        public void SimulateUserInput()
        {
            Interlocked.Increment(ref version);
            if (MonitorSupported) return;
            lock (gate)
                position = new PixelPoint(position.X + 20, position.Y);
        }

        public void Dispose()
        {
        }
    }
}
