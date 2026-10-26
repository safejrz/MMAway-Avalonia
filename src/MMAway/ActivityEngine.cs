using Avalonia;
using MMAway.Input;

namespace MMAway;

/// <summary>
/// Generates the pseudo-random mouse/typing activity. Port of the original WinForms timer + MouseMoves logic.
/// </summary>
public sealed class ActivityEngine
{
    private readonly IInputInjector input;
    private readonly Random random = new();
    private readonly Func<PixelRect> getWindowBounds;
    private readonly Action<string> appendText;
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

    public ActivityEngine(IInputInjector input, Func<PixelRect> getWindowBounds,
        Action<string> appendText, Action clearText)
    {
        this.input = input;
        this.getWindowBounds = getWindowBounds;
        this.appendText = appendText;
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

    // Side length for patterns so they always fit inside the window.
    private int PatternSize()
    {
        var a = Area;
        return Math.Max(20, Math.Min(a.Width, a.Height) * 3 / 4);
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
    }

    private void RunPattern(CancellationToken ct)
    {
        if (random.Next(1, 9) >= 5) MoveInSquare(ct);
        else MoveInCircles(ct);

        ClickOnWindow();
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

    private void MoveInSquare(CancellationToken ct)
    {
        var size = PatternSize();
        var v = random.Next(1, 3);
        var c = Center;
        MoveTo(new PixelPoint(c.X - size / 2, c.Y - size / 2));
        BeginPaced(size * 4);
        try
        {
            SquareCore(size, v);
        }
        finally
        {
            EndPaced();
        }
    }

    private void SquareCore(int size, int v)
    {
        for (var i = 0; i < size; i++)
        {
            Move(1, -v);
            i++;
            Move(1, v);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            Move(v, 1);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-1, -v);
            i++;
            Move(-1, v);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, -1);
            i++;
            Move(v, -1);
        }
    }

    private void MoveInCircles(CancellationToken ct)
    {
        var size = PatternSize();
        var v = random.Next(1, 3);
        var c = Center;
        MoveTo(new PixelPoint(c.X - size / 2, c.Y + size / 2));
        BeginPaced(size * 4);
        try
        {
            CirclesCore(size, v);
        }
        finally
        {
            EndPaced();
        }
    }

    private void CirclesCore(int size, int v)
    {
        for (var i = 0; i < size; i++)
        {
            Move(1, -1);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            Move(v, 1);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-1, -1);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            Move(v, 1);
        }
    }
}
