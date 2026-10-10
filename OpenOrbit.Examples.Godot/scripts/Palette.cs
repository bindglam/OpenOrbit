using Godot;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Examples.Godot;

public static class Palette
{
    public static readonly Color Background = new("0d1017");
    public static readonly Color Track = new("debb7b");
    public static readonly Color TrackPassed = Track.Lerp(Background, 0.78f); // 반투명이면 겹치는 부분이 진해지므로 불투명하게
    public static readonly Color NextTile = new("ffffff");
    public static readonly Color Orbit = new(1, 1, 1, 0.1f);

    public static readonly Color Fire = new("ff5a36");
    public static readonly Color Ice = new("3fa9ff");

    public static readonly Color Twirl = new("b04bff");
    public static readonly Color SpeedUp = new("ff4b4b");
    public static readonly Color SpeedDown = new("4b8bff");

    public static Color ForMargin(HitMargin margin) => margin switch
    {
        HitMargin.Perfect => new Color("60ff4e"),
        HitMargin.EarlyPerfect or HitMargin.LatePerfect => new Color("e8ff5a"),
        HitMargin.VeryEarly or HitMargin.VeryLate => new Color("ff9a3c"),
        _ => new Color("ff3c3c"),
    };

    public static string Text(HitMargin margin) => margin switch
    {
        HitMargin.Perfect => "Perfect!",
        HitMargin.EarlyPerfect => "EPerfect!",
        HitMargin.LatePerfect => "LPerfect!",
        HitMargin.VeryEarly => "Early!",
        HitMargin.VeryLate => "Late!",
        HitMargin.TooEarly => "Too Early!",
        HitMargin.TooLate => "Miss",
        _ => "",
    };
}
