using OpenOrbit.Core.Charts.Levels.Events;

namespace OpenOrbit.Core.Charts.Levels;

/// <summary>
/// 파싱된 레벨 원본 데이터. 타이밍 계산은 하지 않는다 (<see cref="Timing.ChartCompiler"/> 참고).
/// </summary>
public sealed class Level
{
    /// <summary>angleData 에서 미드스핀을 나타내는 값.</summary>
    public const double MidspinAngle = 999;

    public required LevelSettings Settings { get; init; }

    /// <summary>
    /// angleData. Angles[i] 는 (i+1)번 타일이 i번 타일 기준으로 놓인 방향(도).
    /// 0번 타일(시작 타일)은 암묵적으로 0°.
    /// </summary>
    public required IReadOnlyList<double> Angles { get; init; }

    public required IReadOnlyList<LevelEvent> Events { get; init; }

    public int FloorCount => Angles.Count + 1;
}
