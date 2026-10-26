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
    private double paceIntervalMs;
    private long paceCount;
    private MovementPattern? lastPattern;
    private int typingCyclesSinceSelection;
    private int nextSelectionAfter = 3;

    public ActivityEngine(IInputInjector input, Func<PixelRect> getWindowBounds,
        Action<string> appendText, Action selectRandomText, Action clearText)
    {
        this.input = input;
        this.getWindowBounds = getWindowBounds;
        this.appendText = appendText;
        this.selectRandomText = selectRandomText;
        this.clearText = clearText;
    }

    public bool IsRunning => loop is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;
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
        catch (OperationCanceledException)
        {
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
            MoveTo(Center);
            RunPattern(ct);

            var delay = random.Next(10000, 15000);
            while (!ct.IsCancellationRequested)
            {
                // Waiting on the token avoids the TaskCanceledException Task.Delay throws on stop.
                if (ct.WaitHandle.WaitOne(delay)) break;
                Step(ct);
                delay = random.Next(5000, 15000);
            }
        }
        catch (OperationCanceledException)
        {
        }
        return Task.CompletedTask;
    }

    private void Step(CancellationToken ct)
    {
        RandomMove(ct);
        WriteRandomText(ct);

        if (pass == 0)
        {
            MoveTo(Center);
        }

        if (pass > 2 && random.Next(0, 3) <= pass)
        {
            RunPattern(ct);
            WriteRandomText(ct);
        }

        if (pass >= 4)
        {
            RandomMove(ct);
            pass = 0;
            clearText();
        }

        pass++;
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
        input.SetCursorPosition(new PixelPoint(x, y));
    }

    private void Move(int dx, int dy)
    {
        var p = input.GetCursorPosition();
        MoveTo(new PixelPoint(p.X + dx, p.Y + dy));
        Pace();
    }

    private void BeginPaced(int expectedMoves)
    {
        var durationMs = random.Next(MinMoveSeconds * 1000, MaxMoveSeconds * 1000 + 1);
        paceIntervalMs = (double)durationMs / Math.Max(1, expectedMoves);
        paceCount = 0;
        paceClock.Restart();
    }

    private void EndPaced()
    {
        paceClock.Reset();
        paceIntervalMs = 0;
    }

    // Deadline-based, so coarse OS sleep granularity cannot stretch the total duration.
    private void Pace()
    {
        if (paceIntervalMs <= 0) return;
        paceCount++;
        var remaining = paceCount * paceIntervalMs - paceClock.Elapsed.TotalMilliseconds;
        if (remaining >= 1) Pause((int)remaining, runToken);
        else runToken.ThrowIfCancellationRequested();
    }

    private static void Pause(int ms, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ct.WaitHandle.WaitOne(ms)) ct.ThrowIfCancellationRequested();
    }

    private void WriteRandomText(CancellationToken ct)
    {
        var repeat = random.Next(2, 7);
        for (var i = 0; i < repeat; i++)
        {
            appendText(((char)random.Next(48, 122)).ToString());
            Pause(repeat * random.Next(20, 100), ct);
        }

        typingCyclesSinceSelection++;
        if (typingCyclesSinceSelection >= nextSelectionAfter)
        {
            selectRandomText();
            typingCyclesSinceSelection = 0;
            nextSelectionAfter = random.Next(3, 5);
        }
    }

    private void RunPattern(CancellationToken ct)
    {
        var candidates = MovementPatterns.Where(pattern => pattern != lastPattern).ToArray();
        var pattern = candidates[random.Next(candidates.Length)];
        lastPattern = pattern;
        MovePattern(pattern, ct);

        ClickOnWindow();
    }

    private void MovePattern(MovementPattern pattern, CancellationToken ct)
    {
        var points = CreatePatternPoints(pattern);
        BeginPaced(points.Length);
        try
        {
            foreach (var point in points)
            {
                ct.ThrowIfCancellationRequested();
                MoveTo(point);
                Pace();
            }
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

    private void RandomMove(CancellationToken ct)
    {
        var moves = random.Next(20, 55);
        var variance = random.Next(1, 3);
        var horizontal = random.Next(1, 6) >= 4;

        BeginPaced(moves);
        try
        {
            RandomMoveCore(moves, variance, horizontal);
        }
        finally
        {
            EndPaced();
        }
    }

    private void RandomMoveCore(int moves, int variance, bool horizontal)
    {
        for (var i = 0; i < moves; i++)
        {
            if (horizontal)
            {
                Move(random.Next(-5, 5), -variance);
                i++;
                Move(random.Next(-5, 5), variance);
            }
            else
            {
                Move(variance, random.Next(-5, 5));
                i++;
                Move(variance, random.Next(-5, 5));
            }
        }
    }

}
