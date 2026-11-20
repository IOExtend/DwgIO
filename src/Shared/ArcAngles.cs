namespace DwgIO.Scene;

internal static class ArcAngles
{
    public static (double Start, double Span) Normalize(double start, double end)
    {
        start = PositiveAngle(start);
        double span = PositiveAngle(end) - start;
        return (start, span <= 0 ? span + Math.Tau : span);
    }

    public static double PositiveAngle(double radians)
    {
        double turn = radians % Math.Tau;
        return turn < 0 ? turn + Math.Tau : turn;
    }
}
