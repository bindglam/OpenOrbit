using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Charts.Levels.Events;
using OpenOrbit.Core.Geometry;

namespace OpenOrbit.Core.Charts.Track;

/// <summary>
/// 한 타일의 특정 시각 모습. 렌더러는 이 값만 보고 그리면 된다.
/// </summary>
/// <param name="Offset">타일 위치(<see cref="Tile.Position"/>)에 더할 오프셋(타일 단위, +Y 위).</param>
/// <param name="Rotation">타일 중심 기준 회전(도, 반시계 방향).</param>
/// <param name="Scale">1 = 원래 크기.</param>
/// <param name="Opacity">0~1. <see cref="Color"/> 의 알파와는 따로 곱해야 한다.</param>
/// <param name="Color">채움 색. 테두리는 렌더러가 스타일에 맞게 만든다.</param>
public readonly record struct TileVisual(
    Vec2 Offset,
    double Rotation,
    Vec2 Scale,
    double Opacity,
    Rgba Color,
    TrackStyle Style,
    double GlowIntensity)
{
    /// <summary>안 보이는 타일 (등장 전, 퇴장 후, 투명도 0, 크기 0).</summary>
    public bool IsHidden => Opacity <= 0 || Color.A <= 0 || Scale.X == 0 || Scale.Y == 0;
}

/// <summary>
/// 트랙 장식 이벤트(ColorTrack, RecolorTrack, MoveTrack, AnimateTrack, PositionTrack 의 모양)를 미리 정리해 두고
/// 타일 하나의 특정 시각 모습을 계산한다. 엔진과 무관한 순수 계산이며 만든 뒤에는 바뀌지 않으므로 아무 스레드에서나 불러도 된다.
/// <para>
/// 얼불춤(ffxMoveFloorPlus, ffxFloorAppearPlus, ffxFloorDisappearPlus, ffxRecolorFloorPlus)을 따른다.
/// 타일의 위치 X/Y, 회전, 크기 X/Y, 투명도는 각각 트윈 하나씩만 갖는다. 새 트윈이 시작되면 이전 트윈은 끝값으로 바로 완료되고
/// (DOTween <c>Kill(complete: true)</c>) 새 트윈은 그 값에서 출발한다. 등장/퇴장 애니메이션도 같은 속성을 움직이는 트윈이다.
/// </para>
/// </summary>
public sealed class TrackTimeline
{
    private enum Property { X, Y, Rotation, ScaleX, ScaleY, Opacity }
    private const int PropertyCount = 6;

    /// <summary>[Start, Start + Duration) 동안 From → To. 끝나면 다음 구간 전까지 To.</summary>
    private readonly record struct Segment(double Start, double Duration, double From, double To, Ease Ease)
    {
        public double End => Start + Duration;
    }

    /// <summary>타일의 트랙 색 상태. ColorStart 는 Stripes 의 기준 타일.</summary>
    private readonly record struct ColorState(TrackColor Color, int ColorStart);

    /// <summary>Start 부터 Color 로 바뀐다. 단색이면 Duration 동안 From 에서 섞인다.</summary>
    private readonly record struct ColorSegment(double Start, double Duration, Ease Ease, ColorState State, Rgba From);

    private readonly int _tileCount;
    private readonly double _pitch;

    // 타일 i, 속성 p: 처음 값은 _initial[i * 6 + p], 구간은 _segments[_starts[i * 6 + p] .. _starts[i * 6 + p + 1]) (시작 시각 순)
    private readonly double[] _initial;
    private readonly Segment[] _segments;
    private readonly int[] _starts;

    private readonly ColorState[] _baseColors;
    private readonly ColorSegment[] _recolors;
    private readonly int[] _recolorStarts;

    public bool StickToFloors { get; }

    private TrackTimeline(
        int tileCount, double pitch, double[] initial, Segment[] segments, int[] starts,
        ColorState[] baseColors, ColorSegment[] recolors, int[] recolorStarts, bool stickToFloors)
    {
        _tileCount = tileCount;
        _pitch = pitch;
        _initial = initial;
        _segments = segments;
        _starts = starts;
        _baseColors = baseColors;
        _recolors = recolors;
        _recolorStarts = recolorStarts;
        StickToFloors = stickToFloors;
    }

    /// <summary>floor 번 타일의 songTimeMs 시점 모습.</summary>
    public TileVisual Evaluate(int floor, double songTimeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(floor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(floor, _tileCount);

        var t = songTimeMs;
        var (color, style) = ColorAt(floor, t);
        return new TileVisual(
            new Vec2(Sample(floor, Property.X, t), Sample(floor, Property.Y, t)),
            Sample(floor, Property.Rotation, t),
            new Vec2(Sample(floor, Property.ScaleX, t), Sample(floor, Property.ScaleY, t)),
            Math.Clamp(Sample(floor, Property.Opacity, t), 0, 1),
            color,
            style.Style,
            style.GlowIntensity);
    }

    /// <summary>
    /// songTimeMs 부터 이 시각 전까지는 <see cref="Evaluate"/> 결과가 변하지 않는다.
    /// 지금 애니메이션 중이면 songTimeMs 를, 앞으로 바뀔 일이 없으면 +∞ 를 돌려준다.
    /// 렌더러가 그동안 계산을 건너뛰는 데 쓴다 (시각이 뒤로 가면 다시 물어봐야 한다).
    /// </summary>
    public double StableUntil(int floor, double songTimeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(floor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(floor, _tileCount);
        var t = songTimeMs;
        var until = double.PositiveInfinity;

        for (var p = 0; p < PropertyCount; p++)
        {
            var slot = floor * PropertyCount + p;
            int from = _starts[slot], to = _starts[slot + 1];
            var k = LastStarted(_segments, from, to, t, static s => s.Start);
            if (k >= from && t < _segments[k].End)
                return t;
            if (k + 1 < to)
                until = Math.Min(until, _segments[k + 1].Start);
        }

        int first = _recolorStarts[floor], last = _recolorStarts[floor + 1];
        var r = LastStarted(_recolors, first, last, t, static s => s.Start);
        var active = r < first ? _baseColors[floor] : _recolors[r].State;
        if (active.Color.IsAnimated)
            return t;
        if (r >= first && t < _recolors[r].Start + _recolors[r].Duration)
            return t;
        if (r + 1 < last)
            until = Math.Min(until, _recolors[r + 1].Start);

        return until;
    }

    private double Sample(int floor, Property property, double t)
    {
        var slot = floor * PropertyCount + (int)property;
        var from = _starts[slot];
        var k = LastStarted(_segments, from, _starts[slot + 1], t, static s => s.Start);
        if (k < from)
            return _initial[slot];
        var s = _segments[k];
        var p = s.Duration <= 0 ? 1 : Math.Clamp((t - s.Start) / s.Duration, 0, 1);
        return s.From + (s.To - s.From) * Easing.Evaluate(s.Ease, p);
    }

    /// <summary>곡 기준 초. 색 주기(trackColorAnimDuration)는 피치가 걸리기 전 곡 시간으로 잰다.</summary>
    private double SongSeconds(double t) => t / 1000 * _pitch / 100;

    private (Rgba Color, TrackColor Style) ColorAt(int floor, double t)
    {
        var first = _recolorStarts[floor];
        var k = LastStarted(_recolors, first, _recolorStarts[floor + 1], t, static s => s.Start);
        var state = k < first ? _baseColors[floor] : _recolors[k].State;
        var color = state.Color.Evaluate(floor, state.ColorStart, SongSeconds(t));
        if (k >= first)
        {
            var segment = _recolors[k];
            if (segment.Duration > 0 && t < segment.Start + segment.Duration)
                color = Rgba.Lerp(segment.From, color, Easing.Evaluate(segment.Ease, (t - segment.Start) / segment.Duration));
        }
        return (color, state.Color);
    }

    /// <summary>[from, to) 안에서 시작 시각이 t 이하인 마지막 원소. 없으면 from - 1.</summary>
    private static int LastStarted<T>(T[] items, int from, int to, double t, Func<T, double> start)
    {
        int lo = from, hi = to;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (start(items[mid]) <= t)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo - 1;
    }

    // ---------- 만들기 ----------

    /// <summary>
    /// 속성 하나를 움직이는 트윈. From 이 null 이면 그 시점 값(앞 트윈의 끝값)에서 출발한다.
    /// Relative 면 To 는 출발값에 더하는 양이다.
    /// </summary>
    private readonly record struct Tween(int Slot, double Start, int Order, double Duration, double? From, double To, bool Relative, Ease Ease);

    /// <summary>
    /// 컴파일된 타일과 레벨 이벤트로 타임라인을 만든다.
    /// shapes 는 PositionTrack 이 정한 타일별 처음 모양 (회전, 크기, 투명도).
    /// </summary>
    internal static TrackTimeline Build(Level level, Tile[] tiles, (double Rotation, double Scale, double Opacity)[] shapes)
    {
        var settings = level.Settings;
        var pitch = settings.Pitch;
        var lastFloor = tiles.Length - 1;

        var initial = new double[tiles.Length * PropertyCount];
        for (var i = 0; i < tiles.Length; i++)
        {
            var b = i * PropertyCount;
            initial[b + (int)Property.Rotation] = shapes[i].Rotation;
            initial[b + (int)Property.ScaleX] = shapes[i].Scale;
            initial[b + (int)Property.ScaleY] = shapes[i].Scale;
            initial[b + (int)Property.Opacity] = shapes[i].Opacity;
        }

        var tweens = new List<Tween>();
        var order = 0;
        var baseColors = BuildTileSettings(level, tiles, initial, tweens, ref order);
        AddMoveTracks(level, tiles, lastFloor, pitch, tweens, ref order);
        var (segments, starts) = Resolve(tweens, initial, tiles.Length);
        var (recolors, recolorStarts) = BuildRecolors(level, tiles, lastFloor, pitch, baseColors);

        return new TrackTimeline(
            tiles.Length, pitch, initial, segments, starts, baseColors, recolors, recolorStarts, settings.StickToFloors);
    }

    private static double MsPerBeat(double bpm, double pitch) => 60000.0 / (bpm * pitch / 100.0);

    /// <summary>floor 번 타일 도착 후 angleOffset(도)만큼 지난 시각 (ffxPlusBase.SetStartTime).</summary>
    private static double TriggerMs(Tile[] tiles, int floor, double angleOffset, double pitch) =>
        tiles[floor].TimeMs + angleOffset / 180.0 * MsPerBeat(tiles[floor].Bpm, pitch);

    /// <summary>범위 [start, end] 안의 타일 (순서 무관, 범위 밖은 잘라냄). gapLength 만큼씩 건너뛴다.</summary>
    private static IEnumerable<int> TilesInRange(LevelEvent e, TileReference start, TileReference end, int gapLength, int lastFloor)
    {
        var (a, b) = Range(e, start, end, lastFloor);
        for (var i = a; i <= b; i += Math.Max(gapLength, 0) + 1)
            yield return i;
    }

    private static (int Start, int End) Range(LevelEvent e, TileReference start, TileReference end, int lastFloor)
    {
        var a = Math.Clamp(start.Resolve(e.Floor, lastFloor), 0, lastFloor);
        var b = Math.Clamp(end.Resolve(e.Floor, lastFloor), 0, lastFloor);
        return a > b ? (b, a) : (a, b);
    }

    /// <summary>
    /// 타일에 붙는 설정(ColorTrack, AnimateTrack)을 타일마다 정하고 등장/퇴장 트윈을 만든다 (얼불춤 scnGame 의 타일 준비 루프).
    /// </summary>
    private static ColorState[] BuildTileSettings(Level level, Tile[] tiles, double[] initial, List<Tween> tweens, ref int order)
    {
        var settings = level.Settings;
        var pitch = settings.Pitch;
        var lastFloor = tiles.Length - 1;
        var eventsByFloor = level.Events.ToLookup(e => e.Floor);
        var unitsPerTile = Chart.UnitsPerTileOf(settings.TileShape);

        // 곡 시작(카운트다운 시작) 시각. 이보다 이른 등장은 이 시각으로 당겨진다
        var levelStart = tiles[0].TimeMs - (settings.CountdownTicks - 1) * MsPerBeat(settings.Bpm, pitch);

        var colors = new ColorState[tiles.Length];
        var color = new ColorState(settings.TrackColor, 0);
        var appear = settings.TrackAnimation;
        var disappear = settings.TrackDisappearAnimation;
        double beatsAhead = settings.BeatsAhead, beatsBehind = settings.BeatsBehind;
        // "몇 박"은 AnimateTrack 이 걸린 타일의 BPM 으로 잰다. 등장이 꺼진 AnimateTrack 뒤에는 그 앞 AnimateTrack 의 BPM 을 쓴다
        // (얼불춤 scnGame 의 num5/num6/flag2 를 그대로 따른다)
        double referenceBpm = settings.Bpm, previousReferenceBpm = settings.Bpm;
        var appearChanged = true;

        for (var floor = 0; floor < tiles.Length; floor++)
        {
            var tileColor = color;
            foreach (var e in eventsByFloor[floor])
            {
                switch (e)
                {
                    case ColorTrackEvent c:
                        tileColor = new ColorState(c.Color.ApplyTo(tileColor.Color), c.Color.Type is null ? tileColor.ColorStart : floor);
                        if (!c.JustThisTile)
                            color = tileColor;
                        break;
                    case AnimateTrackEvent a:
                        appearChanged = a.Appear is not null;
                        if (a.Appear is { } newAppear)
                        {
                            appear = newAppear;
                            beatsAhead = a.BeatsAhead ?? 3;
                        }
                        if (a.Disappear is { } newDisappear)
                        {
                            disappear = newDisappear;
                            beatsBehind = a.BeatsBehind ?? 4;
                        }
                        previousReferenceBpm = referenceBpm;
                        referenceBpm = tiles[floor].Bpm;
                        break;
                }
            }
            colors[floor] = tileColor;

            var beatMs = MsPerBeat(appearChanged ? referenceBpm : previousReferenceBpm, pitch);
            AddAppear(tiles, floor, appear, beatsAhead * beatMs, levelStart, pitch, unitsPerTile, initial, tweens, ref order);
            if (floor < lastFloor)
                AddDisappear(tiles, floor, disappear, tiles[floor + 1].TimeMs + beatsBehind * beatMs, pitch, unitsPerTile, tweens, ref order);
        }
        return colors;
    }

    /// <summary>등장/퇴장 애니메이션 길이: 이 타일의 반 박, 최대 0.5초.</summary>
    private static double AnimationMs(Tile tile, double pitch) => Math.Min(0.5 * MsPerBeat(tile.Bpm, pitch), 500);

    /// <summary>
    /// ffxFloorAppearPlus. 타일 도착 aheadMs 전(Drop/Rise 는 두 배 전)에 시작한다.
    /// 시작 전 모습(FloorSetup)은 처음 값으로 넣는다.
    /// </summary>
    private static void AddAppear(
        Tile[] tiles, int floor, TrackAppearAnimation animation, double aheadMs, double levelStart, double pitch,
        double unitsPerTile, double[] initial, List<Tween> tweens, ref int order)
    {
        if (animation == TrackAppearAnimation.None)
            return;

        var dropOrRise = animation is TrackAppearAnimation.Drop or TrackAppearAnimation.Rise;
        var start = Math.Max(tiles[floor].TimeMs - aheadMs * (dropOrRise ? 2 : 1), levelStart);
        var duration = dropOrRise ? aheadMs : AnimationMs(tiles[floor], pitch);
        var b = floor * PropertyCount;
        var o = order++;

        void Add(Property p, double to, Ease ease, double? from = null, double? length = null) =>
            tweens.Add(new Tween(b + (int)p, start, o, length ?? duration, from, to, false, ease));
        void Initial(Property p, double value) => initial[b + (int)p] = value;

        // 시작 전 모습
        switch (animation)
        {
            case TrackAppearAnimation.Extend or TrackAppearAnimation.Grow:
                Initial(Property.ScaleX, 0);
                Initial(Property.ScaleY, 0);
                break;
            case TrackAppearAnimation.Grow_Spin:
                Initial(Property.ScaleX, 0);
                Initial(Property.ScaleY, 0);
                Initial(Property.Rotation, -180);
                break;
            case TrackAppearAnimation.Assemble or TrackAppearAnimation.Assemble_Far:
            {
                var range = (animation == TrackAppearAnimation.Assemble ? 4 : 8) / unitsPerTile;
                Initial(Property.X, Random(floor, 1) * range);
                Initial(Property.Y, Random(floor, 2) * range);
                Initial(Property.Rotation, Random(floor, 3) * 75);
                break;
            }
            case TrackAppearAnimation.Fade:
                Initial(Property.Opacity, 0);
                break;
            case TrackAppearAnimation.Drop or TrackAppearAnimation.Rise:
                Initial(Property.Y, (animation == TrackAppearAnimation.Drop ? 8 : -8) / unitsPerTile);
                Initial(Property.ScaleX, 0);
                Initial(Property.ScaleY, 0);
                break;
        }

        // 위치는 모든 종류가 원래 자리로 돌아온다
        var moveEase = dropOrRise ? Ease.Linear : Ease.OutSine;
        Vec2? from = null;
        if (animation == TrackAppearAnimation.Extend && floor > 0)
            from = tiles[floor - 1].Position - tiles[floor].Position; // 앞 타일 자리에서 뻗어 나온다
        Add(Property.X, 0, moveEase, from?.X);
        Add(Property.Y, 0, moveEase, from?.Y);

        // 얼불춤은 DOScale 에 이징을 따로 주지 않은 곳이 있다. DOTween 기본 이징(OutQuad)으로 본다
        switch (animation)
        {
            case TrackAppearAnimation.Extend:
                Add(Property.Rotation, 0, Ease.Linear, 0, 0);
                Add(Property.ScaleX, 1, Ease.OutSine);
                Add(Property.ScaleY, 1, Ease.OutSine);
                break;
            case TrackAppearAnimation.Assemble or TrackAppearAnimation.Assemble_Far:
                Add(Property.Rotation, 0, Ease.OutSine);
                break;
            case TrackAppearAnimation.Grow:
                Add(Property.ScaleX, 1, Ease.OutQuad);
                Add(Property.ScaleY, 1, Ease.OutQuad);
                break;
            case TrackAppearAnimation.Grow_Spin:
                Add(Property.ScaleX, 1, Ease.OutQuad);
                Add(Property.ScaleY, 1, Ease.OutQuad);
                Add(Property.Rotation, 0, Ease.OutSine);
                break;
            case TrackAppearAnimation.Fade:
                Add(Property.Opacity, 1, Ease.Linear);
                break;
            case TrackAppearAnimation.Drop or TrackAppearAnimation.Rise:
                Add(Property.ScaleX, 1, Ease.OutQuad, length: duration / 8);
                Add(Property.ScaleY, 1, Ease.OutQuad, length: duration / 8);
                break;
        }
    }

    /// <summary>ffxFloorDisappearPlus. 다음 타일 도착 뒤 behindMs 가 지나 시작한다. 마지막 타일은 사라지지 않는다.</summary>
    private static void AddDisappear(
        Tile[] tiles, int floor, TrackDisappearAnimation animation, double start, double pitch, double unitsPerTile,
        List<Tween> tweens, ref int order)
    {
        if (animation == TrackDisappearAnimation.None)
            return;

        var duration = AnimationMs(tiles[floor], pitch);
        var b = floor * PropertyCount;
        var o = order++;
        void Add(Property p, double to, Ease ease, bool relative = false) =>
            tweens.Add(new Tween(b + (int)p, start, o, duration, null, to, relative, ease));

        switch (animation)
        {
            case TrackDisappearAnimation.Retract:
            {
                // 다음 타일 자리로 빨려 들어가며 작아진다
                var next = tiles[floor + 1].Position - tiles[floor].Position;
                Add(Property.ScaleX, 0, Ease.OutSine);
                Add(Property.ScaleY, 0, Ease.OutSine);
                Add(Property.X, next.X, Ease.OutSine);
                Add(Property.Y, next.Y, Ease.OutSine);
                break;
            }
            case TrackDisappearAnimation.Scatter or TrackDisappearAnimation.Scatter_Far:
            {
                // 흩어지기만 하고 사라지지는 않는다 (얼불춤도 크기, 투명도는 그대로)
                var range = (animation == TrackDisappearAnimation.Scatter ? 4 : 8) / unitsPerTile;
                Add(Property.X, Random(floor, 4) * range, Ease.OutSine, relative: true);
                Add(Property.Y, Random(floor, 5) * range, Ease.OutSine, relative: true);
                Add(Property.Rotation, Random(floor, 6) * 75, Ease.OutSine);
                break;
            }
            case TrackDisappearAnimation.Shrink:
                Add(Property.ScaleX, 0, Ease.OutQuad);
                Add(Property.ScaleY, 0, Ease.OutQuad);
                break;
            case TrackDisappearAnimation.Shrink_Spin:
                Add(Property.ScaleX, 0, Ease.OutQuad);
                Add(Property.ScaleY, 0, Ease.OutQuad);
                Add(Property.Rotation, -180, Ease.OutSine);
                break;
            case TrackDisappearAnimation.Fade:
                Add(Property.Opacity, 0, Ease.Linear);
                break;
        }
    }

    /// <summary>ffxMoveFloorPlus. 값은 원래 상태 기준의 목표값이고, null 인 속성은 건드리지 않는다.</summary>
    private static void AddMoveTracks(Level level, Tile[] tiles, int lastFloor, double pitch, List<Tween> tweens, ref int order)
    {
        foreach (var e in level.Events)
        {
            if (e is not MoveTrackEvent m || m.Floor > lastFloor)
                continue;

            var start = TriggerMs(tiles, m.Floor, m.AngleOffset, pitch);
            var duration = m.DurationBeats * MsPerBeat(tiles[m.Floor].Bpm, pitch);
            ReadOnlySpan<double?> targets = [m.PositionX, m.PositionY, m.Rotation, m.ScaleX, m.ScaleY, m.Opacity];
            var o = order++;
            foreach (var tile in TilesInRange(m, m.StartTile, m.EndTile, m.GapLength, lastFloor))
            {
                for (var p = 0; p < PropertyCount; p++)
                {
                    if (targets[p] is { } to)
                        tweens.Add(new Tween(tile * PropertyCount + p, start, o, duration, null, to, false, m.Ease));
                }
            }
        }
    }

    /// <summary>트윈을 속성별로 시간순 정렬하고, 앞 트윈의 끝값에서 출발하도록 시작값을 채운다.</summary>
    private static (Segment[], int[]) Resolve(List<Tween> tweens, double[] initial, int tileCount)
    {
        tweens.Sort((x, y) => x.Slot != y.Slot ? x.Slot.CompareTo(y.Slot)
            : x.Start != y.Start ? x.Start.CompareTo(y.Start)
            : x.Order.CompareTo(y.Order));

        var segments = new Segment[tweens.Count];
        var starts = new int[tileCount * PropertyCount + 1];
        var current = 0.0;
        for (var i = 0; i < tweens.Count; i++)
        {
            var tw = tweens[i];
            if (i == 0 || tweens[i - 1].Slot != tw.Slot)
                current = initial[tw.Slot];
            var from = tw.From ?? current;
            var to = tw.Relative ? from + tw.To : tw.To;
            segments[i] = new Segment(tw.Start, tw.Duration, from, to, tw.Ease);
            current = to;
            starts[tw.Slot + 1]++;
        }
        for (var s = 1; s < starts.Length; s++)
            starts[s] += starts[s - 1];
        return (segments, starts);
    }

    /// <summary>ffxRecolorFloorPlus. 이벤트에 없는 값은 그 시점 타일의 값을 그대로 쓴다.</summary>
    private static (ColorSegment[], int[]) BuildRecolors(Level level, Tile[] tiles, int lastFloor, double pitch, ColorState[] baseColors)
    {
        var raw = new List<(int Tile, double Start, int Order, RecolorTrackEvent Event, int RangeStart, double Duration)>();
        var order = 0;
        foreach (var e in level.Events)
        {
            if (e is not RecolorTrackEvent r || r.Floor > lastFloor)
                continue;

            var start = TriggerMs(tiles, r.Floor, r.AngleOffset, pitch);
            var duration = r.DurationBeats * MsPerBeat(tiles[r.Floor].Bpm, pitch);
            var rangeStart = Range(r, r.StartTile, r.EndTile, lastFloor).Start;
            foreach (var tile in TilesInRange(r, r.StartTile, r.EndTile, r.GapLength, lastFloor))
                raw.Add((tile, start, order, r, rangeStart, duration));
            order++;
        }
        raw.Sort((x, y) => x.Tile != y.Tile ? x.Tile.CompareTo(y.Tile)
            : x.Start != y.Start ? x.Start.CompareTo(y.Start)
            : x.Order.CompareTo(y.Order));

        var segments = new ColorSegment[raw.Count];
        var starts = new int[tiles.Length + 1];
        var state = default(ColorState);
        for (var i = 0; i < raw.Count; i++)
        {
            var (tile, start, _, r, rangeStart, duration) = raw[i];
            if (i == 0 || raw[i - 1].Tile != tile)
                state = baseColors[tile];

            // 앞 색 변화는 바로 완료되고, 그 색에서 섞이기 시작한다
            var from = state.Color.Evaluate(tile, state.ColorStart, start / 1000 * pitch / 100);
            state = new ColorState(r.Color.ApplyTo(state.Color), r.Color.Type is null ? state.ColorStart : rangeStart);
            // 저절로 바뀌는 색은 섞지 않고 바로 바뀐다 (얼불춤 ColorFloor 는 Single/Stripes 만 TweenColor)
            segments[i] = new ColorSegment(start, state.Color.IsAnimated ? 0 : duration, r.Ease, state, from);
            starts[tile + 1]++;
        }
        for (var s = 1; s < starts.Length; s++)
            starts[s] += starts[s - 1];
        return (segments, starts);
    }

    /// <summary>
    /// 타일마다 고정된 -1~1 난수. 얼불춤은 매번 UnityEngine.Random 을 쓰므로 값 자체는 맞출 수 없고 범위만 같다.
    /// </summary>
    private static double Random(int floor, int salt)
    {
        var h = (uint)floor * 2654435761u ^ (uint)salt * 0x9E3779B9u;
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return h / (double)uint.MaxValue * 2 - 1;
    }
}
