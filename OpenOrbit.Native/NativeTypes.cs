using System.Runtime.InteropServices;
using OpenOrbit.Core;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Native;

// C ABI 로 주고받는 구조체들. 레이아웃이 바뀌면 Java 쪽(NativeLayouts)도 같이 바꿔야 한다.
// 패딩 차이가 없도록 double 을 앞에, int 를 뒤에 두고 크기를 8의 배수로 맞춘다.
// bool 은 플랫폼마다 크기가 달라질 수 있으므로 int(0/1)로 넘긴다.

/// <summary>88 바이트.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OoTile
{
    public double Angle;
    public double PositionX;
    public double PositionY;
    public double TimeMs;
    public double Bpm;
    public double EntryAngle;
    public double TravelAngle;
    public double TravelMs;
    public int Floor;
    public int IsMidspin;
    public int Clockwise;
    public int Planets;
    public int PivotPlanet;
    private int _padding;

    public static OoTile From(in Tile t) => new()
    {
        Angle = t.Angle,
        PositionX = t.Position.X,
        PositionY = t.Position.Y,
        TimeMs = t.TimeMs,
        Bpm = t.Bpm,
        EntryAngle = t.EntryAngle,
        TravelAngle = t.TravelAngle,
        TravelMs = t.TravelMs,
        Floor = t.Floor,
        IsMidspin = t.IsMidspin ? 1 : 0,
        Clockwise = t.Clockwise ? 1 : 0,
        Planets = t.Planets,
        PivotPlanet = t.PivotPlanet,
    };
}

/// <summary>32 바이트.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OoSettings
{
    public double Bpm;
    public double OffsetMs;
    public double Pitch;
    public int CountdownTicks;
    private int _padding;

    public static OoSettings From(LevelSettings s) => new()
    {
        Bpm = s.Bpm,
        OffsetMs = s.OffsetMs,
        Pitch = s.Pitch,
        CountdownTicks = s.CountdownTicks,
    };
}

/// <summary>56 바이트.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OoWindows
{
    public double PerfectDeg;
    public double NearPerfectDeg;
    public double VeryDeg;
    public double PerfectMs;
    public double NearPerfectMs;
    public double VeryMs;
    public double TooEarlyDeg;

    public static OoWindows From(JudgmentWindows w) => new()
    {
        PerfectDeg = w.PerfectDeg,
        NearPerfectDeg = w.NearPerfectDeg,
        VeryDeg = w.VeryDeg,
        PerfectMs = w.PerfectMs,
        NearPerfectMs = w.NearPerfectMs,
        VeryMs = w.VeryMs,
        TooEarlyDeg = w.TooEarlyDeg,
    };

    public readonly JudgmentWindows ToWindows() => new()
    {
        PerfectDeg = PerfectDeg,
        NearPerfectDeg = NearPerfectDeg,
        VeryDeg = VeryDeg,
        PerfectMs = PerfectMs,
        NearPerfectMs = NearPerfectMs,
        VeryMs = VeryMs,
        TooEarlyDeg = TooEarlyDeg,
    };
}

/// <summary>16 바이트.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OoHitResult
{
    public double ErrorMs;
    public int Floor;
    /// <summary><see cref="HitMargin"/> 값.</summary>
    public int Margin;

    public static OoHitResult From(in HitResult r) => new()
    {
        ErrorMs = r.ErrorMs,
        Floor = r.Floor,
        Margin = (int)r.Margin,
    };
}

/// <summary>56 바이트.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct OoPlanetState
{
    public double PivotX;
    public double PivotY;
    public double OrbitingX;
    public double OrbitingY;
    public double OrbitingAngle;
    public int Planets;
    public int PivotPlanet;
    public int Clockwise;
    private int _padding;

    public static OoPlanetState From(in PlanetState s) => new()
    {
        PivotX = s.PivotPosition.X,
        PivotY = s.PivotPosition.Y,
        OrbitingX = s.OrbitingPosition.X,
        OrbitingY = s.OrbitingPosition.Y,
        OrbitingAngle = s.OrbitingAngle,
        Planets = s.Planets,
        PivotPlanet = s.PivotPlanet,
        Clockwise = s.Clockwise ? 1 : 0,
    };

    public readonly PlanetState ToState() => new(
        new(PivotX, PivotY), new(OrbitingX, OrbitingY), OrbitingAngle, Planets, PivotPlanet, Clockwise != 0);
}

/// <summary>반환 코드. 0 이상은 성공(함수에 따라 개수 등), 음수는 실패. 자세한 내용은 oo_last_error.</summary>
public static class OoStatus
{
    public const int Ok = 0;
    public const int InvalidHandle = -1;
    public const int InvalidArgument = -2;
    public const int ParseError = -3;
    public const int IoError = -4;
    public const int InternalError = -5;
}
