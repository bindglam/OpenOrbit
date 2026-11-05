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
    /// floor 번 타일의 timeSec(곡 기준 초) 시점 색.
    /// <para>
    /// 얼불춤 소스를 보고 맞춘 것이 아니라 동작을 보고 근사한 공식이다 (특히 Blink, Rainbow 의 채도).
    /// </para>
    /// </summary>
    public Rgba Evaluate(int floor, double timeSec)
    {
        var period = Math.Max(AnimDurationSec, 1e-3);
        // Forward 는 앞 타일이 먼저 바뀌고 뒤 타일이 따라오도록 타일마다 위상을 늦춘다
        var shift = Pulse == TrackColorPulse.None ? 0 : floor * period / Math.Max(PulseLength, 1);
        var phase = (Pulse == TrackColorPulse.Backward ? timeSec + shift : timeSec - shift) / period;
        var frac = phase - Math.Floor(phase);

        return Type switch
        {
            TrackColorType.Stripes => floor % 2 == 0 ? Primary : Secondary,
            TrackColorType.Glow => Rgba.Lerp(Primary, Secondary, 0.5 - 0.5 * Math.Cos(2 * Math.PI * phase)),
            // 보조 색으로 번쩍였다가 주 색으로 돌아온다
            TrackColorType.Blink => Rgba.Lerp(Primary, Secondary, 1 - frac),
            TrackColorType.Switch => (long)Math.Floor(phase) % 2 == 0 ? Primary : Secondary,
            TrackColorType.Rainbow => Rgba.FromHsv(frac, 0.7, 1, Primary.A),
            _ => Primary,
        };
    }
}
