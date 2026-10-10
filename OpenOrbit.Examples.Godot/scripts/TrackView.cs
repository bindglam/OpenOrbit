using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Geometry;

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
/// <para>
/// 미드스핀 직전 타일은 행성이 찍기만 하고 돌아 나오는 막다른 타일이라 뾰족한 조각(midspin.png)으로 그린다.
/// 미드스핀 자체는 한 칸 뒤 타일과 같은 자리에 있으므로 그 타일과 한 덩어리로 묶어 함께 그린다
/// (따로 그리면 위에 있는 타일의 테두리가 아래 타일의 팔을 가로지른다).
/// </para>
/// </summary>
public partial class TrackView : Node2D
{
    private const string ArmTexturePath = "res://assets/sprite/tile/arm.png";
    private const string JointTexturePath = "res://assets/sprite/tile/joint.png";
    private const string MidspinTexturePath = "res://assets/sprite/tile/midspin.png";

    private const float Width = 0.5f * Coords.UnitPx; // 행성(지름 약 0.4)이 꽉 들어찰 만큼
    private const float ArmLength = 0.46f * Coords.UnitPx; // 0.5 보다 짧게 그려 타일 사이에 틈을 둔다
    private const float IconDim = 0.3f;

    private static readonly Rect2 ArmRect = new(0, -Width / 2, ArmLength, Width);
    private static readonly Rect2 JointRect = new(-Width / 2, -Width / 2, Width, Width);
    // 미드스핀 조각 (1:1 이미지, 진행 방향 기준): 평평한 쪽이 앞 타일 쪽 팔 자리에서 시작한다
    private static readonly Rect2 MidspinRect = new(-ArmLength, -Width / 2, Width, Width);

    private TileAtlas? _atlas;
    private Chart? _chart;
    private readonly List<Rid> _tileItems = [];
    private int[] _owners = []; // 타일 → 그 타일을 그리는 타일 (미드스핀이면 같은 자리의 타일, 아니면 자기 자신)
    private readonly Dictionary<int, List<int>> _members = []; // 미드스핀을 함께 그리는 타일 → 미드스핀들
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
        _atlas ??= TileAtlas.Load(ArmTexturePath, JointTexturePath, MidspinTexturePath);
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

        _owners = new int[tiles.Count];
        _members.Clear();
        for (var i = 0; i < tiles.Count; i++)
        {
            // 미드스핀은 두 칸 앞(한 칸 뒤로 돌아온 자리) 타일과 같은 자리
            _owners[i] = i >= 2 && tiles[i].IsMidspin ? _owners[i - 2] : i;
            if (_owners[i] != i)
            {
                if (!_members.TryGetValue(_owners[i], out var members))
                    _members[_owners[i]] = members = [];
                members.Add(i);
            }
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
        }
        for (var i = 0; i < tiles.Count; i++)
        {
            if (_owners[i] == i)
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
        var owners = new HashSet<int>();
        for (var i = from; i < to && i < _tileItems.Count; i++)
            owners.Add(_owners[i]);
        foreach (var owner in owners)
            RecordTile(owner);
        MoveNextMarker();
    }

    public override void _ExitTree() => FreeItems();

    /// <summary>
    /// 타일 하나(와 같은 자리의 미드스핀들)의 그리기 명령을 (다시) 기록한다. 좌표는 타일 중심 기준.
    /// owner 는 미드스핀이 아닌 타일이어야 한다.
    /// </summary>
    private void RecordTile(int owner)
    {
        var item = _tileItems[owner];
        var tiles = _chart!.Tiles;
        var members = _members.GetValueOrDefault(owner);
        var last = members?[^1] ?? owner;

        // 묶음의 마지막 타일을 지나야 지나간 것 (미드스핀 뒤에 다시 이 자리를 중심으로 도는 동안은 밟고 있는 타일)
        var passed = last < _appliedFloor;
        var color = passed ? Palette.TrackPassed : Palette.Track;
        var center = Coords.ToScreen(tiles[owner].Position);

        RenderingServer.CanvasItemClear(item);
        var atlas = _atlas!;
        var texture = atlas.Texture.GetRid();

        if (owner + 1 < tiles.Count && tiles[owner + 1].IsMidspin)
        {
            // 미드스핀 직전 타일: 들어온 방향으로 뾰족하게 끝나는 조각 하나 (한 칸 뒤 = 미드스핀 자리에서 들어온다)
            var forward = (center - Coords.ToScreen(tiles[owner + 1].Position)).Angle();
            RenderingServer.CanvasItemAddSetTransform(item, new Transform2D(forward, Vector2.Zero));
            RenderingServer.CanvasItemAddTextureRectRegion(item, MidspinRect, texture, atlas.Midspin, color);
            RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
        }
        else
        {
            // 묶음 안 타일들의 앞뒤 타일 방향으로 팔을 뻗는다 (같은 자리거나 같은 방향이면 생략)
            var armAngles = new List<float>(4);
            void AddArm(int neighbor)
            {
                if (neighbor < 0 || neighbor >= tiles.Count || _owners[neighbor] == owner)
                    return;
                if (TryDirection(center, Coords.ToScreen(tiles[neighbor].Position), out var angle)
                    && !armAngles.Exists(a => Mathf.IsEqualApprox(Mathf.AngleDifference(a, angle), 0, 1e-3f)))
                    armAngles.Add(angle);
            }
            AddArm(owner - 1);
            AddArm(owner + 1);
            foreach (var m in members ?? [])
            {
                AddArm(m - 1);
                AddArm(m + 1);
            }

            // 1차: 테두리 포함 원본, 2차: 채움만 → 팔과 원 사이의 안쪽 테두리가 지워진다
            for (var pass = 0; pass < 2; pass++)
            {
                var (armRegion, jointRegion) = pass == 0 ? (atlas.Arm, atlas.Joint) : (atlas.ArmFill, atlas.JointFill);
                foreach (var angle in armAngles)
                {
                    RenderingServer.CanvasItemAddSetTransform(item, new Transform2D(angle, Vector2.Zero));
                    RenderingServer.CanvasItemAddTextureRectRegion(item, ArmRect, texture, armRegion, color);
                }
                RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
                RenderingServer.CanvasItemAddTextureRectRegion(item, JointRect, texture, jointRegion, color);
            }
        }

        // 아이콘 (묶음 안 어느 타일에 있든 가운데에)
        var dim = new Color(1, 1, 1, passed ? IconDim : 1);
        foreach (var i in members is null ? [owner] : members.Prepend(owner))
        {
            if (_twirls.Contains(i))
                RenderingServer.CanvasItemAddPolyline(item, ArcPoints(Width * 0.3f, 0.3f, Mathf.Tau - 0.3f, 24), new[] { Palette.Twirl * dim }, 4, true);
            if (_speedChanges.TryGetValue(i, out var faster))
                RenderingServer.CanvasItemAddCircle(item, Vector2.Zero, Width * 0.18f, (faster ? Palette.SpeedUp : Palette.SpeedDown) * dim);
        }
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
