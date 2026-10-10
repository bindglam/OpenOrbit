using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using OpenOrbit.Core.Charts;

namespace OpenOrbit.Examples.Godot;

/// <summary>
/// 타일 경로 렌더링.
/// <para>
/// 타일마다 RenderingServer 캔버스 아이템을 하나씩 만들고, 그리기 명령은 채보를 열 때 한 번만 기록한다.
/// 이후 타일을 밟아도 상태가 바뀐 타일만 다시 기록하므로 매 프레임/매 판정마다 전체를 다시 그리지 않는다.
/// 화면 밖 타일은 Godot 가 아이템 단위로 컬링한다.
/// </para>
/// <para>
/// 겹쳐 있는 타일은 순서가 앞선 타일이 위에 보이도록 draw index 를 역순으로 준다.
/// </para>
/// </summary>
public partial class TrackView : Node2D
{
    private const string ArmTexturePath = "res://assets/sprite/tile/arm.png";
    private const string JointTexturePath = "res://assets/sprite/tile/joint.png";

    private const float Width = 0.34f * Coords.UnitPx;
    private const float ArmLength = 0.46f * Coords.UnitPx; // 0.5 보다 짧게 그려 타일 사이에 틈을 둔다
    private const float IconDim = 0.3f;

    private static readonly Rect2 ArmRect = new(0, -Width / 2, ArmLength, Width);
    private static readonly Rect2 JointRect = new(-Width / 2, -Width / 2, Width, Width);

    private TileAtlas? _atlas;
    private Chart? _chart;
    private readonly List<Rid> _tileItems = [];
    private Rid _nextMarker;

    private readonly HashSet<int> _twirls = [];
    private readonly Dictionary<int, bool> _speedChanges = []; // floor → 빨라짐 여부

    private int _requestedFloor; // 다른 스레드(게임 엔진)에서 쓸 수 있다
    private int _appliedFloor;

    /// <summary>현재 타일. 아무 스레드에서나 설정할 수 있고, 화면 반영은 다음 프레임에 메인 스레드에서 한다.</summary>
    public int CurrentFloor
    {
        get => Volatile.Read(ref _requestedFloor);
        set => Volatile.Write(ref _requestedFloor, value);
    }

    public void Setup(Chart chart)
    {
        _atlas ??= TileAtlas.Load(ArmTexturePath, JointTexturePath);
        FreeItems();
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

        CurrentFloor = 0;
        _appliedFloor = 0;

        var parent = GetCanvasItem();
        for (var i = 0; i < tiles.Count; i++)
        {
            var item = CreateItem(parent, Coords.ToScreen(tiles[i].Position));
            // draw index 가 클수록 나중에(위에) 그려진다 → 앞 순서 타일이 위로
            RenderingServer.CanvasItemSetDrawIndex(item, tiles.Count - i);
            _tileItems.Add(item);
            RecordTile(i);
        }

        _nextMarker = CreateItem(parent, Vector2.Zero);
        RenderingServer.CanvasItemSetDrawIndex(_nextMarker, tiles.Count + 1);
        RenderingServer.CanvasItemAddPolyline(_nextMarker, ArcPoints(Width * 0.62f, 0, Mathf.Tau, 32), new[] { Palette.NextTile }, 3, true);
        MoveNextMarker();
    }

    public override void _Process(double delta)
    {
        var floor = CurrentFloor;
        if (_chart is null || floor == _appliedFloor)
            return;

        // 지나감 ↔ 안 지나감 상태가 바뀐 타일만 다시 기록 (보통 1개, 재시작 시에는 지나갔던 타일 전부)
        var (from, to) = floor > _appliedFloor ? (_appliedFloor, floor) : (floor, _appliedFloor);
        _appliedFloor = floor;
        for (var i = from; i < to && i < _tileItems.Count; i++)
            RecordTile(i);
        MoveNextMarker();
    }

    public override void _ExitTree() => FreeItems();

    /// <summary>타일 하나의 그리기 명령을 (다시) 기록한다. 좌표는 타일 중심 기준.</summary>
    private void RecordTile(int index)
    {
        var item = _tileItems[index];
        var tiles = _chart!.Tiles;
        var passed = index < _appliedFloor;
        var color = passed ? Palette.TrackPassed : Palette.Track;
        var center = Coords.ToScreen(tiles[index].Position);

        // 이웃 타일 방향으로 팔을 뻗는다 (미드스핀처럼 같은 자리면 생략)
        Span<float> armAngles = stackalloc float[2];
        var armCount = 0;
        if (index > 0 && TryDirection(center, Coords.ToScreen(tiles[index - 1].Position), out var toPrev))
            armAngles[armCount++] = toPrev;
        if (index + 1 < tiles.Count && TryDirection(center, Coords.ToScreen(tiles[index + 1].Position), out var toNext))
            armAngles[armCount++] = toNext;

        RenderingServer.CanvasItemClear(item);
        var atlas = _atlas!;
        var texture = atlas.Texture.GetRid();

        // 1차: 테두리 포함 원본, 2차: 채움만 → 팔과 원 사이의 안쪽 테두리가 지워진다
        for (var pass = 0; pass < 2; pass++)
        {
            var (armRegion, jointRegion) = pass == 0 ? (atlas.Arm, atlas.Joint) : (atlas.ArmFill, atlas.JointFill);
            for (var a = 0; a < armCount; a++)
            {
                RenderingServer.CanvasItemAddSetTransform(item, new Transform2D(armAngles[a], Vector2.Zero));
                RenderingServer.CanvasItemAddTextureRectRegion(item, ArmRect, texture, armRegion, color);
            }
            RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
            RenderingServer.CanvasItemAddTextureRectRegion(item, JointRect, texture, jointRegion, color);
        }

        // 아이콘
        var dim = new Color(1, 1, 1, passed ? IconDim : 1);
        if (tiles[index].IsMidspin)
            RenderingServer.CanvasItemAddCircle(item, Vector2.Zero, Width * 0.3f, Palette.Midspin);
        if (_twirls.Contains(index))
            RenderingServer.CanvasItemAddPolyline(item, ArcPoints(Width * 0.3f, 0.3f, Mathf.Tau - 0.3f, 24), new[] { Palette.Twirl * dim }, 4, true);
        if (_speedChanges.TryGetValue(index, out var faster))
            RenderingServer.CanvasItemAddCircle(item, Vector2.Zero, Width * 0.18f, (faster ? Palette.SpeedUp : Palette.SpeedDown) * dim);
    }

    private void MoveNextMarker()
    {
        var tiles = _chart!.Tiles;
        var next = _appliedFloor + 1;
        RenderingServer.CanvasItemSetVisible(_nextMarker, next < tiles.Count);
        if (next < tiles.Count)
            RenderingServer.CanvasItemSetTransform(_nextMarker, new Transform2D(0, Coords.ToScreen(tiles[next].Position)));
    }

    private static Rid CreateItem(Rid parent, Vector2 position)
    {
        var item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(item, parent);
        RenderingServer.CanvasItemSetTransform(item, new Transform2D(0, position));
        RenderingServer.CanvasItemSetDefaultTextureFilter(item, RenderingServer.CanvasItemTextureFilter.LinearWithMipmaps);
        return item;
    }

    private void FreeItems()
    {
        foreach (var item in _tileItems)
            RenderingServer.FreeRid(item);
        _tileItems.Clear();

        if (_nextMarker.IsValid)
            RenderingServer.FreeRid(_nextMarker);
        _nextMarker = default;
    }

    private static bool TryDirection(Vector2 from, Vector2 to, out float angle)
    {
        angle = (to - from).Angle();
        return !from.IsEqualApprox(to);
    }

    private static Vector2[] ArcPoints(float radius, float startAngle, float endAngle, int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
            points[i] = Vector2.FromAngle(Mathf.Lerp(startAngle, endAngle, (float)i / segments)) * radius;
        return points;
    }
}
