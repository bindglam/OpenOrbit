using OpenOrbit.Core.Charts.Track;

namespace OpenOrbit.Core.Charts.Levels;

/// <summary>.adofai 의 "settings" 블록 중 게임 로직과 트랙 표시에 필요한 값만.</summary>
public sealed record LevelSettings
{
    public double Bpm { get; init; } = 100;

    /// <summary>곡 시작부터 첫 입력(1번 타일 도착)까지의 시간(ms).</summary>
    public double OffsetMs { get; init; }

    /// <summary>곡 피치(%). 100 = 원속.</summary>
    public double Pitch { get; init; } = 100;

    public int CountdownTicks { get; init; } = 4;

    public string SongFilename { get; init; } = "";

    /// <summary>0번 타일부터 쓰는 트랙 색. ColorTrack 이 없으면 끝까지 이 색이다.</summary>
    public TrackColor TrackColor { get; init; } = TrackColor.Default;

    public TrackAppearAnimation TrackAnimation { get; init; } = TrackAppearAnimation.None;

    /// <summary>행성이 도착하기 몇 박 전에 타일이 나타나는지.</summary>
    public double BeatsAhead { get; init; } = 3;

    public TrackDisappearAnimation TrackDisappearAnimation { get; init; } = TrackDisappearAnimation.None;

    /// <summary>행성이 떠난 뒤 몇 박 뒤에 타일이 사라지는지.</summary>
    public double BeatsBehind { get; init; } = 4;

    /// <summary>켜져 있으면 MoveTrack 으로 움직인 타일 위에 행성도 같이 움직여서 그린다.</summary>
    public bool StickToFloors { get; init; }
}
