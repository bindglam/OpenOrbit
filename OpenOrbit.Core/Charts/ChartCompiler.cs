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
        var planets = 2;
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
                {
                    // 직전 타일은 찍기만 하고 중심 행성은 그 앞 타일에 그대로 남는다 → 한 칸 뒤로, 진입 방향 반전.
                    // 회전하던 행성은 직전 타일 자리(뒤 칸에서 원래 진행 방향)에서 이어서 돈다.
                    position -= Vec2.FromAngle(incomingAngle);
                    incomingAngle += 180;
                }
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
                    case MultiPlanetEvent m:
                        planets = m.Planets;
                        break;
                }
            }

            // 3공 이상: 다음 행성이 다각형 내각만큼 회전 방향 앞에서 출발한다.
            // 미드스핀 직후에는 원래 돌던 행성이 이어서 돌므로 앞당기지 않는다 (얼불춤과 같음).
            var entryAngle = Angles.Normalize(incomingAngle + 180);
            if (!isMidspin)
                entryAngle = Angles.Normalize(entryAngle + (clockwise ? -1 : 1) * InteriorAngle(planets));

            // 미드스핀은 같은 자리(두 칸 앞) 타일의 행성이 다시 중심이 되고, 나머지는 다음 행성이 중심이 된다
            var pivotPlanet = floor == 0 ? 0
                : isMidspin && floor >= 2 ? tiles[floor - 2].PivotPlanet
                : (tiles[floor - 1].PivotPlanet + 1) % planets;

            var travelAngle = floor + 1 < tiles.Length
                ? TravelAngle(entryAngle, level.Angles[floor], clockwise) + extraAngle
                : 0;
            var travelMs = travelAngle * 60000.0 / (bpm * settings.Pitch / 100.0) / 180.0;

            tiles[floor] = new Tile(
                floor, angle, isMidspin, position, time,
                bpm, clockwise, entryAngle, travelAngle, travelMs, planets, pivotPlanet % planets);

            time += travelMs;
        }

        // 오프셋은 첫 입력(1번 타일) 시각이다. 실제 레벨 곡도 오프셋 지점에서 첫 소리가 난다.
        var shift = settings.OffsetMs - (tiles.Length > 1 ? tiles[1].TimeMs : 0);
        for (var floor = 0; floor < tiles.Length; floor++)
            tiles[floor] = tiles[floor] with { TimeMs = tiles[floor].TimeMs + shift };

        return new Chart(level, tiles);
    }

    /// <summary>
    /// 행성 n 개가 이루는 정다각형의 내각(도). 2공 0°, 3공 60°, 4공 90°.
    /// 얼불춤 scrMisc.GetInverseAnglePerBeatMultiplanet 과 같은 값.
    /// </summary>
    public static double InteriorAngle(int planets) => 180.0 * (planets - 2) / planets;

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
