using Godot;

namespace OpenOrbit.Godot;

/// <summary>타일 위에 잠깐 떠올랐다 사라지는 판정 텍스트.</summary>
public partial class JudgmentPopup : Node2D
{
    private const double Lifetime = 0.6;
    private const int FontSize = 26;

    private string _text = "";
    private Color _color;
    private double _age;

    public static JudgmentPopup Create(string text, Color color, Vector2 position) => new()
    {
        _text = text,
        _color = color,
        Position = position + new Vector2(0, -0.45f * Coords.UnitPx),
    };

    public override void _Process(double delta)
    {
        _age += delta;
        if (_age >= Lifetime)
        {
            QueueFree();
            return;
        }
        Position += new Vector2(0, (float)(-40 * delta));
        Modulate = new Color(1, 1, 1, (float)(1 - _age / Lifetime));
    }

    public override void _Draw() =>
        DrawString(ThemeDB.FallbackFont, new Vector2(-150, 0), _text, HorizontalAlignment.Center, 300, FontSize, _color);
}
