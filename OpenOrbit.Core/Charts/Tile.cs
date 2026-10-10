using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts;

/// <summary>
/// 컴파일된 타일 한 칸. "Travel*" 값은 이 타일에서 다음 타일로 넘어가는 구간 기준.
/// </summary>
/// <param name="Angle">원본 angleData 값 (0번 타일은 0, 미드스핀은 999).</param>
/// <param name="TimeMs">행성이 이 타일에 도착해야 하는 곡 기준 시각(ms).</param>
/// <param name="Bpm">이 타일을 떠날 때 적용되는 BPM (피치 미적용).</param>
/// <param name="EntryAngle">회전하는 행성의 출발 각도(도).</param>
/// <param name="TravelAngle">다음 타일까지 회전하는 각도(도). Pause/Hold 포함. 마지막 타일은 0.</param>
public readonly record struct Tile(
    int Floor,
    double Angle,
    bool IsMidspin,
    Vec2 Position,
    double TimeMs,
    double Bpm,
    bool Clockwise,
    double EntryAngle,
    double TravelAngle,
    double TravelMs)
{
    public double TravelBeats => TravelAngle / 180.0;
}
