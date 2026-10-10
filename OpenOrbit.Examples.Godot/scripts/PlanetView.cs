using Godot;
using OpenOrbit.Core;

namespace OpenOrbit.Examples.Godot;

public partial class PlanetView : Node2D
{
    private Sprite2D _fireSprite = null!;
    private Sprite2D _iceSprite = null!;
    
    private Vector2 _pivot;
    private Vector2 _orbit;
    private bool _fireIsPivot = true;

    public Vector2 Pivot => _pivot;

    public override void _Ready()
    {
        _fireSprite = GetNode<Sprite2D>("Fire");
        _iceSprite = GetNode<Sprite2D>("Ice");
    }

    public void Set(PlanetState state, int currentFloor)
    {
        _pivot = Coords.ToScreen(state.PivotPosition);
        _orbit = Coords.ToScreen(state.OrbitingPosition);
        _fireIsPivot = currentFloor % 2 == 0;
    }

    public override void _Process(double delta)
    {
        GlobalPosition = _pivot;
        if (_fireIsPivot)
        {
            _fireSprite.Position = new Vector2();
            _iceSprite.GlobalPosition = _orbit;
        }
        else
        {
            _fireSprite.GlobalPosition = _orbit;
            _iceSprite.Position = new Vector2();
        }
    }

    public override void _Draw()
    {
        DrawArc(_pivot, Coords.UnitPx, 0, Mathf.Tau, 64, Palette.Orbit, 2, true);
    }
}
