using Avalonia;

namespace MMAway;

public enum MovementPattern
{
    Square,
    Circle,
    Triangle,
    VerticalHourglass,
    HorizontalHourglass,
    SpiralOut,
    SpiralIn,
    Oval,
    IceCreamCone,
    FigureEight
}

/// <summary>
/// Generates reusable, normalized cursor paths. Returned points are centered around
/// (0, 0), generally within [-1, 1], and can be scaled to fit a target region.
/// </summary>
public static class MovementPatternLibrary
{
    public const int DefaultSegments = 240;

    public static IReadOnlyList<Point> CreateSquare(int segments = DefaultSegments) =>
        SamplePolyline([new(-0.8, -0.8), new(0.8, -0.8), new(0.8, 0.8), new(-0.8, 0.8)],
            segments, close: true);

    public static IReadOnlyList<Point> CreateCircle(int segments = DefaultSegments) =>
        SampleParametric(segments, t => new Point(Math.Cos(t), Math.Sin(t)), 2 * Math.PI);

    public static IReadOnlyList<Point> CreateTriangle(int segments = DefaultSegments) =>
        SamplePolyline([new(0, -0.9), new(0.82, 0.75), new(-0.82, 0.75)], segments, close: true);

    public static IReadOnlyList<Point> CreateVerticalHourglass(int segments = DefaultSegments) =>
        SamplePolyline(
        [
            new(-0.82, -0.85), new(0.82, -0.85), new(-0.72, 0), new(0.82, 0.85),
            new(-0.82, 0.85), new(0.72, 0), new(-0.82, -0.85)
        ], segments);

    public static IReadOnlyList<Point> CreateHorizontalHourglass(int segments = DefaultSegments) =>
        SamplePolyline([new(-0.52, 0), new(0, -0.82), new(0.85, 0), new(0, 0.82)],
            segments, close: true);

    public static IReadOnlyList<Point> CreateSpiralOut(int segments = DefaultSegments) =>
        SampleByIndex(segments, i =>
        {
            var progress = (double)i / (segments - 1);
            var t = progress * 4 * Math.PI;
            var radius = 0.08 + 0.88 * progress;
            return new Point(radius * Math.Cos(t), radius * Math.Sin(t));
        });

    public static IReadOnlyList<Point> CreateSpiralIn(int segments = DefaultSegments) =>
        SampleByIndex(segments, i =>
        {
            var progress = (double)i / (segments - 1);
            var t = progress * 4 * Math.PI;
            var radius = 0.96 - 0.88 * progress;
            return new Point(radius * Math.Cos(t), radius * Math.Sin(t));
        });

    public static IReadOnlyList<Point> CreateOval(int segments = DefaultSegments) =>
        SampleParametric(segments, t => new Point(Math.Cos(t), Math.Sin(t)), 2 * Math.PI);

    public static IReadOnlyList<Point> CreateIceCreamCone(int segments = DefaultSegments)
    {
        if (segments < 8)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), "An ice-cream-cone path requires at least eight points.");
        }
        var lineSteps = segments / 4;
        var arcSteps = segments / 2;
        var path = new List<Point>(segments);

        for (var i = 0; i < lineSteps; i++)
        {
            var t = (double)i / lineSteps;
            path.Add(new Point(-0.5 * t, 1.0 - 1.2 * t));
        }

        for (var i = 0; i < arcSteps; i++)
        {
            var angle = Math.PI + Math.PI * i / (arcSteps - 1);
            path.Add(new Point(0.5 * Math.Cos(angle), -0.2 + 0.5 * Math.Sin(angle)));
        }

        for (var i = 0; i < lineSteps; i++)
        {
            var t = (double)i / (lineSteps - 1);
            path.Add(new Point(0.5 - 0.5 * t, -0.2 + 1.2 * t));
        }

        path[^1] = path[0];
        return path;
    }

    public static IReadOnlyList<Point> CreateFigureEight(int segments = DefaultSegments) =>
        SampleParametric(segments, t => new Point(Math.Sin(t), Math.Sin(2 * t) * 0.5), 2 * Math.PI);

    private static IReadOnlyList<Point> SampleParametric(int segments, Func<double, Point> pointAt, double end)
    {
        ValidateSegments(segments);
        return SampleByIndex(segments, i => pointAt(end * i / (segments - 1)));
    }

    private static IReadOnlyList<Point> SampleByIndex(int segments, Func<int, Point> pointAt)
    {
        ValidateSegments(segments);
        return Enumerable.Range(0, segments).Select(pointAt).ToArray();
    }

    private static IReadOnlyList<Point> SamplePolyline(IReadOnlyList<Point> vertices, int count, bool close = false)
    {
        ValidateSegments(count);
        var edges = close ? vertices.Count : vertices.Count - 1;
        var result = new Point[count];

        for (var i = 0; i < count; i++)
        {
            var progress = (double)i / (count - 1) * edges;
            var edge = Math.Min((int)progress, edges - 1);
            var start = vertices[edge];
            var end = vertices[(edge + 1) % vertices.Count];
            var t = progress - edge;
            result[i] = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        }

        return result;
    }

    private static void ValidateSegments(int segments)
    {
        if (segments < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(segments), "A movement path requires at least two points.");
        }
    }
}
