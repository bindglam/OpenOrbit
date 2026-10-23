using Godot;
using OpenOrbit.Godot;

namespace OpenOrbit.Godot.Examples.TestGame;

/// <summary>화면 고정 텍스트. 라벨을 화면 전체 크기로 깔고 정렬만 다르게 해서 배치한다.</summary>
public partial class Hud : CanvasLayer
{
    private Label _info = null!;
    private Label _stats = null!;
    private Label _center = null!;
    private Label _hint = null!;

    public HitErrorMeter Meter { get; private set; } = null!;

    public string Info { set => _info.Text = value; }
    public string Stats { set => _stats.Text = value; }
    public string Center { set => _center.Text = value; }
    public string Hint { set => _hint.Text = value; }

    public override void _Ready()
    {
        _info = MakeLabel(HorizontalAlignment.Left, VerticalAlignment.Top, 18);
        _stats = MakeLabel(HorizontalAlignment.Right, VerticalAlignment.Top, 18);
        _center = MakeLabel(HorizontalAlignment.Center, VerticalAlignment.Center, 40);
        _hint = MakeLabel(HorizontalAlignment.Center, VerticalAlignment.Bottom, 16);
        _hint.Modulate = new Color(1, 1, 1, 0.6f);
        AddChild(Meter = new HitErrorMeter());
    }

    private Label MakeLabel(HorizontalAlignment horizontal, VerticalAlignment vertical, int fontSize)
    {
        var label = new Label
        {
            HorizontalAlignment = horizontal,
            VerticalAlignment = vertical,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeConstantOverride("outline_size", 6);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        AddChild(label);
        label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        label.OffsetLeft = label.OffsetTop = 16;
        label.OffsetRight = label.OffsetBottom = -16;
        return label;
    }
}
