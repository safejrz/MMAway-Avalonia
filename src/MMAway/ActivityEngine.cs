using Avalonia;
using MMAway.Input;

namespace MMAway;

/// <summary>
/// Generates the pseudo-random mouse/typing activity. Port of the original WinForms timer + MouseMoves logic.
/// </summary>
public sealed class ActivityEngine
{
    private static readonly MovementPattern[] MovementPatterns = Enum.GetValues<MovementPattern>();

    private readonly IInputInjector input;
    private readonly Random random = new();
    private readonly Func<PixelRect> getWindowBounds;
    private readonly Func<ActivitySettings> getSettings;
    private readonly Action<string> appendText;
    private readonly Action selectRandomText;
    private readonly Action clearText;
    private const int EdgeMargin = 8;

    private CancellationTokenSource? cts;
    private Task? loop;
    private int pass;

    // Each movement routine is stretched to a random 2-5 seconds.
    private const int MinMoveSeconds = 2;
    private const int MaxMoveSeconds = 5;
    private CancellationToken runToken;
    private readonly System.Diagnostics.Stopwatch paceClock = new();
    private readonly System.Diagnostics.Stopwatch sessionClock = new();
    private double paceIntervalMs;
    private long paceCount;
    private PixelPoint? lastInjectedPosition;
    private int completedActivities;
    private int charactersUntilNextSpace;
    private static readonly TimeSpan CursorPollInterval = TimeSpan.FromMilliseconds(100);
    private MovementPattern? lastPattern;
    private int typingCyclesSinceSelection;
    private int nextSelectionAfter = 3;

    public ActivityEngine(IInputInjector input, Func<PixelRect> getWindowBounds,
        Func<ActivitySettings> getSettings,
        Action<string> appendText, Action selectRandomText, Action clearText)
    {
        this.input = input;
        this.getWindowBounds = getWindowBounds;
        this.getSettings = getSettings;
        this.appendText = appendText;
        this.selectRandomText = selectRandomText;
        this.clearText = clearText;
    }

    public bool IsRunning => loop is { IsCompleted: false };
    public TimeSpan SessionDuration => sessionClock.Elapsed;
    public int ActivityCount => Volatile.Read(ref completedActivities);

    public void Start()
    {
        if (IsRunning) return;
        Volatile.Write(ref completedActivities, 0);
        charactersUntilNextSpace = 0;
        sessionClock.Restart();
        cts = new CancellationTokenSource();
        var ct = cts.Token;
        loop = Task.Run(() => RunAsync(ct));
    }

    public async Task StopAsync()
    {
        if (cts is null) return;
        cts.Cancel();
        try
        {
            if (loop is not null) await loop.ConfigureAwait(false);
        }
        finally
        {
            cts.Dispose();
            cts = null;
            loop = null;
        }
    }

    private Task RunAsync(CancellationToken ct)
    {
        runToken = ct;
        try
        {
            if (ct.IsCancellationRequested) return Task.CompletedTask;
            MoveTo(Center);
            if (!RunPattern(ct)) return Task.CompletedTask;
            Interlocked.Increment(ref completedActivities);

            var scheduleClock = System.Diagnostics.Stopwatch.StartNew();
            var nextActivityAt = scheduleClock.ElapsedMilliseconds + random.Next(10000, 15000);
            var nextTypingAt = scheduleClock.ElapsedMilliseconds + NextTypingInterval();
            while (!ct.IsCancellationRequested)
            {
                var currentSettings = getSettings();
                var typingDue = currentSettings.TypingEnabled && nextTypingAt <= nextActivityAt;
                var nextDueAt = typingDue ? nextTypingAt : nextActivityAt;
                var remaining = nextDueAt - scheduleClock.ElapsedMilliseconds;
                if (remaining > 0 && !Pause((int)Math.Min(remaining, int.MaxValue), ct)) break;
                if (ct.IsCancellationRequested) break;

                if (typingDue)
                {
                    if (!WriteRandomText(ct)) break;
                    nextTypingAt = scheduleClock.ElapsedMilliseconds + NextTypingInterval();
                }
                else
                {
                    if (!Step(ct)) break;
                    Interlocked.Increment(ref completedActivities);
                    nextActivityAt = scheduleClock.ElapsedMilliseconds + NextActivityInterval();
                }
            }
            return Task.CompletedTask;
        }
        finally
        {
            sessionClock.Stop();
        }
    }

    private bool Step(CancellationToken ct)
    {
        if (!WaitForUserMovement(0, ct)) return false;
        if (!RandomMove(ct)) return false;

        if (pass == 0)
        {
            if (ct.IsCancellationRequested) return false;
            MoveTo(Center);
        }

        if (pass > 2 && random.Next(0, 3) <= pass)
        {
            if (!RunPattern(ct)) return false;
        }

        if (pass >= 4)
        {
            if (!RandomMove(ct)) return false;
            pass = 0;
            if (getSettings().TypingEnabled)
                clearText();
        }

        pass++;
        return !ct.IsCancellationRequested;
    }

    // Area the cursor is allowed to occupy: the app window minus a small margin.
    private PixelRect Area
    {
        get
        {
            var b = getWindowBounds();
            var w = Math.Max(1, b.Width - 2 * EdgeMargin);
            var h = Math.Max(1, b.Height - 2 * EdgeMargin);
            return new PixelRect(b.X + EdgeMargin, b.Y + EdgeMargin, w, h);
        }
    }

    private PixelPoint Center => Area.Center;

    private void MoveTo(PixelPoint target)
    {
        var a = Area;
        var x = Math.Clamp(target.X, a.X, a.Right - 1);
        var y = Math.Clamp(target.Y, a.Y, a.Bottom - 1);
        lastInjectedPosition = new PixelPoint(x, y);
        input.SetCursorPosition(lastInjectedPosition.Value);
    }

    private void EndPaced()
    {
        paceClock.Reset();
        paceIntervalMs = 0;
    }

    // Deadline-based, so coarse OS sleep granularity cannot stretch the total duration.
    private bool Pace()
    {
        if (runToken.IsCancellationRequested) return false;
        if (paceIntervalMs <= 0) return true;
        paceCount++;
        while (true)
        {
            var remaining = paceCount * paceIntervalMs - paceClock.Elapsed.TotalMilliseconds;
            if (remaining < 1) return true;

            var waitMilliseconds = (int)Math.Clamp(remaining, 1, CursorPollInterval.TotalMilliseconds);
            if (!Pause(waitMilliseconds, runToken)) return false;
            if (lastInjectedPosition is not { } expected || input.GetCursorPosition() == expected) continue;
            if (!WaitForUserIdle(runToken)) return false;
            paceClock.Restart();
            paceCount = 0;
        }
    }

    private static bool Pause(int ms, CancellationToken ct) =>
        !ct.IsCancellationRequested && !ct.WaitHandle.WaitOne(ms);

    private bool WaitForUserIdle(CancellationToken ct)
    {
        var idleThreshold = TimeSpan.FromSeconds(getSettings().UserIdleSeconds);
        var previousPosition = input.GetCursorPosition();
        var idleClock = System.Diagnostics.Stopwatch.StartNew();
        while (idleClock.Elapsed < idleThreshold)
        {
            if (!Pause((int)CursorPollInterval.TotalMilliseconds, ct)) return false;
            var currentPosition = input.GetCursorPosition();
            if (currentPosition == previousPosition) continue;

            previousPosition = currentPosition;
            idleClock.Restart();
        }

        lastInjectedPosition = previousPosition;
        return true;
    }

    private bool WaitForUserMovement(int milliseconds, CancellationToken ct)
    {
        var waitClock = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (ct.IsCancellationRequested) return false;
            if (lastInjectedPosition is { } expected && input.GetCursorPosition() != expected)
            {
                if (!WaitForUserIdle(ct)) return false;
            }

            var remaining = milliseconds - waitClock.ElapsedMilliseconds;
            if (remaining <= 0) return true;
            if (!Pause((int)Math.Min(remaining, CursorPollInterval.TotalMilliseconds), ct)) return false;
        }
    }

    private bool WriteRandomText(CancellationToken ct)
    {
        if (!getSettings().TypingEnabled) return !ct.IsCancellationRequested;

        var settings = getSettings();
        var repeat = random.Next(
            settings.TypingBurstMinimumCharacters,
            settings.TypingBurstMaximumCharacters + 1);
        for (var i = 0; i < repeat; i++)
        {
            if (ct.IsCancellationRequested) return false;
            if (settings.TypingSpacesEnabled && charactersUntilNextSpace == 0)
            {
                appendText(" ");
                charactersUntilNextSpace = NextSpaceFrequency(settings);
            }
            else
            {
                appendText(((char)random.Next(48, 122)).ToString());
                if (settings.TypingSpacesEnabled) charactersUntilNextSpace--;
            }
            if (!WaitForUserMovement(random.Next(40, 601), ct)) return false;
        }

        typingCyclesSinceSelection++;
        if (typingCyclesSinceSelection >= nextSelectionAfter)
        {
            selectRandomText();
            typingCyclesSinceSelection = 0;
            nextSelectionAfter = random.Next(3, 5);
        }
        return !ct.IsCancellationRequested;
    }

    private bool RunPattern(CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return false;
        var pattern = ChooseRandomPattern(getSettings());
        lastPattern = pattern;
        if (!MovePattern(pattern, ct)) return false;

        ClickOnWindow();
        return !ct.IsCancellationRequested;
    }

    private bool MovePattern(MovementPattern pattern, CancellationToken ct)
    {
        var points = CreatePatternPoints(pattern);
        BeginPaced(points.Length, getSettings());
        try
        {
            foreach (var point in points)
            {
                if (ct.IsCancellationRequested) return false;
                MoveTo(point);
                if (!Pace()) return false;
            }
            return true;
        }
        finally
        {
            EndPaced();
        }
    }

    private PixelPoint[] CreatePatternPoints(MovementPattern pattern)
    {
        var area = Area;
        var center = area.Center;
        var radius = Math.Max(1, Math.Min(area.Width, area.Height) * 0.38);
        var halfWidth = Math.Max(1, area.Width * 0.42);
        var halfHeight = Math.Max(1, area.Height * 0.42);

        IReadOnlyList<Point> points = pattern switch
        {
            MovementPattern.Square => MovementPatternLibrary.CreateSquare(),
            MovementPattern.Circle => MovementPatternLibrary.CreateCircle(),
            MovementPattern.Triangle => MovementPatternLibrary.CreateTriangle(),
            MovementPattern.VerticalHourglass => MovementPatternLibrary.CreateVerticalHourglass(),
            MovementPattern.HorizontalHourglass => MovementPatternLibrary.CreateHorizontalHourglass(),
            MovementPattern.SpiralOut => MovementPatternLibrary.CreateSpiralOut(),
            MovementPattern.SpiralIn => MovementPatternLibrary.CreateSpiralIn(),
            MovementPattern.Oval => MovementPatternLibrary.CreateOval(),
            MovementPattern.IceCreamCone => MovementPatternLibrary.CreateIceCreamCone(),
            MovementPattern.FigureEight => MovementPatternLibrary.CreateFigureEight(),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "Unknown movement pattern.")
        };

        return points.Select(point =>
        {
            var scaleX = pattern is MovementPattern.Circle or MovementPattern.SpiralOut or MovementPattern.SpiralIn
                ? radius
                : halfWidth;
            var scaleY = pattern is MovementPattern.Circle or MovementPattern.SpiralOut or MovementPattern.SpiralIn
                ? radius
                : halfHeight;
            return new PixelPoint(
                center.X + (int)Math.Round(point.X * scaleX),
                center.Y + (int)Math.Round(point.Y * scaleY));
        }).ToArray();
    }

    private void ClickOnWindow()
    {
        var original = input.GetCursorPosition();
        var area = Area;
        MoveTo(new PixelPoint(area.X + 2, area.Y + 2));
        input.LeftClick();
        MoveTo(original);
    }

    private bool RandomMove(CancellationToken ct)
    {
        var area = Area;
        var start = input.GetCursorPosition();
        var end = new PixelPoint(
            random.Next(area.X, area.Right),
            random.Next(area.Y, area.Bottom));
        var control1 = RandomPoint(area);
        var control2 = RandomPoint(area);
        var moves = random.Next(36, 61);

        BeginPaced(moves, getSettings());
        try
        {
            for (var i = 1; i <= moves; i++)
            {
                if (ct.IsCancellationRequested) return false;
                var progress = (double)i / moves;
                var easedProgress = progress * progress * (3 - 2 * progress);
                MoveTo(CubicBezier(start, control1, control2, end, easedProgress));
                if (!Pace()) return false;
            }
            return true;
        }
        finally
        {
            EndPaced();
        }
    }

    private MovementPattern ChooseRandomPattern(ActivitySettings settings)
    {
        var enabled = settings.SelectedPatterns
            .Select(name => Enum.Parse<MovementPattern>(name))
            .ToArray();
        var candidates = enabled.Where(pattern => pattern != lastPattern).ToArray();
        if (candidates.Length == 0) candidates = enabled;
        return candidates[random.Next(candidates.Length)];
    }

    private int NextActivityInterval()
    {
        var settings = getSettings();
        return random.Next(settings.IntervalMinimumSeconds, settings.IntervalMaximumSeconds + 1) * 1000;
    }

    private int NextTypingInterval()
    {
        var settings = getSettings();
        return random.Next(settings.TypingIntervalMinimumSeconds, settings.TypingIntervalMaximumSeconds + 1) * 1000;
    }

    private int NextSpaceFrequency(ActivitySettings settings) =>
        random.Next(settings.SpaceMinimumCharacters, settings.SpaceMaximumCharacters + 1);

    private void BeginPaced(int expectedMoves, ActivitySettings settings)
    {
        var durationMs = random.Next(MinMoveSeconds * 1000, MaxMoveSeconds * 1000 + 1);
        paceIntervalMs = (double)durationMs / Math.Max(1, expectedMoves) / settings.Aggressiveness;
        paceCount = 0;
        paceClock.Restart();
    }

    private PixelPoint RandomPoint(PixelRect area) =>
        new(random.Next(area.X, area.Right), random.Next(area.Y, area.Bottom));

    private static PixelPoint CubicBezier(PixelPoint start, PixelPoint control1,
        PixelPoint control2, PixelPoint end, double t)
    {
        var inverse = 1 - t;
        var x = inverse * inverse * inverse * start.X
            + 3 * inverse * inverse * t * control1.X
            + 3 * inverse * t * t * control2.X
            + t * t * t * end.X;
        var y = inverse * inverse * inverse * start.Y
            + 3 * inverse * inverse * t * control1.Y
            + 3 * inverse * t * t * control2.Y
            + t * t * t * end.Y;
        return new PixelPoint((int)Math.Round(x), (int)Math.Round(y));
    }

}
