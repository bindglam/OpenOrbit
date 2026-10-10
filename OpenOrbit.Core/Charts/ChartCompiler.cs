using OpenOrbit.Core.Geometry;
using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Levels.Events;

namespace OpenOrbit.Core.Charts;

/// <summary>
/// <see cref="Level"/> → <see cref="Chart"/>. 각 타일의 위치, 회전량, 판정 시각을 계산한다.
/// </summary>
public static class ChartCompiler
{
    public static Chart Compile(Level level)
    {
        var settings = level.Settings;
        var eventsByFloor = level.Events.ToLookup(e => e.Floor);
        var tiles = new Tile[level.FloorCount];

        var bpm = settings.Bpm;
        var clockwise = true;
        var time = 0.0; // 0번 타일 기준으로 계산한 뒤, 루프 후에 오프셋 기준으로 옮긴다
        var position = Vec2.Zero;
        var incomingAngle = 0.0; // 마지막으로 실제 이동한 방향. 시작 타일은 0° 로 들어온 것으로 취급

        for (var floor = 0; floor < tiles.Length; floor++)
        {
            var angle = floor == 0 ? 0 : level.Angles[floor - 1];
            var isMidspin = angle == Level.MidspinAngle;

            if (floor > 0)
            {
                if (isMidspin)
                    incomingAngle += 180; // 이동 없이 중심 행성만 바뀜 → 진입 방향 반전
                else
                {
                    incomingAngle = angle;
                    position += Vec2.FromAngle(angle);
                }
            }

            var extraAngle = 0.0;
            foreach (var e in eventsByFloor[floor])
            {
                switch (e)
                {
                    case SetSpeedEvent s:
                        bpm = s.SpeedType == SpeedType.Bpm ? s.BeatsPerMinute : bpm * s.BpmMultiplier;
                        break;
                    case TwirlEvent:
                        clockwise = !clockwise;
                        break;
                    case PauseEvent p:
                        extraAngle += p.Duration * 180;
                        break;
                    case HoldEvent h:
                        extraAngle += h.Duration * 360;
                        break;
                }
            }

            var entryAngle = Angles.Normalize(incomingAngle + 180);
            var travelAngle = floor + 1 < tiles.Length
                ? TravelAngle(entryAngle, level.Angles[floor], clockwise) + extraAngle
                : 0;
            var travelMs = travelAngle * 60000.0 / (bpm * settings.Pitch / 100.0) / 180.0;

            tiles[floor] = new Tile(
                floor, angle, isMidspin, position, time,
                bpm, clockwise, entryAngle, travelAngle, travelMs);

            time += travelMs;
        }

        // 오프셋은 첫 입력(1번 타일) 시각이다. 실제 레벨 곡도 오프셋 지점에서 첫 소리가 난다.
        var shift = settings.OffsetMs - (tiles.Length > 1 ? tiles[1].TimeMs : 0);
        for (var floor = 0; floor < tiles.Length; floor++)
            tiles[floor] = tiles[floor] with { TimeMs = tiles[floor].TimeMs + shift };

        return new Chart(level, tiles);
    }

    /// <summary>entry 에서 다음 타일 방향 next 까지 회전량(도). 같은 방향이면 한 바퀴(360°).</summary>
    private static double TravelAngle(double entry, double next, bool clockwise)
    {
        // 다음 타일이 미드스핀이면 회전 없이 즉시 넘어간다
        if (next == Level.MidspinAngle)
            return 0;

        var delta = Angles.Normalize(clockwise ? entry - next : next - entry);
        return delta == 0 ? 360 : delta;
    }
}
