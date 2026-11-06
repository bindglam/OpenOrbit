namespace OpenOrbit.Core.Charts.Track;

/// <summary>얼불춤(DOTween) 이징 이름. 레벨 파일의 "ease" 값과 같다.</summary>
public enum Ease
{
    Linear,
    InSine, OutSine, InOutSine,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InQuart, OutQuart, InOutQuart,
    InQuint, OutQuint, InOutQuint,
    InExpo, OutExpo, InOutExpo,
    InCirc, OutCirc, InOutCirc,
    InElastic, OutElastic, InOutElastic,
    InBack, OutBack, InOutBack,
    InBounce, OutBounce, InOutBounce,
    Flash, InFlash, OutFlash, InOutFlash,
}

/// <summary>DOTween 기본값(오버슛 1.70158)을 쓰는 Penner 이징 공식.</summary>
public static class Easing
{
    private const double Overshoot = 1.70158;

    /// <summary>t 는 0~1. Back/Elastic 은 결과가 0~1 을 벗어날 수 있다.</summary>
    public static double Evaluate(Ease ease, double t)
    {
        t = Math.Clamp(t, 0, 1);
        switch (ease)
        {
            case Ease.InSine: return 1 - Math.Cos(t * Math.PI / 2);
            case Ease.OutSine: return Math.Sin(t * Math.PI / 2);
            case Ease.InOutSine: return -(Math.Cos(Math.PI * t) - 1) / 2;
            case Ease.InQuad: return t * t;
            case Ease.OutQuad: return 1 - (1 - t) * (1 - t);
            case Ease.InOutQuad: return InOut(t, 2);
            case Ease.InCubic: return t * t * t;
            case Ease.OutCubic: return 1 - Math.Pow(1 - t, 3);
            case Ease.InOutCubic: return InOut(t, 3);
            case Ease.InQuart: return Math.Pow(t, 4);
            case Ease.OutQuart: return 1 - Math.Pow(1 - t, 4);
            case Ease.InOutQuart: return InOut(t, 4);
            case Ease.InQuint: return Math.Pow(t, 5);
            case Ease.OutQuint: return 1 - Math.Pow(1 - t, 5);
            case Ease.InOutQuint: return InOut(t, 5);
            case Ease.InExpo: return t == 0 ? 0 : Math.Pow(2, 10 * t - 10);
            case Ease.OutExpo: return t == 1 ? 1 : 1 - Math.Pow(2, -10 * t);
            case Ease.InOutExpo:
                if (t is 0 or 1) return t;
                return t < 0.5 ? Math.Pow(2, 20 * t - 10) / 2 : (2 - Math.Pow(2, -20 * t + 10)) / 2;
            case Ease.InCirc: return 1 - Math.Sqrt(1 - t * t);
            case Ease.OutCirc: return Math.Sqrt(1 - (t - 1) * (t - 1));
            case Ease.InOutCirc:
                return t < 0.5
                    ? (1 - Math.Sqrt(1 - 4 * t * t)) / 2
                    : (Math.Sqrt(1 - Math.Pow(-2 * t + 2, 2)) + 1) / 2;
            case Ease.InElastic:
                if (t is 0 or 1) return t;
                return -Math.Pow(2, 10 * t - 10) * Math.Sin((t * 10 - 10.75) * (2 * Math.PI / 3));
            case Ease.OutElastic:
                if (t is 0 or 1) return t;
                return Math.Pow(2, -10 * t) * Math.Sin((t * 10 - 0.75) * (2 * Math.PI / 3)) + 1;
            case Ease.InOutElastic:
                if (t is 0 or 1) return t;
                const double c5 = 2 * Math.PI / 4.5;
                return t < 0.5
                    ? -(Math.Pow(2, 20 * t - 10) * Math.Sin((20 * t - 11.125) * c5)) / 2
                    : Math.Pow(2, -20 * t + 10) * Math.Sin((20 * t - 11.125) * c5) / 2 + 1;
            case Ease.InBack: return (Overshoot + 1) * t * t * t - Overshoot * t * t;
            case Ease.OutBack: return 1 + (Overshoot + 1) * Math.Pow(t - 1, 3) + Overshoot * Math.Pow(t - 1, 2);
            case Ease.InOutBack:
                const double c2 = Overshoot * 1.525;
                return t < 0.5
                    ? Math.Pow(2 * t, 2) * ((c2 + 1) * 2 * t - c2) / 2
                    : (Math.Pow(2 * t - 2, 2) * ((c2 + 1) * (t * 2 - 2) + c2) + 2) / 2;
            case Ease.InBounce: return 1 - OutBounce(1 - t);
            case Ease.OutBounce: return OutBounce(t);
            case Ease.InOutBounce: return t < 0.5 ? (1 - OutBounce(1 - 2 * t)) / 2 : (1 + OutBounce(2 * t - 1)) / 2;
            // Flash 계열은 DOTween 고유 이징이라 아직 선형으로 둔다
            default: return t;
        }
    }

    private static double InOut(double t, double power) =>
        t < 0.5 ? Math.Pow(2, power - 1) * Math.Pow(t, power) : 1 - Math.Pow(-2 * t + 2, power) / 2;

    private static double OutBounce(double t)
    {
        const double n1 = 7.5625;
        const double d1 = 2.75;
        if (t < 1 / d1) return n1 * t * t;
        if (t < 2 / d1) return n1 * (t -= 1.5 / d1) * t + 0.75;
        if (t < 2.5 / d1) return n1 * (t -= 2.25 / d1) * t + 0.9375;
        return n1 * (t -= 2.625 / d1) * t + 0.984375;
    }
}
