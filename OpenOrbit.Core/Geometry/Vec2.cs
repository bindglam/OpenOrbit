namespace OpenOrbit.Core.Geometry;

/// <summary>긴 차트에서도 오차가 쌓이지 않도록 double 기반 2D 벡터.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 v, double s) => new(v.X * s, v.Y * s);

    /// <summary>각도(도, 반시계 방향 기준 0° = +X)에 해당하는 단위 벡터.</summary>
    public static Vec2 FromAngle(double degrees)
    {
        var rad = degrees * Math.PI / 180.0;
        return new Vec2(Math.Cos(rad), Math.Sin(rad));
    }
}
