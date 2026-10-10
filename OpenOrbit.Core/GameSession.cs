using System.Runtime.InteropServices;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Geometry;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Core;

/// <summary>
/// 한 판의 진행 상태. 시간은 외부(오디오 재생 위치)에서 주입받고, 이 클래스는 순수 로직만 담당한다.
/// </summary>
public sealed class GameSession(Chart chart, JudgmentWindows windows)
{
    public Chart Chart { get; } = chart;
    public JudgmentWindows Windows { get; } = windows;

    /// <summary>중심 행성이 서 있는 타일.</summary>
    public int CurrentFloor { get; private set; } = 0;

    public bool IsFinished => CurrentFloor >= Chart.Tiles.Count - 1;
    
    private readonly List<HitResult> _results = [];
    public IReadOnlyList<HitResult> Results => _results;
    private readonly List<HitResult> _producedBuffer = [];

    public void Reset()
    {
        CurrentFloor = 0;
        _results.Clear();
        _producedBuffer.Clear();
    }

    /// <summary>키 입력 처리. 판정 대상이 아니면(너무 빠름/끝남) TooEarly 결과만 돌려주고 진행하지 않는다.</summary>
    public HitResult? Press(double songTimeMs)
    {
        if (IsFinished)
            return null;

        var target = Chart.Tiles[CurrentFloor + 1];
        var errorMs = songTimeMs - target.TimeMs;
        var errorDeg = errorMs / Chart.MsPerDegree(Chart.Tiles[CurrentFloor].Bpm);
        var margin = Windows.Classify(errorDeg);

        var result = new HitResult(target.Floor, margin, errorMs);
        if (margin == HitMargin.TooEarly)
            return result;

        // TooLate 는 Update 에서 처리되므로 여기선 판정 범위 안의 입력만 진행시킨다
        if (margin != HitMargin.TooLate)
            Advance(result);
        return result;
    }

    /// <summary>
    /// 매 프레임 호출. 미드스핀 자동 통과, 판정 범위를 지나친 타일의 TooLate(미스) 처리를 한다.
    /// </summary>
    public ReadOnlySpan<HitResult> Update(double songTimeMs)
    {
        _producedBuffer.Clear();
        
        while (!IsFinished)
        {
            var target = Chart.Tiles[CurrentFloor + 1];
            HitResult result;

            if (target.IsMidspin)
                result = new HitResult(target.Floor, HitMargin.Auto, 0);
            else
            {
                var errorMs = songTimeMs - target.TimeMs;
                var errorDeg = errorMs / Chart.MsPerDegree(Chart.Tiles[CurrentFloor].Bpm);
                if (errorDeg <= Windows.VeryDeg)
                    break;
                // TODO: 실제 게임은 미스 시 사망 / 연습 모드는 계속 진행. 지금은 계속 진행.
                result = new HitResult(target.Floor, HitMargin.TooLate, errorMs);
            }

            Advance(result);
            _producedBuffer.Add(result);
        }
        
        return CollectionsMarshal.AsSpan(_producedBuffer);
    }

    /// <summary>렌더링용: 현재 시각 기준 중심 행성 위치와 회전 행성의 각도.</summary>
    public PlanetState GetPlanetState(double songTimeMs)
    {
        var tile = Chart.Tiles[CurrentFloor];
        var rotated = (songTimeMs - tile.TimeMs) / Chart.MsPerDegree(tile.Bpm);
        var angle = Angles.Normalize(tile.EntryAngle + (tile.Clockwise ? -rotated : rotated));
        return new PlanetState(tile.Position, tile.Position + Vec2.FromAngle(angle), angle);
    }

    private void Advance(HitResult result)
    {
        _results.Add(result);
        CurrentFloor++;
    }
}

public readonly record struct PlanetState(Vec2 PivotPosition, Vec2 OrbitingPosition, double OrbitingAngle);
