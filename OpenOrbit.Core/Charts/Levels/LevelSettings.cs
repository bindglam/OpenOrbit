using OpenOrbit.Core.Charts.Levels.Events;
using OpenOrbit.Core.Charts.Track;
using OpenOrbit.Core.Geometry;

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

    /// <summary>카메라 초기 기준 (Tile 이면 0번 타일).</summary>
    public CameraRelativeTo CameraRelativeTo { get; init; } = CameraRelativeTo.Player;

    /// <summary>카메라 초기 오프셋(타일 단위).</summary>
    public Vec2 CameraPosition { get; init; } = Vec2.Zero;

    /// <summary>카메라 초기 회전(도, 반시계 방향).</summary>
    public double CameraRotation { get; init; }

    /// <summary>카메라 초기 줌. 1 = 100%, 클수록 넓게 보인다.</summary>
    public double CameraZoom { get; init; } = 1;

    /// <summary>옛 레벨 호환: MoveCamera 에 relativeTo 가 없으면 위치를 지금 카메라 위치에 더한다.</summary>
    public bool LegacyCameraRelativeTo { get; init; }

    /// <summary>타일 크기 (settings.tileShape). 타일 간격이 얼불춤 유닛으로 얼마인지 정한다.</summary>
    public TileShape TileShape { get; init; } = TileShape.Long;

    /// <summary>모든 트랙 스타일의 그림자 색을 덮어쓴다 (settings.trackShadowColor). null 이면 스타일 기본값.</summary>
    public Rgba? TrackShadowColor { get; init; }
}

/// <summary>타일 크기 (얼불춤 TileShape). 타일 한 칸이 Long 은 1.5, Short 는 1.0 유닛이다.</summary>
public enum TileShape
{
    Short,
    Long,
}
