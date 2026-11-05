using OpenOrbit.Core.Charts.Track;

namespace OpenOrbit.Core.Charts.Levels.Events;

/// <summary>카메라 위치의 기준 (settings.relativeTo, MoveCamera.relativeTo).</summary>
public enum CameraRelativeTo
{
    /// <summary>플레이어(중심 행성)를 따라간다. 위치는 플레이어 기준 오프셋.</summary>
    Player,
    /// <summary>이벤트가 걸린 타일에 고정. 위치는 그 타일 기준 오프셋.</summary>
    Tile,
    /// <summary>월드 좌표에 고정 (0번 타일 = 원점).</summary>
    Global,
    /// <summary>기준은 그대로 두고 지금 위치에 오프셋을, 지금 회전에 회전값을 더한다.</summary>
    LastPosition,
    /// <summary>위치는 <see cref="LastPosition"/> 과 같고, 회전은 더하지 않고 그 값으로 정한다 (없으면 0).</summary>
    LastPositionNoRotation,
}

/// <summary>
/// 이 타일에 도착한 뒤 AngleOffset(도)만큼 지나서 카메라를 DurationBeats 동안 움직인다.
/// null 인 값은 이벤트에 없던 값으로, 바꾸지 않는다. 다만 RelativeTo 로 기준을 정하면 빈 위치 축은 0 이 된다.
/// </summary>
/// <param name="PositionX">타일 단위. 기준은 <see cref="RelativeTo"/>.</param>
/// <param name="Rotation">카메라 회전(도, 반시계 방향).</param>
/// <param name="Zoom">1 = 100%. 클수록 넓게 보인다 (얼불춤과 같음).</param>
public sealed record MoveCameraEvent(
    int Floor,
    double DurationBeats,
    CameraRelativeTo? RelativeTo,
    double? PositionX,
    double? PositionY,
    double? Rotation,
    double? Zoom,
    Ease Ease,
    double AngleOffset) : LevelEvent(Floor);
