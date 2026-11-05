using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Levels.Events;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts.Track;

/// <summary>카메라의 특정 시각 상태.</summary>
/// <param name="Position">카메라 중심(타일 단위, +Y 위).</param>
/// <param name="Rotation">카메라 회전(도, 반시계 방향). 화면 속 세상은 반대로 돌아 보인다.</param>
/// <param name="Zoom">1 = 100%. 클수록 넓게 보인다.</param>
public readonly record struct CameraState(Vec2 Position, double Rotation, double Zoom);

/// <summary>
/// settings 의 카메라 초기값과 MoveCamera 이벤트를 시간순 구간으로 정리해 두고 특정 시각의 카메라를 계산한다.
/// 만든 뒤에는 바뀌지 않으므로 아무 스레드에서나 불러도 된다.
/// <para>
/// 플레이어를 따라가는 구간은 위치가 플레이어 위치에 따라 달라지므로, 플레이어 위치를 호출자가 넘겨준다.
/// 얼불춤처럼 카메라가 플레이어를 부드럽게 따라가게 하려면 호출자가 그 위치를 미리 부드럽게 만들어서 넘긴다.
/// </para>
/// <para>
/// 트윈 중에 다음 트윈이 시작되면 그 순간의 카메라에서 이어받는다. 비어 있는(null) 값은 직전 구간의 값을 물려받는다.
/// 기준(relativeTo)이 바뀌는데 위치 한 축이 비어 있으면 그 축은 0 으로 본다.
/// </para>
/// </summary>
public sealed class CameraTimeline
{
    /// <summary>트윈이 이만큼 겹쳐 있으면 더 앞 구간은 보지 않는다 (재귀 깊이 제한).</summary>
    private const int MaxBlendDepth = 16;

    /// <param name="Follow">true 면 플레이어 + Offset, false 면 Anchor + Offset.</param>
    /// <param name="Anchor">고정 카메라의 기준점 (Tile 이면 그 타일 위치, Global 이면 원점).</param>
    private readonly record struct Segment(
        double Start, double Duration, Ease Ease, bool Follow, Vec2 Anchor, Vec2 Offset, double Rotation, double Zoom);

    private readonly Segment[] _segments;

    private CameraTimeline(Segment[] segments) => _segments = segments;

    /// <summary>songTimeMs 시점의 카메라. player 는 그 시점 플레이어(중심 행성) 위치.</summary>
    public CameraState Evaluate(double songTimeMs, Vec2 player)
    {
        int lo = 1, hi = _segments.Length; // 0 번은 settings 초기값 (항상 시작됨)
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (_segments[mid].Start <= songTimeMs)
                lo = mid + 1;
            else
                hi = mid;
        }
        return StateAt(lo - 1, songTimeMs, player, MaxBlendDepth);
    }

    private CameraState StateAt(int k, double t, Vec2 player, int depth)
    {
        var s = _segments[k];
        var target = new CameraState((s.Follow ? player : s.Anchor) + s.Offset, s.Rotation, s.Zoom);
        var p = s.Duration <= 0 ? 1 : Math.Clamp((t - s.Start) / s.Duration, 0, 1);
        if (p >= 1 || k == 0 || depth == 0)
            return target;

        var from = StateAt(k - 1, t, player, depth - 1);
        var e = Easing.Evaluate(s.Ease, p);
        return new CameraState(
            from.Position + (target.Position - from.Position) * e,
            from.Rotation + (target.Rotation - from.Rotation) * e,
            from.Zoom + (target.Zoom - from.Zoom) * e);
    }

    internal static CameraTimeline Build(Level level, Tile[] tiles)
    {
        var settings = level.Settings;
        var lastFloor = tiles.Length - 1;
        var pitch = settings.Pitch;

        // 초기값: settings 의 relativeTo 는 Player 나 Tile(0번 타일) 이 대부분이다
        var (follow, anchor) = Anchor(settings.CameraRelativeTo, tiles[0].Position);
        var current = new Segment(
            double.NegativeInfinity, 0, Ease.Linear, follow, anchor, settings.CameraPosition, settings.CameraRotation, settings.CameraZoom);
        var segments = new List<Segment> { current };

        var timed = new List<(double Start, int Order, MoveCameraEvent Event)>();
        var order = 0;
        foreach (var e in level.Events)
        {
            if (e is MoveCameraEvent m && m.Floor <= lastFloor)
                timed.Add((tiles[m.Floor].TimeMs + m.AngleOffset / 180.0 * MsPerBeat(tiles[m.Floor], pitch), order++, m));
        }
        timed.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Order.CompareTo(b.Order));

        foreach (var (start, _, m) in timed)
        {
            var (newFollow, newAnchor, newOffset) = m.RelativeTo switch
            {
                // 기준은 그대로, 빈 축은 직전 값 유지
                null => (current.Follow, current.Anchor,
                    new Vec2(m.PositionX ?? current.Offset.X, m.PositionY ?? current.Offset.Y)),
                CameraRelativeTo.LastPosition or CameraRelativeTo.LastPositionNoRotation => (current.Follow, current.Anchor,
                    current.Offset + new Vec2(m.PositionX ?? 0, m.PositionY ?? 0)),
                // 기준이 바뀌면 빈 축은 0
                var relativeTo => Combine(Anchor(relativeTo.Value, tiles[m.Floor].Position), new Vec2(m.PositionX ?? 0, m.PositionY ?? 0)),
            };

            current = new Segment(
                start,
                m.DurationBeats * MsPerBeat(tiles[m.Floor], pitch),
                m.Ease,
                newFollow,
                newAnchor,
                newOffset,
                m.Rotation ?? current.Rotation,
                m.Zoom ?? current.Zoom);
            segments.Add(current);
        }

        return new CameraTimeline(segments.ToArray());
    }

    private static double MsPerBeat(Tile tile, double pitch) => 60000.0 / (tile.Bpm * pitch / 100.0);

    private static (bool Follow, Vec2 Anchor, Vec2 Offset) Combine((bool Follow, Vec2 Anchor) a, Vec2 offset) =>
        (a.Follow, a.Anchor, offset);

    /// <summary>기준 → (플레이어를 따라가는지, 고정 카메라의 기준점).</summary>
    private static (bool Follow, Vec2 Anchor) Anchor(CameraRelativeTo relativeTo, Vec2 tilePosition) => relativeTo switch
    {
        CameraRelativeTo.Tile => (false, tilePosition),
        CameraRelativeTo.Global => (false, Vec2.Zero),
        _ => (true, Vec2.Zero),
    };
}
