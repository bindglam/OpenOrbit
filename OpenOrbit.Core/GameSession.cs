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
        var msPerDegree = Chart.MsPerDegree(Chart.Tiles[CurrentFloor].Bpm);
        var margin = Windows.Classify(errorMs / msPerDegree, msPerDegree);

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
                var msPerDegree = Chart.MsPerDegree(Chart.Tiles[CurrentFloor].Bpm);
                if (errorMs / msPerDegree <= Windows.VeryDegAt(msPerDegree))
                    break;
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
        return new PlanetState(tile.Position, tile.Position + Vec2.FromAngle(angle), angle,
            tile.Planets, tile.PivotPlanet, tile.Clockwise);
    }

    private void Advance(HitResult result)
    {
        _results.Add(result);
        CurrentFloor++;
    }
}

/// <param name="Planets">행성 개수 (MultiPlanet).</param>
/// <param name="PivotPlanet">중심 행성 번호 (0 = 불, 1 = 얼음, 2.. = 3공, 4공 ...).</param>
public readonly record struct PlanetState(
    Vec2 PivotPosition,
    Vec2 OrbitingPosition,
    double OrbitingAngle,
    int Planets = 2,
    int PivotPlanet = 0,
    bool Clockwise = true)
{
    /// <summary>
    /// 순서 k 번째 행성의 위치. 0 = 중심, 1 = 회전 중, 2.. = 그다음에 회전할 행성.
    /// 행성들은 한 변이 타일 한 칸인 정다각형을 이룬다 (얼불춤 scrPlanet 과 같은 배치).
    /// </summary>
    public Vec2 PositionAt(int k)
    {
        if (k == 0) return PivotPosition;
        if (k == 1) return OrbitingPosition;

        // 다각형 중심은 회전 방향 반대쪽으로 내각의 절반만큼 돌아간 쪽에 있다
        var sign = Clockwise ? -1 : 1;
        var radius = 1 / (2 * Math.Sin(Math.PI / Planets));
        var centerAngle = OrbitingAngle - sign * Charts.ChartCompiler.InteriorAngle(Planets) / 2;
        var center = PivotPosition + Vec2.FromAngle(centerAngle) * radius;
        return center + Vec2.FromAngle(centerAngle + sign * 360.0 * (-k - Planets / 2.0) / Planets) * radius;
    }

    /// <summary>순서 k 번째 자리에 있는 행성 번호.</summary>
    public int PlanetAt(int k) => (PivotPlanet + k) % Planets;
}
