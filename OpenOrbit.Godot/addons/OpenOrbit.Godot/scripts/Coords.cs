using Godot;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Godot;

/// <summary>Core 좌표(타일 1칸 = 1, +Y 위쪽) ↔ Godot 화면 좌표(픽셀, +Y 아래쪽).</summary>
public static class Coords
{
    public const float UnitPx = 100f;

    public static Vector2 ToScreen(Vec2 v) => new Vector2((float)v.X, -(float)v.Y) * UnitPx;
}
