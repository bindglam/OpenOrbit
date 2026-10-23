using System.Text.Json.Nodes;

namespace OpenOrbit.Core.Charts.Levels.Events;

/// <summary>"actions" 배열의 한 항목. Floor 는 이벤트가 걸린 타일 번호.</summary>
public abstract record LevelEvent(int Floor);

public enum SpeedType
{
    Bpm,
    Multiplier,
}

public sealed record SetSpeedEvent(int Floor, SpeedType SpeedType, double BeatsPerMinute, double BpmMultiplier)
    : LevelEvent(Floor);

public sealed record TwirlEvent(int Floor) : LevelEvent(Floor);

/// <summary>Duration 은 박자 단위로 추가되는 대기 시간.</summary>
public sealed record PauseEvent(int Floor, double Duration) : LevelEvent(Floor);

/// <summary>Duration 은 추가로 도는 바퀴 수 (1바퀴 = 360° = 2박).</summary>
public sealed record HoldEvent(int Floor, int Duration) : LevelEvent(Floor);

/// <summary>
/// 행성 개수 변경 (3공, 4공). 행성들은 한 변이 타일 한 칸인 정다각형을 이루며,
/// 다음 행성이 그만큼 앞에서 출발하므로 타일마다 회전량이 내각만큼(3공 60°, 4공 90°) 줄어든다.
/// </summary>
public sealed record MultiPlanetEvent(int Floor, int Planets) : LevelEvent(Floor);

/// <summary>
/// 아직 해석하지 않는 이벤트 (장식, 카메라, MultiPlanet 등). 원본 JSON 을 그대로 보관한다.
/// </summary>
public sealed record UnknownEvent(int Floor, string EventType, JsonObject Raw) : LevelEvent(Floor);
