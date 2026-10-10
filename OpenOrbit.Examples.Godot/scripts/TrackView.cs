using System.Collections.Generic;
using Godot;
using OpenOrbit.Core.Charts;

namespace OpenOrbit.Examples.Godot;

/// <summary>타일 경로 렌더링. 현재 타일이 바뀔 때만 다시 그린다.</summary>
public partial class TrackView : Node2D
{
    private const float Width = 0.34f * Coords.UnitPx;
    private const float HalfLength = 0.46f; // 0.5 보다 짧게 그려 타일 사이에 틈을 둔다

    private Chart? _chart;
    private readonly HashSet<int> _twirls = [];
    private readonly Dictionary<int, bool> _speedChanges = []; // floor → 빨라짐 여부
    private int _currentFloor;

    public int CurrentFloor
    {
        get => _currentFloor;
        set
        {
            if (_currentFloor == value) return;
            _currentFloor = value;
            //CallDeferred("queue_redraw");
        }
    }

    public void Setup(Chart chart)
    {
        _chart = chart;
        _twirls.Clear();
        _speedChanges.Clear();

        var tiles = chart.Tiles;
        var prevBpm = chart.Level.Settings.Bpm;
        for (var i = 0; i < tiles.Count; i++)
        {
            if (i > 0 && tiles[i].Clockwise != tiles[i - 1].Clockwise)
                _twirls.Add(i);
            if (tiles[i].Bpm != prevBpm)
                _speedChanges[i] = tiles[i].Bpm > prevBpm;
            prevBpm = tiles[i].Bpm;
        }

        _currentFloor = 0;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_chart is null)
            return;

        var tiles = _chart.Tiles;
        for (var i = 0; i < tiles.Count; i++)
        {
            var color = i < _currentFloor ? Palette.TrackPassed : Palette.Track;
            var p = Coords.ToScreen(tiles[i].Position);

            if (i > 0)
                DrawHalf(p, Coords.ToScreen(tiles[i - 1].Position), color);
            if (i + 1 < tiles.Count)
                DrawHalf(p, Coords.ToScreen(tiles[i + 1].Position), color);
            DrawCircle(p, Width / 2, color, antialiased: true);
        }

        // 아이콘은 경로 위에 겹쳐 그린다
        for (var i = 0; i < tiles.Count; i++)
        {
            var p = Coords.ToScreen(tiles[i].Position);
            var dim = i < _currentFloor ? 0.3f : 1f;

            if (tiles[i].IsMidspin)
                DrawCircle(p, Width * 0.3f, Palette.Midspin);
            if (_twirls.Contains(i))
                DrawArc(p, Width * 0.3f, 0.3f, Mathf.Tau - 0.3f, 24, Palette.Twirl * new Color(1, 1, 1, dim), 4, true);
            if (_speedChanges.TryGetValue(i, out var faster))
                DrawCircle(p, Width * 0.18f, (faster ? Palette.SpeedUp : Palette.SpeedDown) * new Color(1, 1, 1, dim), antialiased: true);
        }

        if (_currentFloor + 1 < tiles.Count)
            DrawArc(Coords.ToScreen(tiles[_currentFloor + 1].Position), Width * 0.62f, 0, Mathf.Tau, 32, Palette.NextTile, 3, true);
    }

    private void DrawHalf(Vector2 from, Vector2 neighbor, Color color)
    {
        if (from.IsEqualApprox(neighbor)) // 미드스핀은 같은 자리
            return;
        DrawLine(from, from.Lerp(neighbor, HalfLength), color, Width, true);
    }
}
