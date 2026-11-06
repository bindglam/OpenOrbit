namespace OpenOrbit.Core.Charts.Track;

public enum TrackColorType
{
    Single,
    Stripes,
    Glow,
    Blink,
    Switch,
    Rainbow,
    /// <summary>곡 음량에 따라 바뀐다. Core 는 오디오를 모르므로 Primary 로 둔다.</summary>
    Volume,
}

/// <summary>색 애니메이션이 타일을 따라 퍼져 나가는 방향.</summary>
public enum TrackColorPulse
{
    None,
    Forward,
    Backward,
}

/// <summary>타일 모양/질감. 색 계산에는 쓰이지 않고 렌더러가 고른다.</summary>
public enum TrackStyle
{
    Standard,
    Neon,
    NeonLight,
    Basic,
    Gems,
    Minimal,
}

/// <summary>
/// 트랙 색 설정 (settings, ColorTrack, RecolorTrack 이 공유하는 필드).
/// </summary>
/// <param name="AnimDurationSec">Glow/Blink/Switch/Rainbow 한 주기(초).</param>
/// <param name="PulseLength">Pulse 가 한 주기만큼 어긋나는 데 걸리는 타일 수.</param>
/// <param name="GlowIntensity">Neon 계열 스타일의 발광 세기(%). 렌더러용.</param>
public sealed record TrackColor(
    TrackColorType Type,
    Rgba Primary,
    Rgba Secondary,
    double AnimDurationSec,
    TrackColorPulse Pulse,
    int PulseLength,
    TrackStyle Style,
    double GlowIntensity)
{
    public static readonly TrackColor Default = new(
        TrackColorType.Single, Rgba.Parse("debb7b", Rgba.White), Rgba.White, 2, TrackColorPulse.None, 10, TrackStyle.Standard, 100);

    /// <summary>
    /// floor 번 타일의 색. timeSec 는 곡 기준 초(피치 반영 전)라서 피치가 바뀌어도 주기가 박자에 맞는다.
    /// colorStart 는 Stripes 의 기준 타일(색 종류를 바꾼 ColorTrack/RecolorTrack 의 타일).
    /// <para>
    /// 얼불춤과 같은 색이 나온다. 얼불춤의 색 주기는 곡과 무관하게 흐르므로 절대 위상은 맞출 필요가 없고, 여기서는 곡 시간을 쓴다.
    /// </para>
    /// </summary>
    public Rgba Evaluate(int floor, int colorStart, double timeSec)
    {
        var period = Math.Max(AnimDurationSec, 1e-3);
        var length = Math.Max(PulseLength, 1);
        // Forward: (1 - (floor % L) / L) * T 만큼 앞당김, Backward: (floor % L) / L * T 만큼 앞당김
        var offset = Pulse switch
        {
            TrackColorPulse.Forward => (1 - (double)(floor % length) / length) * period,
            TrackColorPulse.Backward => (double)(floor % length) / length * period,
            _ => 0,
        };
        var phase = (timeSec + offset) / period;
        var frac = phase - Math.Floor(phase);

        return Type switch
        {
            // C# 의 % 처럼 음수 차이도 홀짝만 본다
            TrackColorType.Stripes => (floor - colorStart) % 2 == 0 ? Primary : Secondary,
            TrackColorType.Glow => Rgba.Lerp(Primary, Secondary, (1 - Math.Cos(2 * Math.PI * frac)) / 2),
            TrackColorType.Blink => Rgba.Lerp(Primary, Secondary, frac),
            TrackColorType.Switch => frac < 0.5 ? Primary : Secondary,
            // 색상만 돌리고 채도와 명도는 주 색의 것을 쓴다
            TrackColorType.Rainbow => Rainbow(frac),
            _ => Primary,
        };
    }

    /// <summary>시간에 따라 저절로 바뀌는 색 (Glow, Blink, Switch, Rainbow, Volume).</summary>
    public bool IsAnimated => Type is not (TrackColorType.Single or TrackColorType.Stripes);

    private Rgba Rainbow(double hue)
    {
        var (_, s, v) = Primary.ToHsv();
        return Rgba.FromHsv(hue, s, v, Primary.A);
    }
}

/// <summary>
/// ColorTrack/RecolorTrack 이 바꾸는 값. null 은 이벤트에 없던 값으로, 바꾸지 않는다 (얼불춤의 disabled 필드).
/// </summary>
public sealed record TrackColorPatch(
    TrackColorType? Type,
    Rgba? Primary,
    Rgba? Secondary,
    double? AnimDurationSec,
    TrackColorPulse? Pulse,
    int? PulseLength,
    TrackStyle? Style,
    double? GlowIntensity)
{
    public TrackColor ApplyTo(TrackColor c) => new(
        Type ?? c.Type,
        Primary ?? c.Primary,
        Secondary ?? c.Secondary,
        AnimDurationSec ?? c.AnimDurationSec,
        Pulse ?? c.Pulse,
        PulseLength ?? c.PulseLength,
        Style ?? c.Style,
        GlowIntensity ?? c.GlowIntensity);
}
