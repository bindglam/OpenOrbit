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
/// settings 의 카메라 초기값과 MoveCamera 이벤트를 정리해 두고 특정 시각의 카메라를 계산한다.
/// 만든 뒤에는 바뀌지 않으므로 아무 스레드에서나 불러도 된다.
/// <para>
/// 카메라는 "기준점"을 움직인다. 플레이어를 따라가는 동안 기준점은 플레이어로부터의 오프셋이고,
/// 고정(Tile, Global)일 때는 월드 좌표다. 플레이어 위치는 호출자가 넘긴다 (부드럽게 따라가게 하려면 미리 부드럽게 만들어서).
/// </para>
/// <para>
/// X, Y, 회전, 줌은 각각 따로 움직이고 한 번에 움직임 하나만 갖는다. 새 움직임이 시작되면 그 값의 앞 움직임은
/// 그 자리에서 끝값으로 완료된다. 바꾸지 않는 값의 움직임은 계속 진행된다.
/// </para>
/// </summary>
public sealed class CameraTimeline
{
    /// <summary>값이 플레이어 위치에 붙어 있는지(오프셋) 월드에 고정인지.</summary>
    private readonly record struct Anchored(double Value, bool OnPlayer)
    {
        public double Resolve(double player) => Value + (OnPlayer ? player : 0);
    }

    /// <summary>Start 부터 Length 동안 From → To.</summary>
    private readonly record struct Keyframe(double Start, double Length, Ease Ease, Anchored From, Anchored To);

    /// <summary>값 하나의 움직임들 (시작 시각 순). 첫 원소는 settings 값이라 항상 시작된 상태다.</summary>
    private sealed class Lane(Keyframe[] keys)
    {
        public double At(double t, double player)
        {
            var lo = 1;
            var hi = keys.Length;
            while (lo < hi)
            {
                var mid = (lo + hi) >>> 1;
                if (keys[mid].Start <= t)
                    lo = mid + 1;
                else
                    hi = mid;
            }
            var k = keys[lo - 1];
            var to = k.To.Resolve(player);
            var progress = k.Length <= 0 ? 1 : Math.Clamp((t - k.Start) / k.Length, 0, 1);
            if (progress >= 1)
                return to;
            // 따라가기 ↔ 고정이 바뀌는 움직임도 양 끝을 지금 플레이어 위치로 풀어서, 그 순간 화면 위치에서 출발하게 한다
            var from = k.From.Resolve(player);
            return from + (to - from) * Easing.Evaluate(k.Ease, progress);
        }
    }

    private readonly Lane _x, _y, _rotation, _zoom;

    private CameraTimeline(Lane x, Lane y, Lane rotation, Lane zoom)
    {
        _x = x;
        _y = y;
        _rotation = rotation;
        _zoom = zoom;
    }

    /// <summary>songTimeMs 시점의 카메라. player 는 그 시점 플레이어(중심 행성) 위치.</summary>
    public CameraState Evaluate(double songTimeMs, Vec2 player) => new(
        new Vec2(_x.At(songTimeMs, player.X), _y.At(songTimeMs, player.Y)),
        _rotation.At(songTimeMs, 0),
        _zoom.At(songTimeMs, 0));

    internal static CameraTimeline Build(Level level, Tile[] tiles)
    {
        var settings = level.Settings;
        var pitch = settings.Pitch;
        double BeatMs(Tile tile) => 60000.0 / (tile.Bpm * pitch / 100.0);

        var rig = new Rig(settings.LegacyCameraRelativeTo);

        // settings 값은 처음부터 걸려 있는 길이 0 짜리 MoveCamera 로 본다
        rig.Move(double.NegativeInfinity, 0, new MoveCameraEvent(
            0, 0, settings.CameraRelativeTo, settings.CameraPosition.X, settings.CameraPosition.Y,
            settings.CameraRotation, settings.CameraZoom, Ease.Linear, 0), tiles[0].Position);
        rig.FillEmptyLanes();

        // 이벤트는 실제로 시작하는 시각 순서대로 적용한다 (같은 시각이면 레벨에 적힌 순서)
        var moves = level.Events
            .OfType<MoveCameraEvent>()
            .Where(m => m.Floor < tiles.Length)
            .Select(m => (Event: m, Start: tiles[m.Floor].TimeMs + m.AngleOffset / 180.0 * BeatMs(tiles[m.Floor])))
            .OrderBy(x => x.Start);
        foreach (var (m, start) in moves)
            rig.Move(start, m.DurationBeats * BeatMs(tiles[m.Floor]), m, tiles[m.Floor].Position);

        return new CameraTimeline(
            new Lane([.. rig.X]), new Lane([.. rig.Y]), new Lane([.. rig.Rotation]), new Lane([.. rig.Zoom]));
    }

    /// <summary>
    /// 카메라 상태를 들고 MoveCamera 를 하나씩 적용하며 각 값의 움직임을 쌓는다.
    /// </summary>
    private sealed class Rig(bool legacyRelativeTo)
    {
        public readonly List<Keyframe> X = [], Y = [], Rotation = [], Zoom = [];

        private bool _onPlayer = true;
        private Vec2 _point;       // 기준점: 따라가는 중이면 플레이어로부터의 오프셋, 아니면 월드 좌표
        private double _angle;
        private double _zoom = 1;
        private CameraRelativeTo _mode = CameraRelativeTo.Player;
        private int _anchorFloor = -1;  // 마지막으로 Tile 기준을 정한 타일

        public void Move(double start, double length, MoveCameraEvent m, Vec2 tilePosition)
        {
            var hasPosition = m.PositionX is not null || m.PositionY is not null;
            var mode = EffectiveRelativeTo(m, hasPosition);

            // 기준을 정하면 비운 축은 0 이다
            var offset = (X: m.PositionX, Y: m.PositionY);
            if (mode is not null)
                offset = (offset.X ?? 0, offset.Y ?? 0);

            var wasOnPlayer = _onPlayer;
            var addAngle = false;
            (double? X, double? Y) target;
            if (legacyRelativeTo && mode is null)
            {
                // 옛 레벨: 기준 없이 위치만 주면 지금 위치에 더한다
                target = Shifted(offset, hasPosition);
            }
            else
            {
                var current = mode ?? _mode;
                switch (current)
                {
                    case CameraRelativeTo.Player:
                        _onPlayer = true;
                        target = offset;
                        break;
                    case CameraRelativeTo.Tile:
                        _onPlayer = false;
                        if (mode is not null)
                            _anchorFloor = m.Floor;
                        target = (tilePosition.X + offset.X, tilePosition.Y + offset.Y);
                        break;
                    case CameraRelativeTo.Global:
                        _onPlayer = false;
                        target = offset;
                        break;
                    default:
                        // LastPosition 은 회전도 지금 각도에 더하고, NoRotation 은 회전을 그대로 정한다
                        addAngle = current == CameraRelativeTo.LastPosition;
                        target = Shifted(offset, hasPosition);
                        break;
                }
            }
            if (mode is { } chosen)
                _mode = chosen;

            if (hasPosition || mode is not null)
            {
                if (target.X is { } x)
                {
                    X.Add(new Keyframe(start, length, m.Ease, new(_point.X, wasOnPlayer), new(x, _onPlayer)));
                    _point = _point with { X = x };
                }
                if (target.Y is { } y)
                {
                    Y.Add(new Keyframe(start, length, m.Ease, new(_point.Y, wasOnPlayer), new(y, _onPlayer)));
                    _point = _point with { Y = y };
                }
            }

            // LastPosition 계열은 회전 값을 안 줘도 회전을 정한다 (NoRotation 은 0 으로 돌아간다)
            if (m.Rotation is not null || mode is CameraRelativeTo.LastPosition or CameraRelativeTo.LastPositionNoRotation)
            {
                var angle = (m.Rotation ?? 0) + (addAngle ? _angle : 0.0);
                Rotation.Add(new Keyframe(start, length, m.Ease, new(_angle, false), new(angle, false)));
                _angle = angle;
            }
            if (m.Zoom is { } zoom)
            {
                Zoom.Add(new Keyframe(start, length, m.Ease, new(_zoom, false), new(zoom, false)));
                _zoom = zoom;
            }
        }

        /// <summary>
        /// 이벤트가 새로 정하는 기준. null 이면 기준을 바꾸지 않는다.
        /// 지금과 같은 기준을 다시 주면서 위치 한 축만 주면 기준을 바꾸지 않은 것으로 본다 (빈 축을 0 으로 만들지 않는다).
        /// 단 Global 과 LastPosition 계열, 그리고 다른 타일을 가리키는 Tile 은 새로 정한 것이다.
        /// </summary>
        private CameraRelativeTo? EffectiveRelativeTo(MoveCameraEvent m, bool hasPosition)
        {
            if (m.RelativeTo is not { } requested || requested != _mode)
                return m.RelativeTo;
            var oneAxis = hasPosition && (m.PositionX is null || m.PositionY is null);
            var repeatable = requested switch
            {
                CameraRelativeTo.Player => true,
                CameraRelativeTo.Tile => m.Floor == _anchorFloor,
                _ => false,
            };
            return oneAxis && repeatable ? null : requested;
        }

        /// <summary>지금 기준점에 오프셋을 더한 위치. 위치를 안 줬으면 지금 자리, 한 축만 비웠으면 그 축은 그대로.</summary>
        private (double? X, double? Y) Shifted((double? X, double? Y) offset, bool hasPosition) =>
            hasPosition ? (_point.X + offset.X, _point.Y + offset.Y) : (_point.X, _point.Y);

        /// <summary>settings 가 정하지 않은 값도 첫 원소가 있도록 지금 값으로 채운다.</summary>
        public void FillEmptyLanes()
        {
            var start = double.NegativeInfinity;
            if (X.Count == 0) X.Add(Hold(start, new(_point.X, _onPlayer)));
            if (Y.Count == 0) Y.Add(Hold(start, new(_point.Y, _onPlayer)));
            if (Rotation.Count == 0) Rotation.Add(Hold(start, new(_angle, false)));
            if (Zoom.Count == 0) Zoom.Add(Hold(start, new(_zoom, false)));
        }

        private static Keyframe Hold(double start, Anchored value) => new(start, 0, Ease.Linear, value, value);
    }
}
