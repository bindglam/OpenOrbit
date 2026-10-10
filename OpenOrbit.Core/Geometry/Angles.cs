namespace OpenOrbit.Core.Geometry;

public static class Angles
{
    /// <summary>각도를 [0, 360) 범위로 정규화.</summary>
    public static double Normalize(double degrees)
    {
        var r = degrees % 360.0;
        return r < 0 ? r + 360.0 : r;
    }
}
