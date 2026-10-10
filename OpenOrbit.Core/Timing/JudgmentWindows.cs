namespace OpenOrbit.Core.Timing;

/// <summary>
/// 각도 기준 판정 범위(도). 얼불춤은 BPM 이 빨라질수록 같은 각도가 더 짧은 시간이 되므로 각도로 판정한다.
/// TODO: 기본값은 임시값. 실제 게임(난이도별, ms 하한 등)과 맞춰야 함.
/// </summary>
public sealed record JudgmentWindows
{
    public static readonly JudgmentWindows Strict = new();

    public double PerfectDeg { get; init; } = 30;
    public double NearPerfectDeg { get; init; } = 45;
    public double VeryDeg { get; init; } = 60;

    /// <summary>이보다 일찍 누르면 입력을 무시한다 (TooEarly, 타일 진행 없음).</summary>
    public double TooEarlyDeg { get; init; } = 90;

    public HitMargin Classify(double errorDeg)
    {
        var abs = Math.Abs(errorDeg);
        var early = errorDeg < 0;

        if (abs <= PerfectDeg) return HitMargin.Perfect;
        if (abs <= NearPerfectDeg) return early ? HitMargin.EarlyPerfect : HitMargin.LatePerfect;
        if (abs <= VeryDeg) return early ? HitMargin.VeryEarly : HitMargin.VeryLate;
        return early ? HitMargin.TooEarly : HitMargin.TooLate;
    }
}
