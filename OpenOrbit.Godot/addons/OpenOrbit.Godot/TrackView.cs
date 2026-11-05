using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Godot;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Track;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Godot;

/// <summary>
/// 타일 경로 렌더링.
/// <para>
/// 타일마다 RenderingServer 캔버스 아이템을 하나씩 만들고, 모양은 채보를 열 때 <b>흰색으로</b> 한 번만 기록한다.
/// 색, 위치, 회전, 크기, 투명도는 매 프레임 <see cref="Chart.Track"/> 타임라인을 계산해서 아이템의 transform 과
/// modulate 로만 바꾼다. 그래서 색 애니메이션이나 MoveTrack 이 있어도 그리기 명령을 다시 기록하지 않는다.
/// 값이 바뀐 아이템에만 RenderingServer 를 호출하고, 화면 밖 아이템은 Godot 가 컬링한다.
/// </para>
/// <para>
/// 아이템 구조: 타일 아이템(transform, 투명도 = modulate, 색 = self_modulate) 아래에 아이콘 아이템(트윌, 속도 변화).
/// self_modulate 는 자식에게 전해지지 않으므로 아이콘은 트랙 색에 물들지 않고 투명도만 같이 받는다.
/// </para>
/// <para>
/// 겹쳐 있는 타일은 순서가 앞선 타일이 위에 보이도록 draw index 를 역순으로 준다.
/// </para>
/// <para>
/// 미드스핀 직전 타일은 행성이 찍기만 하고 돌아 나오는 막다른 타일이라 뾰족한 조각(midspin.png)으로 그린다.
/// 미드스핀 자체는 한 칸 뒤 타일과 같은 자리에 있으므로 그 타일과 한 덩어리로 묶어 함께 그린다
/// (따로 그리면 위에 있는 타일의 테두리가 아래 타일의 팔을 가로지른다). 묶인 미드스핀은 그 타일의 모습을 따른다.
/// </para>
/// </summary>
[GlobalClass]
public partial class TrackView : Node2D
{
    private const float Width = 0.5f * Coords.UnitPx; // 행성(지름 약 0.4)이 꽉 들어찰 만큼
    private const float ArmLength = 0.46f * Coords.UnitPx; // 0.5 보다 짧게 그려 타일 사이에 틈을 둔다
    private const float IconDim = 0.3f;
    /// <summary>지나간 타일의 밝기. 곱해서 어둡게 하므로 배경이 어두워야 자연스럽다.</summary>
    private const float PassedBrightness = 0.25f;

    private static readonly Rect2 ArmRect = new(0, -Width / 2, ArmLength, Width);
    private static readonly Rect2 JointRect = new(-Width / 2, -Width / 2, Width, Width);
    // 미드스핀 조각 (1:1 이미지, 진행 방향 기준): 평평한 쪽이 앞 타일 쪽 팔 자리에서 시작한다
    private static readonly Rect2 MidspinRect = new(-ArmLength, -Width / 2, Width, Width);

    [Export] public Texture2D? ArmTexture { get; private set; }
    [Export] public Texture2D? JointTexture { get; private set; }
    [Export] public Texture2D? MidspinTexture { get; private set; }

    /// <summary>지나간 타일을 어둡게 그린다 (얼불춤에는 없는 표시).</summary>
    [Export] public bool DimPassedTiles { get; set; } = true;

    private TileAtlas? _atlas;
    private Chart? _chart;
    private int[] _owners = []; // 타일 → 그 타일을 그리는 타일 (미드스핀이면 같은 자리의 타일, 아니면 자기 자신)
    private readonly Dictionary<int, List<int>> _members = []; // 미드스핀을 함께 그리는 타일 → 미드스핀들
    private int[] _drawn = []; // 아이템이 있는 타일 (= 미드스핀이 아닌 owner) 들
    private Rid _nextMarker;

    // _drawn 과 같은 순서
    private Rid[] _items = [];
    private Rid[] _icons = [];
    private Applied[] _applied = [];

    private readonly HashSet<int> _twirls = [];
    private readonly Dictionary<int, bool> _speedChanges = []; // floor → 빨라짐 여부

    private int _requestedFloor; // 다른 스레드(게임 엔진)에서 쓸 수 있다

    /// <summary>마지막으로 RenderingServer 에 넘긴 값. 같으면 호출을 건너뛴다.</summary>
    private struct Applied
    {
        public bool Valid;
        public bool Visible;
        public Transform2D Transform;
        public Color Modulate;
        public Color SelfModulate;
        public bool IconsDimmed;
        /// <summary>[EvaluatedAt, StableUntil) 동안은 타임라인 값이 그대로라 계산을 건너뛴다.</summary>
        public double EvaluatedAt;
        public double StableUntil;
        public TileVisual Visual;
    }

    /// <summary>현재 타일. 아무 스레드에서나 설정할 수 있고, 화면 반영은 다음 프레임에 메인 스레드에서 한다.</summary>
    public int CurrentFloor
    {
        get => Volatile.Read(ref _requestedFloor);
        set => Volatile.Write(ref _requestedFloor, value);
    }

    /// <summary>타일 모습을 계산할 곡 시각(ms). 메인 스레드에서 매 프레임 설정한다.</summary>
    public double SongTimeMs { get; set; }

    public void Setup(Chart chart)
    {
        ArgumentNullException.ThrowIfNull(ArmTexture);
        ArgumentNullException.ThrowIfNull(JointTexture);
        ArgumentNullException.ThrowIfNull(MidspinTexture);
        _atlas ??= TileAtlas.Load(ArmTexture.ResourcePath, JointTexture.ResourcePath, MidspinTexture.ResourcePath);
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
        _drawn = Enumerable.Range(0, tiles.Count).Where(i => _owners[i] == i).ToArray();

        CurrentFloor = 0;
        SongTimeMs = tiles[0].TimeMs;

        var parent = GetCanvasItem();
        _items = new Rid[_drawn.Length];
        _icons = new Rid[_drawn.Length];
        _applied = new Applied[_drawn.Length];
        for (var k = 0; k < _drawn.Length; k++)
        {
            var owner = _drawn[k];
            var item = CreateItem(parent);
            // draw index 가 클수록 나중에(위에) 그려진다 → 앞 순서 타일이 위로
            RenderingServer.CanvasItemSetDrawIndex(item, tiles.Count - owner);
            _items[k] = item;
            _icons[k] = CreateItem(item);
            RecordTile(owner, item, _icons[k]);
        }

        _nextMarker = CreateItem(parent);
        RenderingServer.CanvasItemSetDrawIndex(_nextMarker, tiles.Count + 1);
        RenderingServer.CanvasItemAddPolyline(_nextMarker, ArcPoints(Width * 0.62f, 0, Mathf.Tau, 32), new[] { Palette.NextTile }, 3, true);

        Refresh();
    }

    public override void _Process(double delta) => Refresh();

    public override void _ExitTree() => FreeItems();

    /// <summary>타일 floor 가 MoveTrack/애니메이션으로 원래 자리에서 움직인 만큼 (화면 좌표).</summary>
    public Vector2 TileOffset(int floor)
    {
        if (_chart is null)
            return Vector2.Zero;
        var offset = _chart.Track.Evaluate(_owners[floor], SongTimeMs).Offset;
        return new Vector2((float)offset.X, -(float)offset.Y) * Coords.UnitPx;
    }

    /// <summary>타일 floor 의 지금 화면 위치 (움직인 만큼 포함).</summary>
    public Vector2 TilePosition(int floor) =>
        _chart is null ? Vector2.Zero : Coords.ToScreen(_chart.Tiles[floor].Position) + TileOffset(floor);

    /// <summary>켜져 있으면 행성과 카메라도 움직인 타일을 따라간다 (settings.stickToFloors).</summary>
    public bool StickToFloors => _chart?.Track.StickToFloors ?? false;

    /// <summary>모든 타일의 모습을 지금 시각으로 계산해서 바뀐 것만 반영한다.</summary>
    private void Refresh()
    {
        if (_chart is null)
            return;

        var time = SongTimeMs;
        var floor = CurrentFloor;
        var tiles = _chart.Tiles;
        var track = _chart.Track;

        for (var k = 0; k < _drawn.Length; k++)
        {
            var owner = _drawn[k];
            ref var applied = ref _applied[k];

            // 묶음의 마지막 타일을 지나야 지나간 것 (미드스핀 뒤에 다시 이 자리를 중심으로 도는 동안은 밟고 있는 타일)
            var last = _members.TryGetValue(owner, out var members) ? members[^1] : owner;
            var passed = DimPassedTiles && last < floor;

            // 타임라인 값이 그대로인 구간이면 계산도, RenderingServer 호출도 하지 않는다
            if (applied.Valid && time >= applied.EvaluatedAt && time < applied.StableUntil)
            {
                if (applied.IconsDimmed == passed)
                    continue;
            }
            else
            {
                applied.Visual = track.Evaluate(owner, time);
                applied.EvaluatedAt = time;
                applied.StableUntil = track.StableUntil(owner, time);
            }
            var visual = applied.Visual;

            var visible = !visual.IsHidden;
            if (!applied.Valid || applied.Visible != visible)
                RenderingServer.CanvasItemSetVisible(_items[k], visible);
            applied.Visible = visible;
            if (!visible)
            {
                // 지나감 표시(IconsDimmed)는 다시 보일 때 반영한다
                applied.Valid = true;
                continue;
            }

            var transform = new Transform2D(
                -Mathf.DegToRad((float)visual.Rotation), // Core 는 반시계, 화면은 Y 가 아래라 반대
                new Vector2((float)visual.Scale.X, (float)visual.Scale.Y),
                0,
                Coords.ToScreen(tiles[owner].Position + visual.Offset));

            var c = visual.Color;
            var brightness = passed ? PassedBrightness : 1f;
            var selfModulate = new Color(c.R * brightness, c.G * brightness, c.B * brightness, c.A);
            var modulate = new Color(1, 1, 1, (float)visual.Opacity);

            if (!applied.Valid || applied.Transform != transform)
                RenderingServer.CanvasItemSetTransform(_items[k], transform);
            if (!applied.Valid || applied.Modulate != modulate)
                RenderingServer.CanvasItemSetModulate(_items[k], modulate);
            if (!applied.Valid || applied.SelfModulate != selfModulate)
                RenderingServer.CanvasItemSetSelfModulate(_items[k], selfModulate);
            if (!applied.Valid || applied.IconsDimmed != passed)
                RenderingServer.CanvasItemSetSelfModulate(_icons[k], new Color(1, 1, 1, passed ? IconDim : 1));

            applied.Valid = true;
            applied.Transform = transform;
            applied.Modulate = modulate;
            applied.SelfModulate = selfModulate;
            applied.IconsDimmed = passed;
        }

        var next = floor + 1;
        RenderingServer.CanvasItemSetVisible(_nextMarker, next < tiles.Count);
        if (next < tiles.Count)
            RenderingServer.CanvasItemSetTransform(_nextMarker, new Transform2D(0, TilePosition(next)));
    }

    /// <summary>
    /// 타일 하나(와 같은 자리의 미드스핀들)의 그리기 명령을 기록한다. 좌표는 타일 중심 기준이고 색은 흰색이다.
    /// owner 는 미드스핀이 아닌 타일이어야 한다.
    /// </summary>
    private void RecordTile(int owner, Rid item, Rid icons)
    {
        var tiles = _chart!.Tiles;
        var members = _members.GetValueOrDefault(owner);
        var center = Coords.ToScreen(tiles[owner].Position);

        var atlas = _atlas!;
        var texture = atlas.Texture.GetRid();
        var white = Colors.White;

        if (owner + 1 < tiles.Count && tiles[owner + 1].IsMidspin)
        {
            // 미드스핀 직전 타일: 들어온 방향으로 뾰족하게 끝나는 조각 하나 (한 칸 뒤 = 미드스핀 자리에서 들어온다)
            var forward = (center - Coords.ToScreen(tiles[owner + 1].Position)).Angle();
            RenderingServer.CanvasItemAddSetTransform(item, new Transform2D(forward, Vector2.Zero));
            RenderingServer.CanvasItemAddTextureRectRegion(item, MidspinRect, texture, atlas.Midspin, white);
            RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
        }
        else
        {
            // 묶음 안 타일들의 앞뒤 타일 방향으로 팔을 뻗는다 (같은 자리거나 같은 방향이면 생략).
            // 방향은 원래 위치 기준이다. MoveTrack 으로 이웃 타일이 움직여도 팔은 그대로 둔다.
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
                    RenderingServer.CanvasItemAddTextureRectRegion(item, ArmRect, texture, armRegion, white);
                }
                RenderingServer.CanvasItemAddSetTransform(item, Transform2D.Identity);
                RenderingServer.CanvasItemAddTextureRectRegion(item, JointRect, texture, jointRegion, white);
            }
        }

        // 아이콘 (묶음 안 어느 타일에 있든 가운데에)
        foreach (var i in members is null ? [owner] : members.Prepend(owner))
        {
            if (_twirls.Contains(i))
                RenderingServer.CanvasItemAddPolyline(icons, ArcPoints(Width * 0.3f, 0.3f, Mathf.Tau - 0.3f, 24), new[] { Palette.Twirl }, 4, true);
            if (_speedChanges.TryGetValue(i, out var faster))
                RenderingServer.CanvasItemAddCircle(icons, Vector2.Zero, Width * 0.18f, faster ? Palette.SpeedUp : Palette.SpeedDown);
        }
    }

    private static Rid CreateItem(Rid parent)
    {
        var item = RenderingServer.CanvasItemCreate();
        RenderingServer.CanvasItemSetParent(item, parent);
        RenderingServer.CanvasItemSetDefaultTextureFilter(item, RenderingServer.CanvasItemTextureFilter.LinearWithMipmaps);
        return item;
    }

    private void FreeItems()
    {
        // 자식(아이콘) 아이템을 먼저 해제한다
        foreach (var item in _icons)
            RenderingServer.FreeRid(item);
        foreach (var item in _items)
            RenderingServer.FreeRid(item);
        _icons = [];
        _items = [];
        _applied = [];

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
