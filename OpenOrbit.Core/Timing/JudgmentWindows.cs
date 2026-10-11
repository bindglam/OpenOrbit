namespace OpenOrbit.Core.Timing;

/// <summary>
/// 각도 기준 판정 범위(도). 얼불춤은 BPM 이 빨라질수록 같은 각도가 더 짧은 시간이 되므로 각도로 판정한다.
/// <para>
/// 다만 BPM 이 아주 빠르면 각도 범위가 사람이 칠 수 없을 만큼 짧아지므로, 각 범위는 *Ms 이상의 시간을 보장한다
/// (실제 범위 = max(각도, ms 를 그 BPM 의 각도로 환산한 값)). ms 는 피치까지 반영된 실제 시간 기준.
/// </para>
/// </summary>
public sealed record JudgmentWindows
{
    public static readonly JudgmentWindows Lenient = new() { VeryMs = 91 };
    public static readonly JudgmentWindows Normal = new() { VeryMs = 65 };
    public static readonly JudgmentWindows Strict = new() { VeryMs = 40 };
    public static readonly JudgmentWindows SuperStrict = new() { PerfectMs = 0, NearPerfectMs = 0, VeryMs = 0 };

    public double PerfectDeg { get; init; } = 30;
    public double NearPerfectDeg { get; init; } = 45;
    public double VeryDeg { get; init; } = 60;

    /// <summary>각 범위의 최소 시간(ms). 0 이면 각도만 쓴다.</summary>
    public double PerfectMs { get; init; } = 25;
    public double NearPerfectMs { get; init; } = 30;
    public double VeryMs { get; init; } = 40;

    /// <summary>이보다 일찍 누르면 입력을 무시한다 (TooEarly, 타일 진행 없음).</summary>
    public double TooEarlyDeg { get; init; } = 90;

    /// <param name="msPerDegree">판정 중인 구간의 1도당 시간(ms). <see cref="Charts.Chart.MsPerDegree"/>.</param>
    public double PerfectDegAt(double msPerDegree) => Math.Max(PerfectDeg, PerfectMs / msPerDegree);
    public double NearPerfectDegAt(double msPerDegree) => Math.Max(NearPerfectDeg, NearPerfectMs / msPerDegree);
    public double VeryDegAt(double msPerDegree) => Math.Max(VeryDeg, VeryMs / msPerDegree);

    /// <summary>ms 하한 없이 각도만으로 판정.</summary>
    public HitMargin Classify(double errorDeg) => Classify(errorDeg, double.PositiveInfinity);

    /// <summary>ms 하한을 적용해 판정.</summary>
    public HitMargin Classify(double errorDeg, double msPerDegree)
    {
        var abs = Math.Abs(errorDeg);
        var early = errorDeg < 0;

        if (abs <= PerfectDegAt(msPerDegree)) return HitMargin.Perfect;
        if (abs <= NearPerfectDegAt(msPerDegree)) return early ? HitMargin.EarlyPerfect : HitMargin.LatePerfect;
        if (abs <= VeryDegAt(msPerDegree)) return early ? HitMargin.VeryEarly : HitMargin.VeryLate;
        return early ? HitMargin.TooEarly : HitMargin.TooLate;
    }
}
