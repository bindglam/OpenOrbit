using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Track;

namespace OpenOrbit.Core.Charts;

/// <summary>레벨을 타이밍/위치까지 계산해 둔 결과물. 게임 플레이는 이걸 기준으로 돈다.</summary>
public sealed class Chart(Level level, Tile[] tiles, TrackTimeline track)
{
    public Level Level { get; } = level;

    public IReadOnlyList<Tile> Tiles { get; } = tiles;

    /// <summary>타일 색, 움직임, 등장/퇴장 애니메이션. 렌더러용.</summary>
    public TrackTimeline Track { get; } = track;

    /// <summary>피치 반영 후 1도 회전에 걸리는 ms.</summary>
    public double MsPerDegree(double bpm) => 60000.0 / (bpm * Level.Settings.Pitch / 100.0) / 180.0;
}
