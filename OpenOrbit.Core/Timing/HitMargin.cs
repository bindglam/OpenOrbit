namespace OpenOrbit.Core.Timing;

public enum HitMargin
{
    TooEarly,
    VeryEarly,
    EarlyPerfect,
    Perfect,
    LatePerfect,
    VeryLate,
    TooLate,

    /// <summary>미드스핀처럼 입력 없이 자동으로 지나간 타일.</summary>
    Auto,
}

/// <param name="ErrorMs">음수 = 빠름, 양수 = 느림.</param>
public readonly record struct HitResult(int Floor, HitMargin Margin, double ErrorMs);
