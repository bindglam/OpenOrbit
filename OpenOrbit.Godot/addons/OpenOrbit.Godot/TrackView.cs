using System;
using System.Collections.Generic;
using System.Threading;
using Godot;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Track;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Godot;

/// <summary>
/// 타일 경로 렌더링. 타일 모양은 <see cref="FloorGeometry"/> 의 거리 함수로, 셰이더가 픽셀마다 계산한다.
/// <para>
/// 타일마다 RenderingServer 캔버스 아이템을 하나씩 만들고, 모양은 <b>흰색으로</b> 기록해 둔다.
/// 메시는 타일을 덮는 사각형이고, 모양(두 팔 사이 각도, 크기)마다 하나만 만들어 같은 모양의 타일이 돌려서 함께 쓴다.
/// 트랙 스타일이 바뀌면(RecolorTrack 등) 그 타일의 메시와 머티리얼만 다시 기록한다.
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
/// 미드스핀은 한 칸 뒤 타일과 같은 자리에 겹쳐 그려지는 보통 타일이다.
/// </para>
/// <para>
/// 색칠: 외곽선에서 안쪽으로 들어간 깊이로 스타일의 테두리 그라데이션을 고르고,
/// (Standard 만) 노이즈로 테두리를 군데군데 밝히고 표면 무늬를 타일마다 다르게 옮기고 돌려서 곱한다.
/// 스타일마다 테두리, 그림자 색, 크기가 다르다. Gems 는 보석 그림을 붙인 정사각형이다.
/// 텍스처가 없으면 테두리는 비슷한 기본 그라데이션, 무늬와 노이즈는 없음, 보석은 흰 정사각형.
/// </para>
/// </summary>
[GlobalClass]
public partial class TrackView : Node2D
{
    private const float Width = 0.55f * Coords.UnitPx; // 아이콘 크기 기준 (긴 타일의 폭)
    private const float IconDim = 0.3f;
    /// <summary>지나간 타일의 밝기. 곱해서 어둡게 하므로 배경이 어두워야 자연스럽다.</summary>
    private const float PassedBrightness = 0.25f;

    /// <summary>
    /// 타일 셰이더. 덮는 사각형의 픽셀마다 <see cref="FloorGeometry"/> 와 같은 거리 함수로 모양, 테두리 깊이, 그림자를 정한다
    /// (두 코드는 함께 고쳐야 한다). 모양 값은 데이터 텍스처(shape_data)에 있고, UV 는 그 위치와 무늬 변형 번호다.
    /// 좌표는 메시 기준 얼불춤 유닛이라 타일이 움직이거나 돌아도 무늬가 따라간다.
    /// </summary>
    private 

    /// <summary>Standard 테두리 그라데이션 (위 = 타일 가장자리). 비워 두면 비슷한 기본값을 만든다. 다른 스타일 테두리도 같다.</summary>
    [Export] public Texture2D? BorderTexture { get; set; }
    [Export] public Texture2D? BasicBorderTexture { get; set; }
    [Export] public Texture2D? NeonBorderTexture { get; set; }
    [Export] public Texture2D? NeonLightBorderTexture { get; set; }
    [Export] public Texture2D? MinimalBorderTexture { get; set; }
    /// <summary>Standard 타일 표면 무늬 (반복 텍스처). 10 얼불춤 유닛마다 반복된다.</summary>
    [Export] public Texture2D? SurfaceTexture { get; set; }
    /// <summary>Standard 테두리를 군데군데 옅게 하는 노이즈 (반복 텍스처, R 채널).</summary>
    [Export] public Texture2D? NoiseTexture { get; set; }
    /// <summary>Gems 스타일 보석 그림. 1 얼불춤 유닛 정사각형에 붙는다.</summary>
    [Export] public Texture2D? GemTexture { get; set; }

    /// <summary>지나간 타일을 어둡게 그린다 (얼불춤에는 없는 표시).</summary>
    [Export] public bool DimPassedTiles { get; set; } = true;

    private Shader? _shader;
    private readonly ShaderMaterial[] _materials = new ShaderMaterial[Enum.GetValues<TrackStyle>().Length];
    /// <summary>
    /// 타일마다 표면 무늬 위치를 다르게 한다. 타일별 셰이더 값(instance uniform)은 개수 제한이 있어서,
    /// 대신 모양마다 무늬 위치가 다른 메시를 이만큼 만들어 타일 번호로 고른다.
    /// </summary>
    private const int SurfaceVariants = 16;

    private readonly Dictionary<(int Shape, int Variant), ArrayMesh> _meshes = []; // 모양 번호 + 변형 → 메시

    /// <summary>모양 데이터 텍스처 한 줄에 들어가는 모양 수 (모양 하나에 2 픽셀).</summary>
    private const int ShapesPerRow = 512;
    private readonly Dictionary<FloorGeometry, int> _shapeIndex = [];
    private readonly List<FloorGeometry> _shapeList = [];
    private readonly ImageTexture _shapeTexture = new();
    private bool _shapeDataDirty;
    private Chart? _chart;
    private FloorShape[] _shapes = [];
    private Rid _nextMarker;

    // 타일 순서
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
        /// <summary>메시를 기록한 스타일. null 이면 아직 기록하지 않았다.</summary>
        public TrackStyle? RecordedStyle;
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
        FreeItems();
        _chart = chart;
        // 모양 번호는 채보마다 새로 매긴다
        _meshes.Clear();
        _shapeIndex.Clear();
        _shapeList.Clear();
        _shapeDataDirty = true;
        CreateMaterials(chart);
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
        SongTimeMs = tiles[0].TimeMs;

        _shapes = FloorMesh.Shapes(chart);
        var parent = GetCanvasItem();
        _items = new Rid[tiles.Count];
        _icons = new Rid[tiles.Count];
        _applied = new Applied[tiles.Count];
        for (var i = 0; i < tiles.Count; i++)
        {
            var item = CreateItem(parent);
            // draw index 가 클수록 나중에(위에) 그려진다 → 앞 순서 타일이 위로
            RenderingServer.CanvasItemSetDrawIndex(item, tiles.Count - i);
            _items[i] = item;
            _icons[i] = CreateItem(item);
            RecordIcons(i, _icons[i]);
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
        var offset = _chart.Track.Evaluate(floor, SongTimeMs).Offset;
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

        for (var k = 0; k < _items.Length; k++)
        {
            ref var applied = ref _applied[k];
            var passed = DimPassedTiles && k < floor;

            // 타임라인 값이 그대로인 구간이면 계산도, RenderingServer 호출도 하지 않는다
            if (applied.Valid && time >= applied.EvaluatedAt && time < applied.StableUntil)
            {
                if (applied.IconsDimmed == passed)
                    continue;
            }
            else
            {
                applied.Visual = track.Evaluate(k, time);
                applied.EvaluatedAt = time;
                applied.StableUntil = track.StableUntil(k, time);
            }
            var visual = applied.Visual;
            if (applied.RecordedStyle != visual.Style)
            {
                RecordMesh(k, visual.Style);
                applied.RecordedStyle = visual.Style;
            }

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
                Coords.ToScreen(tiles[k].Position + visual.Offset));

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

        if (_shapeDataDirty)
            UploadShapeData();

        var next = floor + 1;
        RenderingServer.CanvasItemSetVisible(_nextMarker, next < tiles.Count);
        if (next < tiles.Count)
            RenderingServer.CanvasItemSetTransform(_nextMarker, new Transform2D(0, TilePosition(next)));
    }

    /// <summary>타일 하나의 아이콘(트윌, 속도 변화)을 기록한다. 좌표는 타일 중심 기준.</summary>
    private void RecordIcons(int floor, Rid icons)
    {
        if (_twirls.Contains(floor))
            RenderingServer.CanvasItemAddPolyline(icons, ArcPoints(Width * 0.3f, 0.3f, Mathf.Tau - 0.3f, 24), new[] { Palette.Twirl }, 4, true);
        if (_speedChanges.TryGetValue(floor, out var faster))
            RenderingServer.CanvasItemAddCircle(icons, Vector2.Zero, Width * 0.18f, faster ? Palette.SpeedUp : Palette.SpeedDown);
    }

    /// <summary>타일 하나의 메시를 그 스타일로 (다시) 기록한다. 색은 흰색이고 타일 중심 기준이다.</summary>
    private void RecordMesh(int floor, TrackStyle style)
    {
        var item = _items[floor];
        var shape = _shapes[floor];
        RenderingServer.CanvasItemClear(item);
        RenderingServer.CanvasItemSetMaterial(item, _materials[(int)style].GetRid());
        // Core 는 반시계, 화면은 Y 가 아래라 반대로 돌린다. Gems 정사각형은 돌리지 않는다
        var rotation = style == TrackStyle.Gems ? 0 : -Mathf.DegToRad((float)shape.Rotation);
        RenderingServer.CanvasItemAddMesh(item, MeshFor(shape, style, Variant(floor)).GetRid(), new Transform2D(rotation, Vector2.Zero));
    }

    /// <summary>모양별 기본 메시. 같은 채보 안에서는 같은 모양의 타일이 함께 쓴다.</summary>
    private ArrayMesh MeshFor(FloorShape shape, TrackStyle style, int variant)
    {
        var tileShape = _chart!.Level.Settings.TileShape;
        var geometry = FloorMesh.Geometry(shape, style, tileShape);
        if (geometry.Kind == FloorKind.Sprite)
            variant = 0; // 보석은 무늬를 옮기지 않는다
        if (!_shapeIndex.TryGetValue(geometry, out var index))
        {
            index = _shapeList.Count;
            _shapeIndex[geometry] = index;
            _shapeList.Add(geometry);
            _shapeDataDirty = true;
        }
        if (_meshes.TryGetValue((index, variant), out var mesh))
            return mesh;

        var data = FloorMesh.Build(geometry);
        // 셰이더가 정수로 읽는다. 2048 미만이라 UV 가 half float 로 줄어도 정확하다
        var uv = new Vector2(index % ShapesPerRow, variant + SurfaceVariants * (index / ShapesPerRow));
        var vertices = new Vector2[data.Vertices.Length];
        var uvs = new Vector2[data.Vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            vertices[i] = Coords.ToScreen(data.Vertices[i]);
            uvs[i] = uv;
        }
        var arrays = new global::Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.TexUV] = uvs;
        arrays[(int)Mesh.ArrayType.Index] = data.Indices;
        mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _meshes[(index, variant)] = mesh;
        return mesh;
    }

    /// <summary>
    /// 모양 값을 데이터 텍스처에 올린다. 모양 하나에 2 픽셀(얼불춤 유닛):
    /// (종류, 두 팔 사이 각도, 팔 길이, 폭의 절반), (모서리 중심 x, y, 모서리 반지름, 두 팔 겹침).
    /// </summary>
    private void UploadShapeData()
    {
        _shapeDataDirty = false;
        var unitsPerTile = (float)_chart!.UnitsPerTile;
        var rows = Math.Max(1, (_shapeList.Count + ShapesPerRow - 1) / ShapesPerRow);
        var image = Image.CreateEmpty(ShapesPerRow * 2, rows, false, Image.Format.Rgbaf);
        for (var i = 0; i < _shapeList.Count; i++)
        {
            var g = _shapeList[i];
            int x = i % ShapesPerRow * 2, y = i / ShapesPerRow;
            image.SetPixel(x, y, new Color((int)g.Kind, (float)g.Spread, (float)g.Length * unitsPerTile, (float)g.HalfWidth * unitsPerTile));
            image.SetPixel(x + 1, y, new Color((float)g.CornerCenter.X * unitsPerTile, (float)g.CornerCenter.Y * unitsPerTile,
                (float)g.CornerRadius * unitsPerTile, g.ArmsOverlap ? 1 : 0));
        }
        _shapeTexture.SetImage(image);
    }

    /// <summary>스타일마다 머티리얼 하나.</summary>
    private void CreateMaterials(Chart chart)
    {
        _shader ??= new Shader { Code = FloorShader };
        var customShadow = chart.Level.Settings.TrackShadowColor;
        foreach (var style in Enum.GetValues<TrackStyle>())
        {
            var material = new ShaderMaterial { Shader = _shader };
            material.SetShaderParameter("units_per_pixel", (float)chart.UnitsPerTile / Coords.UnitPx);
            material.SetShaderParameter("shadow_size", (float)FloorMesh.ShadowSize);
            material.SetShaderParameter("shape_data", _shapeTexture);

            var (border, shadow) = style switch
            {
                TrackStyle.Basic => (BasicBorderTexture ?? DefaultBorder(style), Colors.Transparent),
                TrackStyle.Minimal => (MinimalBorderTexture ?? DefaultBorder(style), Colors.Transparent),
                TrackStyle.Neon => (NeonBorderTexture ?? DefaultBorder(style), new Color(1, 1, 1, 0.35f)),
                TrackStyle.NeonLight => (NeonLightBorderTexture ?? DefaultBorder(style), new Color(1, 1, 1, 0.35f)),
                _ => (BorderTexture ?? DefaultBorder(TrackStyle.Standard), new Color(0, 0, 0, 0.45f)),
            };
            if (customShadow is { } c)
                shadow = new Color((float)c.R, (float)c.G, (float)c.B, (float)c.A);
            material.SetShaderParameter("border_texture", border);
            material.SetShaderParameter("shadow_color", shadow);
            // Standard 와 Gems 는 테두리를 1.23 배로 밝힌다
            material.SetShaderParameter("border_tune", style is TrackStyle.Standard or TrackStyle.Gems ? 1.23f : 1f);

            if (style == TrackStyle.Standard)
            {
                if (SurfaceTexture is not null)
                    material.SetShaderParameter("surface_texture", SurfaceTexture);
                if (NoiseTexture is not null)
                    material.SetShaderParameter("noise_texture", NoiseTexture);
            }
            else if (style == TrackStyle.Gems)
            {
                material.SetShaderParameter("sprite", true);
                if (GemTexture is not null)
                    material.SetShaderParameter("surface_texture", GemTexture);
            }
            _materials[(int)style] = material;
        }
    }

    /// <summary>
    /// 텍스처가 없을 때 쓰는 테두리 그라데이션 (128 줄, 위가 타일 가장자리).
    /// </summary>
    private static ImageTexture DefaultBorder(TrackStyle style)
    {
        const int height = 128;
        var image = Image.CreateEmpty(1, height, false, Image.Format.L8);
        for (var y = 0; y < height; y++)
        {
            var v = style switch
            {
                // 가장자리 검은 선
                TrackStyle.Basic => y < 18 ? 0f : 1f,
                // 흰 가장자리, 안쪽은 검정 / 회색으로 가라앉는다
                TrackStyle.Neon => y < 10 ? 1f : y < 16 ? Mathf.Lerp(1f, 0.32f, (y - 10) / 6f) : 0.26f * Mathf.Exp(-(y - 16) / 20f),
                TrackStyle.NeonLight => y < 10 ? 1f : y < 16 ? Mathf.Lerp(1f, 0.63f, (y - 10) / 6f) : 0.5f + 0.13f * Mathf.Exp(-(y - 16) / 20f),
                TrackStyle.Minimal => 1f,
                // Standard: 가장자리 5% 는 어둡고 10% 까지 밝아진다
                _ => Mathf.Lerp(0.34f, 1f, Mathf.Clamp((y - 7) / 5f, 0, 1)),
            };
            image.SetPixel(0, y, new Color(v, v, v));
        }
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>타일 번호로 정해지는 무늬 변형 번호.</summary>
    private static int Variant(int floor)
    {
        var h = (uint)floor * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        return (int)(h % SurfaceVariants);
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

    private static Vector2[] ArcPoints(float radius, float startAngle, float endAngle, int segments)
    {
        var points = new Vector2[segments + 1];
        for (var i = 0; i <= segments; i++)
            points[i] = Vector2.FromAngle(Mathf.Lerp(startAngle, endAngle, (float)i / segments)) * radius;
        return points;
    }
}
