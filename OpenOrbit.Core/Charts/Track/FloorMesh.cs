using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Levels.Events;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts.Track;

/// <summary>
/// 타일 한 칸의 모양: 들어오는 팔과 나가는 팔의 방향(도, 반시계, 0° = +X)과 TileDimensions 로 정한 길이/폭 배율.
/// 방향은 타일 위치가 아니라 angleData 로 정해서 PositionTrack 으로 옮겨도 그대로다.
/// 실제 크기는 트랙 스타일에 따라서도 달라진다 (<see cref="FloorMesh.Dimensions"/>).
/// </summary>
public readonly record struct FloorShape(double EntryAngle, double ExitAngle, double LengthMult = 1, double WidthMult = 1)
{
    /// <summary>두 팔 사이의 각도 (0 ~ 180도). 0 이면 막다른 타일, 180 이면 직선.</summary>
    public double Spread => Math.Min(Angles.Normalize(ExitAngle - EntryAngle), Angles.Normalize(EntryAngle - ExitAngle));

    /// <summary>
    /// 기본 모양(첫 팔 0°, 둘째 팔 +<see cref="Spread"/>)을 이 타일로 돌리는 각도(도, 반시계).
    /// 두 팔 중 반시계로 180도 미만 떨어진 쪽이 둘째 팔이 되게 첫 팔을 고른다.
    /// </summary>
    public double Rotation => Angles.Normalize(ExitAngle - EntryAngle) < 180 ? EntryAngle : ExitAngle;
}

/// <summary>타일 모양의 종류.</summary>
public enum FloorKind
{
    /// <summary>꺾인 타일: 두 팔과 둥글게 깎인 바깥 모서리.</summary>
    Corner,
    /// <summary>직선 타일.</summary>
    Straight,
    /// <summary>막다른 타일 (다음이 미드스핀): 짧은 팔 하나와 뒤쪽의 뾰족한 끝.</summary>
    DeadEnd,
    /// <summary>Gems 스타일: 정사각형 그림.</summary>
    Sprite,
}

/// <summary>
/// 회전을 뺀 타일 모양. 다각형이 아니라 거리 함수로 정의한다. 좌표는 타일 단위(+Y 위), 원점은 타일 중심이고
/// 첫 팔은 +X, 둘째 팔은 +<see cref="Spread"/>도 방향이다. 값이 같으면 모양도 같아서 메시를 공유하는 키로 쓸 수 있다.
/// <para>
/// 꺾인 타일 = (팔 0 ∪ 팔 1) ∩ 둥근 쐐기.
/// <list type="bullet">
/// <item>팔은 폭 2·HalfWidth 의 띠로 앞은 팔 끝에서 잘리고, 뒤로는 다른 팔 끝선까지 뻗는다.</item>
/// <item>쐐기는 두 팔의 바깥 옆선 사이 영역이고, 꼭짓점을 <see cref="CornerCenter"/> 중심, <see cref="CornerRadius"/> 반지름으로 둥글린다.</item>
/// </list>
/// 막다른 타일은 원점에서 <see cref="Length"/> 만큼 뻗은 팔 하나에 뒤로 HalfWidth 만큼 튀어나온 뾰족한 끝을 붙인 오각형이다
/// (원점은 <see cref="CornerCenter"/>).
/// </para>
/// <para>
/// 그림자는 타일 밖에서 외곽선까지의 거리가 <see cref="ShadowSize"/> 보다 가까운 곳 가운데, 두 팔 끝선 안쪽만이다.
/// 그래서 팔 끝에는 그림자가 없어 이웃 타일과 이어진다. 두 팔이 겹칠 만큼 좁게 꺾이면(<see cref="ArmsOverlap"/>) 안쪽에는 그림자가 없다.
/// </para>
/// </summary>
public readonly record struct FloorGeometry(
    FloorKind Kind,
    double Spread,
    double Length,
    double HalfWidth,
    Vec2 CornerCenter,
    double CornerRadius,
    bool ArmsOverlap,
    double ShadowSize)
{
    /// <summary>타일 외곽선까지의 부호 있는 거리. 안쪽이 음수다.</summary>
    public double SignedDistance(Vec2 p)
    {
        switch (Kind)
        {
            case FloorKind.Sprite:
                return Math.Max(Math.Abs(p.X), Math.Abs(p.Y)) - HalfWidth;
            case FloorKind.DeadEnd:
            {
                // 볼록 오각형이라 각 변 직선까지 거리의 최댓값
                var x = p.X - CornerCenter.X;
                var tip = (-x + Math.Abs(p.Y) - HalfWidth) / Math.Sqrt(2);
                return Math.Max(Math.Max(Math.Abs(p.Y) - HalfWidth, x - Length), tip);
            }
        }

        var dir1 = Direction(Spread);
        var along0 = p.X;
        var along1 = Dot(p, dir1);
        var arm0 = Arm(along0, p.Y, along1);
        var arm1 = Arm(along1, Cross(dir1, p), along0);
        var arms = Math.Min(arm0, arm1);
        return Kind == FloorKind.Straight ? arms : Math.Max(arms, Wedge(p, dir1));
    }

    /// <summary>
    /// 그림자 안이면 외곽선까지의 거리(0 ~ <see cref="ShadowSize"/>), 아니면 null.
    /// </summary>
    public double? ShadowDistance(Vec2 p)
    {
        if (Kind == FloorKind.Sprite || ShadowSize <= 0)
            return null;
        var d = SignedDistance(p);
        if (d <= 0 || d >= ShadowSize)
            return null;
        if (Kind == FloorKind.DeadEnd)
        {
            // 뾰족한 끝의 그림자는 끝을 HalfWidth + ShadowSize 크기로 키운 모양까지라서, 끝 쪽은 빗변 방향으로 잰다
            var x = p.X - CornerCenter.X;
            var s = Math.Max(Math.Abs(p.Y) - HalfWidth, -x + Math.Abs(p.Y) - HalfWidth);
            return x <= Length && s < ShadowSize ? Math.Max(s, 0) : null;
        }
        var dir1 = Direction(Spread);
        var across1 = Cross(dir1, p);
        if (p.X > Length || Dot(p, dir1) > Length)
            return null;
        if (ArmsOverlap && p.Y > 0 && across1 < 0)
            return null; // 겹친 두 팔 사이
        return d;
    }

    /// <summary>
    /// 테두리 음영: 외곽선에서 1, 안쪽으로 HalfWidth 만큼 들어가면 0. 그림자 안이면 1 → 2. 타일과 그림자 밖이면 null.
    /// </summary>
    public double? Shade(Vec2 p)
    {
        var d = SignedDistance(p);
        if (d <= 0)
            return Kind == FloorKind.Sprite ? 0 : Math.Max(1 + d / HalfWidth, 0);
        return ShadowDistance(p) is { } s ? 1 + s / ShadowSize : null;
    }

    /// <summary>타일과 그림자를 모두 덮는 사각형 (최소, 최대).</summary>
    public (Vec2 Min, Vec2 Max) Bounds()
    {
        var reach = HalfWidth + ShadowSize;
        var points = new List<Vec2>();
        switch (Kind)
        {
            case FloorKind.Sprite:
                return (new Vec2(-HalfWidth, -HalfWidth), new Vec2(HalfWidth, HalfWidth));
            case FloorKind.DeadEnd:
                var o = CornerCenter;
                points.AddRange([o + new Vec2(Length, reach), o + new Vec2(Length, -reach),
                    o + new Vec2(-reach * Math.Sqrt(2), reach), o + new Vec2(-reach * Math.Sqrt(2), -reach)]);
                break;
            default:
                foreach (var angle in (ReadOnlySpan<double>)[0, Spread])
                {
                    var dir = Direction(angle);
                    var side = new Vec2(-dir.Y, dir.X) * reach;
                    points.AddRange([dir * Length + side, dir * Length - side]);
                }
                var r = CornerRadius + ShadowSize;
                points.AddRange([CornerCenter + new Vec2(r, r), CornerCenter - new Vec2(r, r)]);
                break;
        }
        return (new Vec2(points.Min(v => v.X), points.Min(v => v.Y)), new Vec2(points.Max(v => v.X), points.Max(v => v.Y)));
    }

    /// <summary>
    /// 팔 하나: 옆선(|across| ≤ 폭), 앞쪽 팔 끝선, 그리고 뒤쪽은 다른 팔 끝선 안까지(앞쪽 절반은 제한 없음).
    /// </summary>
    private double Arm(double along, double across, double otherAlong) =>
        Math.Max(Math.Max(Math.Abs(across) - HalfWidth, along - Length), Math.Min(-along, otherAlong - Length));

    /// <summary>
    /// 두 팔의 바깥 옆선 사이 쐐기를 <see cref="CornerRadius"/> 로 둥글린 것까지의 거리.
    /// 반지름만큼 안으로 옮긴 쐐기(꼭짓점 = <see cref="CornerCenter"/>)까지의 정확한 거리에서 반지름을 뺀다.
    /// </summary>
    private double Wedge(Vec2 p, Vec2 dir1)
    {
        var q = p - CornerCenter;
        // 바깥쪽이 양수: 첫 팔은 오른쪽(-Y), 둘째 팔은 왼쪽이 바깥
        var out0 = -q.Y;
        var out1 = Cross(dir1, q);
        double d;
        if (out0 <= 0 && out1 <= 0)
            d = Math.Max(out0, out1);
        else
        {
            var ray0 = q - new Vec2(1, 0) * Math.Max(q.X, 0);
            var ray1 = q - dir1 * Math.Max(Dot(q, dir1), 0);
            d = Math.Min(Len(ray0), Len(ray1));
        }
        return d - CornerRadius;
    }

    internal static Vec2 Direction(double degrees) => Vec2.FromAngle(degrees);
    private static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    /// <summary>dir 기준 왼쪽이 양수인 옆 방향 좌표.</summary>
    private static double Cross(Vec2 dir, Vec2 p) => dir.X * p.Y - dir.Y * p.X;
    private static double Len(Vec2 v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
}

/// <summary>
/// 타일을 그리는 사각형 하나. 좌표는 타일 단위(+Y 위)이고 타일 중심이 원점이다.
/// 사각형은 타일과 그림자를 덮을 뿐이고, 실제 모양과 음영은 렌더러가 픽셀마다 <see cref="Geometry"/> 로 정한다.
/// </summary>
public sealed record FloorMeshData(Vec2[] Vertices, int[] Indices, FloorGeometry Geometry);

/// <summary>
/// 커스텀 레벨 타일의 모양 (<see cref="FloorGeometry"/>). 렌더러는 덮는 사각형만 그리고 픽셀마다 거리를 계산하면 된다.
/// <para>
/// 크기는 얼불춤 유닛으로 정하고(<see cref="Dimensions"/>) 결과는 타일 단위로 바꾼다.
/// Gems 스타일은 각도와 상관없이 1 유닛 정사각형이다.
/// </para>
/// </summary>
public static class FloorMesh
{
    /// <summary>그림자 폭 (얼불춤 유닛).</summary>
    public const double ShadowSize = 0.11;
    /// <summary>Gems 스타일 정사각형 한 변 (얼불춤 유닛).</summary>
    public const double SpriteSize = 1;

    private const double Epsilon = 1e-4;

    /// <summary>
    /// 모든 타일의 모양. 나가는 방향은 다음 타일의 angleData, 들어오는 방향은 앞 타일이 나간 방향의 반대다.
    /// 다음이 미드스핀이면 들어온 방향으로 되돌아가는 막다른 타일이고, 마지막 타일은 직선이다.
    /// <para>TileDimensions 는 그 타일부터 뒤 타일에 계속 적용된다.</para>
    /// </summary>
    public static FloorShape[] Shapes(Chart chart)
    {
        var tiles = chart.Tiles;
        var dimensions = chart.Level.Events.OfType<TileDimensionsEvent>().ToLookup(e => e.Floor);
        var shapes = new FloorShape[tiles.Count];
        var entry = 180.0; // 0 번 타일은 왼쪽에서 들어온 것으로 본다
        double lengthMult = 1, widthMult = 1;
        for (var i = 0; i < tiles.Count; i++)
        {
            foreach (var d in dimensions[i])
                (lengthMult, widthMult) = (d.Length, d.Width);
            var exit = i + 1 >= tiles.Count ? entry + 180
                : tiles[i + 1].IsMidspin ? entry
                : tiles[i + 1].Angle;
            exit = Angles.Normalize(exit);
            shapes[i] = new FloorShape(entry, exit, lengthMult, widthMult);
            entry = Angles.Normalize(exit + 180);
        }
        return shapes;
    }

    /// <summary>
    /// 팔 길이(중심 → 팔 끝)와 폭의 절반 (얼불춤 유닛).
    /// Standard 와 Gems 는 기본 크기, Minimal 은 폭만, 나머지 스타일은 길이와 폭 모두 조금 크다.
    /// </summary>
    public static (double Length, double Width) Dimensions(TrackStyle style, TileShape tileShape, double lengthMult, double widthMult)
    {
        var (length, width) = tileShape == TileShape.Short ? (0.5, 0.413) : (0.75, 0.4125);
        return style switch
        {
            TrackStyle.Standard or TrackStyle.Gems => (length * lengthMult, width * widthMult),
            TrackStyle.Minimal => (length * lengthMult, (width + 0.0375) * widthMult),
            _ => ((length + 0.03) * lengthMult, (width + 0.0375) * widthMult),
        };
    }

    /// <summary>
    /// 바깥 모서리를 둥글리는 비율 (0 = 뾰족, 1 = 폭만큼). 많이 꺾일수록 둥글고 120° 이상 벌어지면 뾰족하다.
    /// </summary>
    public static double CornerRoundness(double spread) => spread switch
    {
        < 5 => 1,
        < 30 => Lerp(1, 0.83, Math.Sqrt((spread - 5) / 25)),
        < 45 => Lerp(0.83, 0.77, (spread - 30) / 15),
        < 90 => Lerp(0.77, 0.15, Math.Pow((spread - 45) / 45, 0.7)),
        < 120 => Lerp(0.15, 0, Math.Sqrt((spread - 90) / 30)),
        _ => 0,
    };

    /// <summary>회전을 뺀 타일 모양 (타일 단위).</summary>
    public static FloorGeometry Geometry(FloorShape shape, TrackStyle style, TileShape tileShape)
    {
        var scale = 1 / Chart.UnitsPerTileOf(tileShape);
        if (style == TrackStyle.Gems)
            return new FloorGeometry(FloorKind.Sprite, 0, 0, SpriteSize / 2 * scale, Vec2.Zero, 0, false, 0);
        var (length, width) = Dimensions(style, tileShape, shape.LengthMult, shape.WidthMult);
        var g = Geometry(Math.Round(shape.Spread, 2), length, width);
        return g with
        {
            Length = g.Length * scale,
            HalfWidth = g.HalfWidth * scale,
            CornerCenter = g.CornerCenter * scale,
            CornerRadius = g.CornerRadius * scale,
            ShadowSize = g.ShadowSize * scale,
        };
    }

    /// <summary>두 팔 사이 각도(도)와 크기(얼불춤 유닛)로 모양을 정한다. 결과도 얼불춤 유닛이다.</summary>
    public static FloorGeometry Geometry(double spread, double length, double halfWidth)
    {
        halfWidth = Math.Max(halfWidth, 0);
        length = Math.Max(length, halfWidth); // 팔이 폭보다 짧으면 모양이 무너진다

        if (spread < Epsilon)
        {
            // 팔을 2/3 로 줄이고 원점을 길이의 1/12 만큼 뒤로 민다. 뒤끝은 폭만큼 튀어나온 뾰족한 끝
            return new FloorGeometry(FloorKind.DeadEnd, 0, length * 2 / 3, halfWidth,
                new Vec2(-length / 12, 0), halfWidth, false, ShadowSize);
        }
        if (spread > 180 - Epsilon)
            return new FloorGeometry(FloorKind.Straight, 180, length, halfWidth, Vec2.Zero, 0, false, ShadowSize);

        // 바깥 옆선(첫 팔 오른쪽, 둘째 팔 왼쪽)이 만나는 점과 안쪽 옆선이 만나는 점. 두 직선의 교점은 각도의 반 기준으로 대칭이다
        var half = spread / 2 * Math.PI / 180;
        var bisector = new Vec2(Math.Cos(half), Math.Sin(half));
        var outerCorner = bisector * (-halfWidth / Math.Sin(half));
        var innerDistance = halfWidth / Math.Sin(half);
        var roundness = CornerRoundness(spread);
        // 팔 끝 안쪽 모서리보다 안쪽 옆선 교점이 멀면 두 팔이 겹친다
        var armCorner = Math.Sqrt(length * length + halfWidth * halfWidth);
        return new FloorGeometry(FloorKind.Corner, spread, length, halfWidth,
            outerCorner * (1 - roundness), halfWidth * roundness, armCorner < innerDistance, ShadowSize);
    }

    /// <summary>타일과 그림자를 덮는 사각형. 첫 팔이 +X 방향이다 (<see cref="FloorShape.Rotation"/> 만큼 돌려서 쓴다).</summary>
    public static FloorMeshData Build(FloorShape shape, TrackStyle style, TileShape tileShape) =>
        Build(Geometry(shape, style, tileShape));

    /// <inheritdoc cref="Build(FloorShape, TrackStyle, TileShape)"/>
    public static FloorMeshData Build(FloorGeometry geometry)
    {
        var (min, max) = geometry.Bounds();
        return new FloorMeshData(
            [new(min.X, min.Y), new(max.X, min.Y), new(max.X, max.Y), new(min.X, max.Y)],
            [0, 1, 2, 0, 2, 3],
            geometry);
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
