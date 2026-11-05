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
/// settings 의 카메라 초기값과 MoveCamera 이벤트를 정리해 두고 특정 시각의 카메라를 계산한다 (얼불춤 ffxCameraPlus).
/// 만든 뒤에는 바뀌지 않으므로 아무 스레드에서나 불러도 된다.
/// <para>
/// 얼불춤 카메라는 "카메라 부모" 위치 P 를 트윈한다. 플레이어를 따라가는 동안 P 는 플레이어 기준 오프셋이고,
/// 고정(Tile, Global)일 때는 월드 좌표다. 플레이어 위치는 호출자가 넘긴다 (얼불춤처럼 부드럽게 따라가게 하려면 미리 부드럽게 만들어서).
/// </para>
/// <para>
/// X, Y, 회전, 줌은 각각 트윈 하나씩만 갖는다. 새 트윈이 시작되면 그 속성의 이전 트윈은 끝값으로 바로 완료된다
/// (DOTween <c>Kill(complete: true)</c>). 바꾸지 않는 속성의 트윈은 계속 진행된다.
/// </para>
/// </summary>
public sealed class CameraTimeline
{
    /// <summary>[Start, Start + Duration) 동안 From → To. Follow 면 값에 플레이어 위치를 더한다.</summary>
    private readonly record struct Segment(double Start, double Duration, Ease Ease, bool FromFollow, double From, bool ToFollow, double To);

    private readonly Segment[] _x, _y, _rotation, _zoom;

    private CameraTimeline(Segment[] x, Segment[] y, Segment[] rotation, Segment[] zoom)
    {
        _x = x;
        _y = y;
        _rotation = rotation;
        _zoom = zoom;
    }

    /// <summary>songTimeMs 시점의 카메라. player 는 그 시점 플레이어(중심 행성) 위치.</summary>
    public CameraState Evaluate(double songTimeMs, Vec2 player) => new(
        new Vec2(Sample(_x, songTimeMs, player.X), Sample(_y, songTimeMs, player.Y)),
        Sample(_rotation, songTimeMs, 0),
        Sample(_zoom, songTimeMs, 0));

    private static double Sample(Segment[] segments, double t, double player)
    {
        // 0 번은 settings 초기값 (항상 시작됨)
        int lo = 1, hi = segments.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (segments[mid].Start <= t)
                lo = mid + 1;
            else
                hi = mid;
        }
        var s = segments[lo - 1];
        var to = s.To + (s.ToFollow ? player : 0);
        var p = s.Duration <= 0 ? 1 : Math.Clamp((t - s.Start) / s.Duration, 0, 1);
        if (p >= 1)
            return to;
        // 따라가기 ↔ 고정이 바뀌는 트윈은 얼불춤처럼 그 순간 화면 위치에서 출발하도록 양쪽 다 지금 플레이어 위치로 맞춘다
        var from = s.From + (s.FromFollow ? player : 0);
        return from + (to - from) * Easing.Evaluate(s.Ease, p);
    }

    internal static CameraTimeline Build(Level level, Tile[] tiles)
    {
        var settings = level.Settings;
        var lastFloor = tiles.Length - 1;
        var pitch = settings.Pitch;
        var builder = new Builder(settings.LegacyCameraRelativeTo);

        // settings 값은 0 번 타일에 걸린 길이 0 짜리 MoveCamera 와 같다
        builder.Apply(double.NegativeInfinity, 0, new MoveCameraEvent(
            0, 0, settings.CameraRelativeTo, settings.CameraPosition.X, settings.CameraPosition.Y,
            settings.CameraRotation, settings.CameraZoom, Ease.Linear, 0), tiles[0].Position);

        var timed = new List<(double Start, int Order, MoveCameraEvent Event)>();
        var order = 0;
        foreach (var e in level.Events)
        {
            if (e is MoveCameraEvent m && m.Floor <= lastFloor)
                timed.Add((tiles[m.Floor].TimeMs + m.AngleOffset / 180.0 * MsPerBeat(tiles[m.Floor], pitch), order++, m));
        }
        timed.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Order.CompareTo(b.Order));
        foreach (var (start, _, m) in timed)
            builder.Apply(start, m.DurationBeats * MsPerBeat(tiles[m.Floor], pitch), m, tiles[m.Floor].Position);

        return new CameraTimeline(builder.X.ToArray(), builder.Y.ToArray(), builder.Rotation.ToArray(), builder.Zoom.ToArray());
    }

    private static double MsPerBeat(Tile tile, double pitch) => 60000.0 / (tile.Bpm * pitch / 100.0);

    /// <summary>ffxCameraPlus.StartEffect 를 시간순으로 흉내 내며 트윈을 쌓는다.</summary>
    private sealed class Builder(bool legacyRelativeTo)
    {
        public readonly List<Segment> X = [], Y = [], Rotation = [], Zoom = [];

        private bool _follow = true;
        private Vec2 _parent;   // 카메라 부모 위치: 따라가는 중이면 오프셋, 아니면 월드 좌표
        private double _rotation;
        private double _zoom = 1;
        private CameraRelativeTo _lastType = CameraRelativeTo.Player;
        private int _lastTileFloor = -1;

        public void Apply(double start, double duration, MoveCameraEvent m, Vec2 tilePosition)
        {
            var positionUsed = m.PositionX is not null || m.PositionY is not null;
            var type = m.RelativeTo;

            // 기준이 직전과 같고 위치 한 축만 주면 기준을 바꾸지 않은 것으로 본다 (빈 축을 0 으로 만들지 않는다)
            if (type is { } same && same == _lastType && positionUsed && (m.PositionX is null || m.PositionY is null)
                && same is not (CameraRelativeTo.Global or CameraRelativeTo.LastPosition or CameraRelativeTo.LastPositionNoRotation)
                && (same != CameraRelativeTo.Tile || m.Floor == _lastTileFloor))
                type = null;

            double? x = m.PositionX, y = m.PositionY;
            if (type is not null)
            {
                // 기준을 정하면 빈 축은 0
                x ??= 0;
                y ??= 0;
            }

            var wasFollow = _follow;
            double? targetX, targetY;
            var rotationBase = 0.0;
            if (legacyRelativeTo && type is null)
            {
                // 옛 레벨: 기준 없이 위치만 주면 지금 위치에서 더한다
                targetX = positionUsed ? _parent.X + x : _parent.X;
                targetY = positionUsed ? _parent.Y + y : _parent.Y;
            }
            else
            {
                switch (type ?? _lastType)
                {
                    case CameraRelativeTo.Player:
                        _follow = true;
                        (targetX, targetY) = (x, y);
                        break;
                    case CameraRelativeTo.Tile:
                        _follow = false;
                        if (type is not null)
                            _lastTileFloor = m.Floor;
                        (targetX, targetY) = (tilePosition.X + x, tilePosition.Y + y);
                        break;
                    case CameraRelativeTo.Global:
                        _follow = false;
                        (targetX, targetY) = (x, y);
                        break;
                    default: // LastPosition, LastPositionNoRotation
                        if ((type ?? _lastType) == CameraRelativeTo.LastPosition)
                            rotationBase = _rotation; // 회전도 지금 각도에 더한다
                        targetX = positionUsed ? _parent.X + x : _parent.X;
                        targetY = positionUsed ? _parent.Y + y : _parent.Y;
                        break;
                }
            }
            if (type is { } used)
                _lastType = used;

            if (positionUsed || type is not null)
            {
                if (targetX is { } tx)
                {
                    X.Add(new Segment(start, duration, m.Ease, wasFollow, _parent.X, _follow, tx));
                    _parent = _parent with { X = tx };
                }
                if (targetY is { } ty)
                {
                    Y.Add(new Segment(start, duration, m.Ease, wasFollow, _parent.Y, _follow, ty));
                    _parent = _parent with { Y = ty };
                }
            }

            // LastPosition 계열은 회전 값이 없어도 회전 트윈을 만든다 (NoRotation 은 0 으로 돌아간다)
            if (m.Rotation is not null || (type is CameraRelativeTo.LastPosition or CameraRelativeTo.LastPositionNoRotation))
            {
                var to = (m.Rotation ?? 0) + rotationBase;
                Rotation.Add(new Segment(start, duration, m.Ease, false, _rotation, false, to));
                _rotation = to;
            }
            if (m.Zoom is { } zoom)
            {
                Zoom.Add(new Segment(start, duration, m.Ease, false, _zoom, false, zoom));
                _zoom = zoom;
            }

            // 0 번(settings)이 항상 있도록 비어 있는 속성은 지금 값으로 채운다
            if (start == double.NegativeInfinity)
            {
                if (X.Count == 0) X.Add(new Segment(start, 0, Ease.Linear, _follow, _parent.X, _follow, _parent.X));
                if (Y.Count == 0) Y.Add(new Segment(start, 0, Ease.Linear, _follow, _parent.Y, _follow, _parent.Y));
                if (Rotation.Count == 0) Rotation.Add(new Segment(start, 0, Ease.Linear, false, _rotation, false, _rotation));
                if (Zoom.Count == 0) Zoom.Add(new Segment(start, 0, Ease.Linear, false, _zoom, false, _zoom));
            }
        }
    }
}
