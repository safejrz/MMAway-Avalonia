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
    [InlineData(MovementPattern.Square)]
    [InlineData(MovementPattern.Circle)]
    [InlineData(MovementPattern.Triangle)]
    [InlineData(MovementPattern.VerticalHourglass)]
    [InlineData(MovementPattern.HorizontalHourglass)]
    [InlineData(MovementPattern.SpiralOut)]
    [InlineData(MovementPattern.SpiralIn)]
    [InlineData(MovementPattern.Oval)]
    [InlineData(MovementPattern.IceCreamCone)]
    [InlineData(MovementPattern.FigureEight)]
    public async Task PatternAndClickMovementsStayInsideActivityArea(MovementPattern pattern)
    {
        var bounds = new PixelRect(100, 200, 200, 160);
        using var input = new TestInput();
        var engine = CreateEngine(input, bounds: () => bounds, clickTarget: bounds.Center, pattern: pattern);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.True(input.ClickCount > 0);
            Assert.All(input.Movements, point =>
            {
                Assert.InRange(point.X, bounds.X + 8, bounds.Right - 9);
                Assert.InRange(point.Y, bounds.Y + 8, bounds.Bottom - 9);
            });
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task RandomMovementStaysInsideActivityArea()
    {
        var bounds = new PixelRect(100, 200, 200, 160);
        using var input = new TestInput();
        var engine = CreateEngine(input, bounds: () => bounds, clickTarget: bounds.Center, typingEnabled: false);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            var movesBeforeRandomMovement = input.MoveCount;
            await WaitUntilAsync(() => engine.ActivityCount >= 2, timeoutSeconds: 30);
            var randomMovements = input.Movements.Skip(movesBeforeRandomMovement).ToArray();
            Assert.NotEmpty(randomMovements);
            Assert.All(randomMovements, point =>
            {
                Assert.InRange(point.X, bounds.X + 8, bounds.Right - 9);
                Assert.InRange(point.Y, bounds.Y + 8, bounds.Bottom - 9);
            });
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task ClickIsSkippedIfCursorDriftsAfterTargetMove()
    {
        var target = new PixelPoint(175, 250);
        using var input = new TestInput();
        input.OnMove = (point, count) =>
        {
            if (point == target && count > 1)
                input.SetObservedPosition(new PixelPoint(175, 490));
        };
        var engine = CreateEngine(input, clickTarget: target);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
            Assert.Equal(new PixelPoint(175, 490), input.GetCursorPosition());
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task ClickIsSkippedIfUserMovesDuringTargetMove()
    {
        var target = new PixelPoint(175, 250);
        using var input = new TestInput();
        input.OnMove = (point, count) =>
        {
            if (point == target && count > 1)
                input.SimulateUserInput();
        };
        var engine = CreateEngine(input, clickTarget: target);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task ClickIsSkippedIfActivityAreaDisappearsDuringTargetMove()
    {
        var target = new PixelPoint(175, 250);
        var available = 1;
        using var input = new TestInput();
        input.OnMove = (point, count) =>
        {
            if (point == target && count > 1)
                Volatile.Write(ref available, 0);
        };
        var engine = CreateEngine(input, clickTarget: target,
            bounds: () => Volatile.Read(ref available) == 1 ? new PixelRect(0, 0, 350, 500) : null);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task ClickIsSkippedIfWindowLosesFocusDuringTargetMove()
    {
        var target = new PixelPoint(175, 250);
        var focused = 1;
        using var input = new TestInput();
        input.OnMove = (point, count) =>
        {
            if (point == target && count > 1)
                Volatile.Write(ref focused, 0);
        };
        var engine = CreateEngine(input, clickTarget: target,
            clickAllowed: () => Volatile.Read(ref focused) == 1);
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task ClickIsSkippedIfTargetIsOutsideActivityArea()
    {
        using var input = new TestInput();
        var engine = CreateEngine(input, clickTarget: new PixelPoint(175, 495));
        engine.Start();
        try
        {
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
            Assert.Equal(0, input.ClickCount);
        }
        finally
        {
            await engine.StopAsync();
        }
    }

    [Fact]
    public async Task MissingActivityAreaPausesMovementAndCanBeCancelled()
    {
        using var input = new TestInput();
        var engine = CreateEngine(input, bounds: () => null);
        engine.Start();
        await Task.Delay(250);
        Assert.Equal(0, input.MoveCount);
        Assert.Equal(0, input.ClickCount);
        await engine.StopAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task ActivityResumesAfterAreaReturnsAndUserIsIdle()
    {
        var available = 0;
        using var input = new TestInput();
        var engine = CreateEngine(input,
            bounds: () => Volatile.Read(ref available) == 1 ? new PixelRect(0, 0, 350, 500) : null);
        engine.Start();
        try
        {
            await Task.Delay(250);
            input.SimulateUserInput();
            Volatile.Write(ref available, 1);
            await Task.Delay(250);
            Assert.Equal(0, input.MoveCount);
            await WaitUntilAsync(() => engine.ActivityCount >= 1);
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
        PixelPoint? clickTarget = null, bool canClick = true, Func<PixelRect?>? bounds = null,
        MovementPattern pattern = MovementPattern.Circle, Func<bool>? clickAllowed = null,
        bool typingEnabled = true) =>
        new(input, bounds ?? (() => new PixelRect(0, 0, 350, 500)),
            () => clickTarget ?? new PixelPoint(175, 250),
            clickAllowed ?? (() => canClick),
            () => new ActivitySettings
            {
                Aggressiveness = 3,
                UserIdleSeconds = 1,
                TypingEnabled = typingEnabled,
                TypingIntervalMinimumSeconds = 1,
                TypingIntervalMaximumSeconds = 1,
                TypingBurstMinimumCharacters = 1,
                TypingBurstMaximumCharacters = 1,
                SelectedPatterns = [pattern.ToString()]
            },
            append ?? (_ => { }), () => { }, () => { });

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutSeconds = 8)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(timeoutSeconds), "Timed out waiting for activity state.");
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
        private readonly List<PixelPoint> movements = [];

        public bool MonitorSupported { get; init; } = true;
        public int CursorOffset { get; init; }
        public int SimulateInputAfterMoveCount { get; init; }
        public Action<PixelPoint, int>? OnMove { get; set; }
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

        public PixelPoint[] Movements
        {
            get
            {
                lock (gate)
                    return movements.ToArray();
            }
        }

        public PixelPoint GetCursorPosition()
        {
            lock (gate)
                return new PixelPoint(position.X + CursorOffset, position.Y);
        }

        public void SetCursorPosition(PixelPoint target)
        {
            lock (gate)
            {
                position = target;
                movements.Add(target);
            }
            var count = Interlocked.Increment(ref moveCount);
            OnMove?.Invoke(target, count);
            if (count == SimulateInputAfterMoveCount)
                SimulateUserInput();
        }

        public void SetObservedPosition(PixelPoint observed)
        {
            lock (gate)
                position = observed;
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
