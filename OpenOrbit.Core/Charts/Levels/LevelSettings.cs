namespace OpenOrbit.Core.Charts.Levels;

/// <summary>.adofai 의 "settings" 블록 중 게임 로직에 필요한 값만.</summary>
public sealed record LevelSettings
{
    public double Bpm { get; init; } = 100;

    /// <summary>곡 시작부터 첫 입력(1번 타일 도착)까지의 시간(ms).</summary>
    public double OffsetMs { get; init; }

    /// <summary>곡 피치(%). 100 = 원속.</summary>
    public double Pitch { get; init; } = 100;

    public int CountdownTicks { get; init; } = 4;

    public string SongFilename { get; init; } = "";
}
