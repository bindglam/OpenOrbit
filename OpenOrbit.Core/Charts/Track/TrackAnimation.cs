using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts.Track;

/// <summary>타일이 나타나는 애니메이션 (settings.trackAnimation, AnimateTrack).</summary>
public enum TrackAppearAnimation
{
    None,
    Assemble,
    Assemble_Far,
    Extend,
    Grow,
    Grow_Spin,
    Fade,
    Drop,
    Rise,
}

/// <summary>타일이 사라지는 애니메이션 (settings.trackDisappearAnimation, AnimateTrack).</summary>
public enum TrackDisappearAnimation
{
    None,
    Scatter,
    Scatter_Far,
    Retract,
    Shrink,
    Shrink_Spin,
    Fade,
}

/// <summary>애니메이션이 타일에 주는 변화. 오프셋은 타일 단위, 회전은 반시계 방향(도).</summary>
public readonly record struct TileTransform(Vec2 Offset, double Rotation, double Scale, double Opacity)
{
    public static readonly TileTransform Identity = new(Vec2.Zero, 0, 1, 1);
    public static readonly TileTransform Hidden = new(Vec2.Zero, 0, 0, 0);
}

/// <summary>
/// 등장/퇴장 애니메이션의 모양. 얼불춤의 정확한 곡선이 아니라 이름에 맞춘 근사다.
/// </summary>
public static class TrackAnimations
{
    private const double NearDistance = 2;
    private const double FarDistance = 6;
    private const double SpinDegrees = 180;

    /// <param name="progress">0 = 막 나타나기 시작, 1 = 다 나타남.</param>
    public static TileTransform Appear(TrackAppearAnimation animation, double progress, int floor)
    {
        if (animation == TrackAppearAnimation.None || progress >= 1)
            return TileTransform.Identity;
        if (progress <= 0)
            return TileTransform.Hidden;

        var p = Easing.Evaluate(Ease.OutCubic, progress);
        var rest = 1 - p;
        return animation switch
        {
            TrackAppearAnimation.Assemble => new(ScatterDirection(floor) * (NearDistance * rest), 0, 1, p),
            TrackAppearAnimation.Assemble_Far => new(ScatterDirection(floor) * (FarDistance * rest), 0, 1, p),
            TrackAppearAnimation.Extend or TrackAppearAnimation.Grow => new(Vec2.Zero, 0, p, 1),
            TrackAppearAnimation.Grow_Spin => new(Vec2.Zero, -SpinDegrees * rest, p, 1),
            TrackAppearAnimation.Fade => new(Vec2.Zero, 0, 1, p),
            TrackAppearAnimation.Drop => new(new Vec2(0, NearDistance * rest), 0, 1, p),
            TrackAppearAnimation.Rise => new(new Vec2(0, -NearDistance * rest), 0, 1, p),
            _ => TileTransform.Identity,
        };
    }

    /// <param name="progress">0 = 사라지기 시작, 1 = 다 사라짐.</param>
    public static TileTransform Disappear(TrackDisappearAnimation animation, double progress, int floor)
    {
        if (animation == TrackDisappearAnimation.None || progress <= 0)
            return TileTransform.Identity;
        if (progress >= 1)
            return TileTransform.Hidden;

        var q = Easing.Evaluate(Ease.InCubic, progress);
        var rest = 1 - q;
        return animation switch
        {
            TrackDisappearAnimation.Scatter => new(ScatterDirection(floor) * (NearDistance * q), 0, 1, rest),
            TrackDisappearAnimation.Scatter_Far => new(ScatterDirection(floor) * (FarDistance * q), 0, 1, rest),
            TrackDisappearAnimation.Retract or TrackDisappearAnimation.Shrink => new(Vec2.Zero, 0, rest, 1),
            TrackDisappearAnimation.Shrink_Spin => new(Vec2.Zero, SpinDegrees * q, rest, 1),
            TrackDisappearAnimation.Fade => new(Vec2.Zero, 0, 1, rest),
            _ => TileTransform.Identity,
        };
    }

    /// <summary>타일마다 고정된 무작위 방향 (매번 같은 결과가 나오도록 타일 번호로 만든다).</summary>
    private static Vec2 ScatterDirection(int floor)
    {
        var h = (uint)floor * 2654435761u;
        h ^= h >> 15;
        return Vec2.FromAngle(h % 3600 / 10.0);
    }
}
