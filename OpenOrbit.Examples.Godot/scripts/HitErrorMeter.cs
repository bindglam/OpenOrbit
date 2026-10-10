using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Godot;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Examples.Godot;

/// <summary>
/// 얼불춤의 화면 아래 판정 미터. 위로 볼록한 호에 판정 범위를 색으로 칠하고(왼쪽 = 빠름, 오른쪽 = 느림),
/// 입력할 때마다 그 오차 위치에 눈금을 찍어 서서히 지운다. 호 안쪽의 화살표는 최근 입력의 평균 오차를 가리킨다.
/// <para>오차는 판정과 같은 각도(도) 기준이라 BPM 이 바뀌어도 눈금 위치가 판정 색과 맞는다.</para>
/// </summary>
public partial class HitErrorMeter : Node2D
{
    private const float Radius = 120;
    private const float Thickness = 10;
    private const float HalfSweep = 65 * Mathf.Pi / 180; // 호 중앙에서 끝까지의 각도
    private const float TipSize = 0.08f; // 양 끝 빨간 구간 (VeryDeg 대비)
    private const float BottomMargin = 48; // 화면 아래 안내 문구와 겹치지 않게

    private const double TickLifetime = 4;
    private const int MaxTicks = 64;
    private const double AverageWeight = 0.2; // 새 입력이 평균에 반영되는 비율

    private static readonly Color Shadow = new(0, 0, 0, 0.3f);
    private static readonly Color Marker = Colors.White;

    private readonly ConcurrentQueue<float> _pending = new(); // 게임 엔진 스레드에서 들어온 오차 (정규화된 값)
    private readonly List<(float Error, double Age)> _ticks = [];
    private JudgmentWindows _windows = JudgmentWindows.Normal;
    private float _average;
    private float _arrow; // 화면에 보이는 화살표 위치 (평균을 부드럽게 따라간다)
    private bool _hasAverage;

    public void Setup(JudgmentWindows windows)
    {
        _windows = windows;
        Clear();
    }

    public void Clear()
    {
        _pending.Clear();
        _ticks.Clear();
        _average = _arrow = 0;
        _hasAverage = false;
        QueueRedraw();
    }

    /// <summary>판정 하나를 추가한다. 아무 스레드에서나 호출할 수 있다.</summary>
    public void Add(HitMargin margin, double errorDeg)
    {
        if (margin == HitMargin.Auto)
            return;

        // VeryDeg 를 ±1 로. 범위 밖(너무 빠름 / 미스)은 양 끝 빨간 구간 가운데에 찍는다.
        var error = (float)(errorDeg / _windows.VeryDeg);
        if (margin is HitMargin.TooEarly or HitMargin.TooLate)
            error = Math.Sign(errorDeg) * (1 + TipSize / 2);
        _pending.Enqueue(Math.Clamp(error, -1 - TipSize, 1 + TipSize));
    }

    public override void _Process(double delta)
    {
        while (_pending.TryDequeue(out var error))
        {
            if (_ticks.Count == MaxTicks)
                _ticks.RemoveAt(0);
            _ticks.Add((error, 0));

            _average = _hasAverage ? Mathf.Lerp(_average, error, (float)AverageWeight) : error;
            _hasAverage = true;
        }

        for (var i = _ticks.Count - 1; i >= 0; i--)
        {
            var age = _ticks[i].Age + delta;
            if (age >= TickLifetime)
                _ticks.RemoveAt(i);
            else
                _ticks[i] = (_ticks[i].Error, age);
        }

        _arrow = Mathf.Lerp(_arrow, _average, (float)(1 - Math.Exp(-delta * 8)));

        Position = new Vector2(GetViewportRect().Size.X / 2, GetViewportRect().Size.Y - BottomMargin + Radius * Mathf.Cos(HalfSweep));
        QueueRedraw();
    }

    public override void _Draw()
    {
        var limit = 1 + TipSize;

        // 뒤에 깔리는 어두운 띠
        DrawArc(Vector2.Zero, Radius, AngleOf(-limit) - 0.06f, AngleOf(limit) + 0.06f, 64, Shadow, Thickness * 5, true);
        DrawArc(Vector2.Zero, Radius, AngleOf(-limit) - 0.03f, AngleOf(limit) + 0.03f, 64, Shadow, Thickness * 3, true);

        // 판정 구간 (가운데 → 양 끝)
        var perfect = (float)(_windows.PerfectDeg / _windows.VeryDeg);
        var near = (float)(_windows.NearPerfectDeg / _windows.VeryDeg);
        DrawBand(-perfect, perfect, Palette.ForMargin(HitMargin.Perfect));
        DrawBand(perfect, near, Palette.ForMargin(HitMargin.LatePerfect));
        DrawBand(near, 1, Palette.ForMargin(HitMargin.VeryLate));
        DrawBand(1, limit, Palette.ForMargin(HitMargin.TooLate));

        // 눈금: 띠를 가로지르는 짧은 선, 오래될수록 흐려진다
        foreach (var (error, age) in _ticks)
        {
            var direction = Vector2.FromAngle(AngleOf(error));
            var color = Marker with { A = (float)(1 - age / TickLifetime) };
            DrawLine(direction * (Radius - Thickness), direction * (Radius + Thickness), color, 3, true);
        }

        // 평균 화살표: 호 안쪽에서 바깥(호)을 향하는 꺾쇠
        if (_hasAverage)
        {
            var angle = AngleOf(_arrow);
            var outward = Vector2.FromAngle(angle);
            var side = outward.Orthogonal();
            var tip = outward * (Radius - Thickness * 1.8f);
            var back = tip - outward * 7;
            DrawPolyline([back + side * 9, tip, back - side * 9], Marker, 3.5f, true);
        }
    }

    /// <summary>느림 쪽 구간과 그 거울상(빠름 쪽)을 같은 색으로 칠한다.</summary>
    private void DrawBand(float from, float to, Color color)
    {
        DrawArc(Vector2.Zero, Radius, AngleOf(from), AngleOf(to), 24, color, Thickness, true);
        if (from >= 0)
            DrawArc(Vector2.Zero, Radius, AngleOf(-to), AngleOf(-from), 24, color, Thickness, true);
    }

    /// <summary>정규화된 오차(-1 = VeryDeg 만큼 빠름) → 호 위의 각도. 0 이면 바로 위.</summary>
    private static float AngleOf(float error) => -Mathf.Pi / 2 + error / (1 + TipSize) * HalfSweep;
}
