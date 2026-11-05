using Godot;
using OpenOrbit.Core;

namespace OpenOrbit.Godot;

/// <summary>
/// 행성들. 2공은 중심 행성과 회전하는 행성, 3공 이상(MultiPlanet)은 한 변이 타일 한 칸인 정다각형으로
/// 나머지 행성들이 회전하는 행성을 따라 돈다 (<see cref="PlanetState.PositionAt"/>).
/// <para>
/// 행성 이미지는 한 장(흰 공)을 쓰고 행성마다 <see cref="PlanetColors"/> 색을 입힌다.
/// </para>
/// </summary>
[GlobalClass]
public partial class PlanetView : Node2D
{
    private const int MaxPlanets = 8;

    /// <summary>행성 이미지. 비워 두면 기본 흰 공. 색을 곱해서 칠하므로 흰색 이미지여야 한다.</summary>
    [Export] public Texture2D? PlanetTexture { get; set; }
    [Export] public Vector2 PlanetScale { get; set; } = new(0.07f, 0.07f);

    /// <summary>
    /// 행성마다의 색. 0 = 불, 1 = 얼음, 2 = 세 번째 행성, 3 = 네 번째 행성 ...
    /// 모자라는 행성은 얼불춤처럼 0, 1 번 색 사이의 색조로 채운다.
    /// </summary>
    [Export] public Color[] PlanetColors { get; set; } = [Palette.Fire, Palette.Ice];

    private readonly Sprite2D[] _sprites = new Sprite2D[MaxPlanets];
    private PlanetState _state = new(default, default, 0);

    public Vector2 Pivot => Coords.ToScreen(_state.PivotPosition);
    public IReadOnlyList<Sprite2D> PlanetSprites => _sprites;

    public override void _Ready()
    {
        ArgumentNullException.ThrowIfNull(PlanetTexture);
        for (var i = 0; i < MaxPlanets; i++)
        {
            AddChild(_sprites[i] = new Sprite2D
            {
                Texture = PlanetTexture,
                Scale = PlanetScale,
                TopLevel = true, // 위치는 화면 좌표로 직접 정한다
            });
        }
    }

    public void Set(PlanetState state) => _state = state;

    /// <summary>
    /// i 번 행성의 색. <see cref="PlanetColors"/> 에 없으면 얼불춤 PlanetarySystem 처럼
    /// 0, 1 번 색의 색조 사이를 행성 개수에 맞춰 나눈다 (기본 빨강, 파랑이면 3공은 초록).
    /// </summary>
    public Color ColorOf(int planet, int planets)
    {
        if (planet < PlanetColors.Length)
            return PlanetColors[planet];

        var first = PlanetColors.Length > 0 ? PlanetColors[0] : Palette.Fire;
        var second = PlanetColors.Length > 1 ? PlanetColors[1] : Palette.Ice;

        // 두 색조 중 어느 쪽에서 출발할지: 색상환에서 먼 쪽 호를 따라 나눈다
        var gap = Mathf.Abs(second.H - first.H);
        var (from, to) = 1 - gap > gap
            ? (first.H > second.H ? (first, second) : (second, first))
            : (first.H < second.H ? (first, second) : (second, first));
        var toHue = to.H < from.H ? to.H + 1 : to.H;

        var t = (float)(planet - 1) / (Mathf.Max(planets, planet + 1) - 1);
        return Color.FromHsv(
            (from.H + (toHue - from.H) * t) % 1,
            Mathf.Lerp(from.S, to.S, t),
            Mathf.Lerp(from.V, to.V, t));
    }

    public override void _Process(double delta)
    {
        GlobalPosition = Pivot;

        var planets = Mathf.Clamp(_state.Planets, 2, MaxPlanets);
        for (var i = 0; i < MaxPlanets; i++)
            _sprites[i].Visible = i < planets;

        // k 번째 자리(0 = 중심, 1 = 회전, 2.. = 그다음)에 있는 행성을 그 자리에
        for (var k = 0; k < planets; k++)
        {
            var sprite = _sprites[_state.PlanetAt(k)];
            sprite.GlobalPosition = Coords.ToScreen(_state.PositionAt(k));
            sprite.Modulate = ColorOf(_state.PlanetAt(k), planets);
        }
    }

    public override void _Draw()
    {
        DrawArc(Vector2.Zero, Coords.UnitPx, 0, Mathf.Tau, 64, Palette.Orbit, 2, true);
    }
}
