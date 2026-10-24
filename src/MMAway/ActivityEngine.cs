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
    private readonly Func<PixelPoint> getWindowPosition;
    private readonly Action<string> appendText;
    private readonly Action clearText;
    private readonly PixelPoint home;

    private CancellationTokenSource? cts;
    private Task? loop;
    private int pass;

    public ActivityEngine(IInputInjector input, PixelPoint home, Func<PixelPoint> getWindowPosition,
        Action<string> appendText, Action clearText)
    {
        this.input = input;
        this.home = home;
        this.getWindowPosition = getWindowPosition;
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

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            input.SetCursorPosition(home);
            RunPattern(ct);

            var delay = random.Next(10000, 15000);
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(delay, ct).ConfigureAwait(false);
                Step(ct);
                delay = random.Next(5000, 15000);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void Step(CancellationToken ct)
    {
        RandomMove(ct);
        WriteRandomText(ct);

        if (pass == 0)
        {
            input.SetCursorPosition(home);
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

    private void Move(int dx, int dy)
    {
        var p = input.GetCursorPosition();
        input.SetCursorPosition(new PixelPoint(p.X + dx, p.Y + dy));
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
        var window = getWindowPosition();
        input.SetCursorPosition(new PixelPoint(window.X + 10, window.Y + 10));
        input.LeftClick();
        input.SetCursorPosition(original);
    }

    private void RandomMove(CancellationToken ct)
    {
        var moves = random.Next(20, 55);
        var variance = random.Next(1, 3);
        var horizontal = random.Next(1, 6) >= 4;

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

            if (i % 3 == 0) Pause(3, ct);
        }
    }

    private void MoveInSquare(CancellationToken ct)
    {
        const int size = 350;
        var v = random.Next(1, 3);

        for (var i = 0; i < size; i++)
        {
            Move(1, -v);
            i++;
            Move(1, v);
            if (i % 7 == 0) Pause(1, ct);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            if (i % 9 == 0) Pause(1, ct);
            Move(v, 1);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-1, -v);
            i++;
            Move(-1, v);
            if (i % 8 == 0) Pause(1, ct);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, -1);
            i++;
            if (i % 7 == 0) Pause(1, ct);
            Move(v, -1);
        }
    }

    private void MoveInCircles(CancellationToken ct)
    {
        const int size = 300;
        var v = random.Next(1, 3);

        for (var i = 0; i < size; i++)
        {
            Move(1, -1);
            if (i % 7 == 0) Pause(1, ct);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            Move(v, 1);
            if (i % 11 == 0) Pause(1, ct);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-1, -1);
            if (i % 7 == 0) Pause(1, ct);
        }

        for (var i = 0; i < size; i++)
        {
            Move(-v, 1);
            i++;
            if (i % 9 == 0) Pause(1, ct);
            Move(v, 1);
        }
    }
}
