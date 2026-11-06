using OpenOrbit.Core.Charts.Track;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts.Levels.Events;

public enum TileAnchor
{
    ThisTile,
    Start,
    End,
}

/// <summary>레벨 파일의 [offset, "ThisTile" | "Start" | "End"] 타일 지정.</summary>
public readonly record struct TileReference(int Offset, TileAnchor Anchor)
{
    /// <summary>실제 타일 번호. 범위를 벗어날 수 있으므로 호출자가 자른다.</summary>
    public int Resolve(int eventFloor, int lastFloor) => Anchor switch
    {
        TileAnchor.Start => Offset,
        TileAnchor.End => lastFloor + Offset,
        _ => eventFloor + Offset,
    };
}

/// <summary>
/// 이 타일부터(JustThisTile 이면 이 타일만) 트랙 색을 바꾼다. 시간과 상관없이 타일에 붙는 설정이다.
/// 이벤트에 없는 값(null)은 직전 설정을 그대로 쓴다.
/// </summary>
public sealed record ColorTrackEvent(int Floor, TrackColorPatch Color, bool JustThisTile) : LevelEvent(Floor);

/// <summary>
/// 이 타일에 도착한 뒤 AngleOffset(도)만큼 지나서 범위 안 타일의 색을 바꾼다.
/// 이벤트에 없는 값(null)은 그 타일의 지금 값을 그대로 쓴다.
/// 단색(Single, Stripes)은 DurationBeats 동안 이전 색에서 새 색으로 섞이고, 저절로 바뀌는 색은 바로 바뀐다.
/// </summary>
/// <param name="GapLength">범위 안에서 건너뛸 타일 수 (0 = 모든 타일).</param>
public sealed record RecolorTrackEvent(
    int Floor,
    TileReference StartTile,
    TileReference EndTile,
    int GapLength,
    TrackColorPatch Color,
    double DurationBeats,
    Ease Ease,
    double AngleOffset) : LevelEvent(Floor);

/// <summary>
/// 이 타일에 도착한 뒤 AngleOffset(도)만큼 지나서 범위 안 타일을 움직인다.
/// 값은 원래 상태 기준의 목표값이고, null 이면 그 속성은 건드리지 않는다.
/// </summary>
/// <param name="PositionX">타일 단위 오프셋.</param>
/// <param name="Rotation">반시계 방향(도).</param>
/// <param name="ScaleX">1 = 100%.</param>
/// <param name="Opacity">0~1.</param>
public sealed record MoveTrackEvent(
    int Floor,
    TileReference StartTile,
    TileReference EndTile,
    int GapLength,
    double DurationBeats,
    double? PositionX,
    double? PositionY,
    double? Rotation,
    double? ScaleX,
    double? ScaleY,
    double? Opacity,
    Ease Ease,
    double AngleOffset) : LevelEvent(Floor);

/// <summary>이 타일부터 등장/퇴장 애니메이션을 바꾼다. null 이면 그 값은 이전 설정을 유지한다.</summary>
public sealed record AnimateTrackEvent(
    int Floor,
    TrackAppearAnimation? Appear,
    double? BeatsAhead,
    TrackDisappearAnimation? Disappear,
    double? BeatsBehind) : LevelEvent(Floor);

/// <summary>
/// 타일 위치와 모양을 정한다. JustThisTile 이 아니면 뒤 타일들에도 계속 적용된다 (위치는 같이 밀린다). 행성 경로도 따라 바뀐다.
/// null 은 이벤트에 없던 값으로, 바꾸지 않는다.
/// </summary>
/// <param name="Offset">RelativeTo 타일 위치 기준 오프셋(타일 단위). null 이면 위치는 그대로 (RelativeTo 도 무시).</param>
/// <param name="Scale">1 = 100%.</param>
/// <param name="Opacity">0~1.</param>
/// <param name="EditorOnly">에디터에서만 적용되고 플레이 중에는 무시한다.</param>
/// <summary>이 타일부터 타일 메시의 길이와 폭을 기본의 몇 배로 그린다. 타일 간격은 그대로다.</summary>
/// <param name="Length">1 = 100%.</param>
/// <param name="Width">1 = 100%.</param>
public sealed record TileDimensionsEvent(int Floor, double Length, double Width) : LevelEvent(Floor);

public sealed record PositionTrackEvent(
    int Floor,
    Vec2? Offset,
    TileReference RelativeTo,
    double? Rotation,
    double? Scale,
    double? Opacity,
    bool JustThisTile,
    bool EditorOnly) : LevelEvent(Floor);
