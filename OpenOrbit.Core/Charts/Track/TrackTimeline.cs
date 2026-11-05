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
/// MoveTrack/RecolorTrack 은 타일마다 "구간" 목록으로 펼쳐 둔다. 구간의 시작값은 그 시점에 이전 구간들이 만든 값이라서,
/// 앞 트윈이 끝나기 전에 다음 트윈이 시작되면 그 자리에서 이어받는다 (얼불춤에서 나중에 시작한 트윈이 이기는 것과 같다).
/// </para>
/// </summary>
public sealed class TrackTimeline
{
    /// <summary>등장/퇴장 애니메이션 길이(박). 얼불춤 실제 길이를 확인하지 못해 1박으로 둔 근사값이다.</summary>
    public const double AnimationBeats = 1;

    private enum Property { X, Y, Rotation, ScaleX, ScaleY, Opacity }
    private const int PropertyCount = 6;
    private static readonly double[] Defaults = [0, 0, 0, 1, 1, 1];

    private readonly record struct MoveSegment(double Start, double Duration, double From, double To, Ease Ease);
    private readonly record struct ColorSegment(double Start, double Duration, Ease Ease, TrackColor Color);
    private readonly record struct Animation(
        TrackAppearAnimation Appear, double AppearStart, double AppearEnd,
        TrackDisappearAnimation Disappear, double DisappearStart, double DisappearEnd);
    private readonly record struct StaticShape(double Rotation, double Scale, double Opacity);

    private readonly int _tileCount;

    // 타일 i, 속성 p 의 구간은 _moves[_moveStarts[i * 6 + p] .. _moveStarts[i * 6 + p + 1]) 이고 시작 시각 순이다
    private readonly MoveSegment[] _moves;
    private readonly int[] _moveStarts;

    private readonly TrackColor[] _baseColors;
    private readonly ColorSegment[] _recolors;
    private readonly int[] _recolorStarts;

    private readonly Animation[] _animations;
    private readonly StaticShape[] _shapes;

    public bool StickToFloors { get; }

    private TrackTimeline(
        int tileCount, MoveSegment[] moves, int[] moveStarts, TrackColor[] baseColors,
        ColorSegment[] recolors, int[] recolorStarts, Animation[] animations, StaticShape[] shapes, bool stickToFloors)
    {
        _tileCount = tileCount;
        _moves = moves;
        _moveStarts = moveStarts;
        _baseColors = baseColors;
        _recolors = recolors;
        _recolorStarts = recolorStarts;
        _animations = animations;
        _shapes = shapes;
        StickToFloors = stickToFloors;
    }

    /// <summary>floor 번 타일의 songTimeMs 시점 모습.</summary>
    public TileVisual Evaluate(int floor, double songTimeMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(floor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(floor, _tileCount);

        var shape = _shapes[floor];
        var animation = _animations[floor];
        var appear = TrackAnimations.Appear(animation.Appear, Progress(songTimeMs, animation.AppearStart, animation.AppearEnd), floor);
        var disappear = TrackAnimations.Disappear(animation.Disappear, Progress(songTimeMs, animation.DisappearStart, animation.DisappearEnd), floor);

        var offset = new Vec2(Sample(floor, Property.X, songTimeMs), Sample(floor, Property.Y, songTimeMs))
                     + appear.Offset + disappear.Offset;
        var rotation = shape.Rotation + Sample(floor, Property.Rotation, songTimeMs) + appear.Rotation + disappear.Rotation;
        var scale = shape.Scale * appear.Scale * disappear.Scale;
        var opacity = shape.Opacity * Sample(floor, Property.Opacity, songTimeMs) * appear.Opacity * disappear.Opacity;

        var (color, style) = ColorAt(floor, songTimeMs);
        return new TileVisual(
            offset,
            rotation,
            new Vec2(Sample(floor, Property.ScaleX, songTimeMs) * scale, Sample(floor, Property.ScaleY, songTimeMs) * scale),
            Math.Clamp(opacity, 0, 1),
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

        // 등장/퇴장
        var a = _animations[floor];
        if (a.Appear != TrackAppearAnimation.None)
        {
            if (t < a.AppearStart) until = Math.Min(until, a.AppearStart);
            else if (t < a.AppearEnd) return t;
        }
        if (a.Disappear != TrackDisappearAnimation.None)
        {
            if (t < a.DisappearStart) until = Math.Min(until, a.DisappearStart);
            else if (t < a.DisappearEnd) return t;
        }

        // MoveTrack
        for (var p = 0; p < PropertyCount; p++)
        {
            var slot = floor * PropertyCount + p;
            int from = _moveStarts[slot], to = _moveStarts[slot + 1];
            var k = LastStarted(_moves, from, to, t, static s => s.Start);
            if (k >= from && t < _moves[k].Start + _moves[k].Duration)
                return t;
            if (k + 1 < to)
                until = Math.Min(until, _moves[k + 1].Start);
        }

        // 색
        int first = _recolorStarts[floor], last = _recolorStarts[floor + 1];
        var r = LastStarted(_recolors, first, last, t, static s => s.Start);
        var active = r < first ? _baseColors[floor] : _recolors[r].Color;
        if (active.Type is not (TrackColorType.Single or TrackColorType.Stripes or TrackColorType.Volume))
            return t;
        if (r >= first && t < _recolors[r].Start + _recolors[r].Duration)
            return t;
        if (r + 1 < last)
            until = Math.Min(until, _recolors[r + 1].Start);

        return until;
    }

    private static double Progress(double t, double start, double end) =>
        end <= start ? (t >= start ? 1 : 0) : Math.Clamp((t - start) / (end - start), 0, 1);

    private double Sample(int floor, Property property, double t)
    {
        var slot = floor * PropertyCount + (int)property;
        var from = _moveStarts[slot];
        var k = LastStarted(_moves, from, _moveStarts[slot + 1], t, static s => s.Start);
        if (k < from)
            return Defaults[(int)property];
        return ValueAt(_moves[k], t);
    }

    private static double ValueAt(in MoveSegment s, double t)
    {
        var p = s.Duration <= 0 ? 1 : Math.Clamp((t - s.Start) / s.Duration, 0, 1);
        return s.From + (s.To - s.From) * Easing.Evaluate(s.Ease, p);
    }

    private (Rgba Color, TrackColor Style) ColorAt(int floor, double t)
    {
        var from = _recolorStarts[floor];
        var k = LastStarted(_recolors, from, _recolorStarts[floor + 1], t, static s => s.Start);
        return ColorAt(floor, t, from, k);
    }

    /// <summary>k 번째 구간까지 적용된 색. 섞이는 중이면 바로 앞 구간의 색과 섞는다.</summary>
    private (Rgba Color, TrackColor Style) ColorAt(int floor, double t, int first, int k)
    {
        var timeSec = t / 1000;
        if (k < first)
        {
            var baseColor = _baseColors[floor];
            return (baseColor.Evaluate(floor, timeSec), baseColor);
        }

        var segment = _recolors[k];
        var target = segment.Color.Evaluate(floor, timeSec);
        var p = segment.Duration <= 0 ? 1 : Math.Clamp((t - segment.Start) / segment.Duration, 0, 1);
        if (p >= 1)
            return (target, segment.Color);

        var (previous, _) = ColorAt(floor, t, first, k - 1);
        // 스타일은 섞을 수 없으므로 시작하자마자 바뀐다
        return (Rgba.Lerp(previous, target, Easing.Evaluate(segment.Ease, p)), segment.Color);
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
    /// 컴파일된 타일과 레벨 이벤트로 타임라인을 만든다.
    /// shapes 는 PositionTrack 이 정한 타일별 고정 모양 (회전, 크기, 투명도).
    /// </summary>
    internal static TrackTimeline Build(Level level, Tile[] tiles, (double Rotation, double Scale, double Opacity)[] shapes)
    {
        var settings = level.Settings;
        var lastFloor = tiles.Length - 1;
        var eventsByFloor = level.Events.ToLookup(e => e.Floor);

        // 타일에 붙는 설정 (ColorTrack, AnimateTrack): 이벤트가 걸린 타일부터 적용된다
        var baseColors = new TrackColor[tiles.Length];
        var appear = new (TrackAppearAnimation Anim, double Beats)[tiles.Length];
        var disappear = new (TrackDisappearAnimation Anim, double Beats)[tiles.Length];
        var color = settings.TrackColor;
        (TrackAppearAnimation, double) currentAppear = (settings.TrackAnimation, settings.BeatsAhead);
        (TrackDisappearAnimation, double) currentDisappear = (settings.TrackDisappearAnimation, settings.BeatsBehind);
        for (var floor = 0; floor < tiles.Length; floor++)
        {
            TrackColor? onlyThis = null;
            foreach (var e in eventsByFloor[floor])
            {
                switch (e)
                {
                    case ColorTrackEvent c when c.JustThisTile:
                        onlyThis = c.Color;
                        break;
                    case ColorTrackEvent c:
                        color = c.Color;
                        break;
                    case AnimateTrackEvent a:
                        currentAppear = (a.Appear ?? currentAppear.Item1, a.BeatsAhead ?? currentAppear.Item2);
                        currentDisappear = (a.Disappear ?? currentDisappear.Item1, a.BeatsBehind ?? currentDisappear.Item2);
                        break;
                }
            }
            baseColors[floor] = onlyThis ?? color;
            appear[floor] = currentAppear;
            disappear[floor] = currentDisappear;
        }

        var (moves, moveStarts) = BuildMoves(level, tiles, lastFloor, settings.Pitch);
        var (recolors, recolorStarts) = BuildRecolors(level, tiles, lastFloor, settings.Pitch);
        var animations = BuildAnimations(tiles, appear, disappear, settings.Pitch);

        return new TrackTimeline(
            tiles.Length, moves, moveStarts, baseColors, recolors, recolorStarts, animations,
            shapes.Select(s => new StaticShape(s.Rotation, s.Scale, s.Opacity)).ToArray(),
            settings.StickToFloors);
    }

    private static double MsPerBeat(double bpm, double pitch) => 60000.0 / (bpm * pitch / 100.0);

    /// <summary>floor 번 타일 도착 후 angleOffset(도)만큼 지난 시각.</summary>
    private static double TriggerMs(Tile[] tiles, int floor, double angleOffset, double pitch) =>
        tiles[floor].TimeMs + angleOffset / 180.0 * MsPerBeat(tiles[floor].Bpm, pitch);

    /// <summary>범위 [start, end] 안의 타일 (순서 무관, 범위 밖은 잘라냄). gapLength 만큼씩 건너뛴다.</summary>
    private static IEnumerable<int> TilesInRange(LevelEvent e, TileReference start, TileReference end, int gapLength, int lastFloor)
    {
        var a = Math.Clamp(start.Resolve(e.Floor, lastFloor), 0, lastFloor);
        var b = Math.Clamp(end.Resolve(e.Floor, lastFloor), 0, lastFloor);
        if (a > b)
            (a, b) = (b, a);
        for (var i = a; i <= b; i += Math.Max(gapLength, 0) + 1)
            yield return i;
    }

    private static (MoveSegment[], int[]) BuildMoves(Level level, Tile[] tiles, int lastFloor, double pitch)
    {
        // (타일, 속성, 시작, 이벤트 순서, 길이, 목표, 이징)을 모았다가 정렬한 뒤 시작값을 채운다
        var raw = new List<(int Slot, double Start, int Order, double Duration, double To, Ease Ease)>();
        var order = 0;
        foreach (var e in level.Events)
        {
            if (e is not MoveTrackEvent m || m.Floor > lastFloor)
                continue;

            var start = TriggerMs(tiles, m.Floor, m.AngleOffset, pitch);
            var duration = m.DurationBeats * MsPerBeat(tiles[m.Floor].Bpm, pitch);
            ReadOnlySpan<double?> targets = [m.PositionX, m.PositionY, m.Rotation, m.ScaleX, m.ScaleY, m.Opacity];
            foreach (var tile in TilesInRange(m, m.StartTile, m.EndTile, m.GapLength, lastFloor))
            {
                for (var p = 0; p < PropertyCount; p++)
                {
                    if (targets[p] is { } to)
                        raw.Add((tile * PropertyCount + p, start, order, duration, to, m.Ease));
                }
            }
            order++;
        }
        raw.Sort((x, y) => x.Slot != y.Slot ? x.Slot.CompareTo(y.Slot)
            : x.Start != y.Start ? x.Start.CompareTo(y.Start)
            : x.Order.CompareTo(y.Order));

        var segments = new MoveSegment[raw.Count];
        var starts = new int[tiles.Length * PropertyCount + 1];
        for (var i = 0; i < raw.Count; i++)
        {
            var r = raw[i];
            var from = i > 0 && raw[i - 1].Slot == r.Slot
                ? ValueAt(segments[i - 1], r.Start)
                : Defaults[r.Slot % PropertyCount];
            segments[i] = new MoveSegment(r.Start, r.Duration, from, r.To, r.Ease);
            starts[r.Slot + 1]++;
        }
        for (var s = 1; s < starts.Length; s++)
            starts[s] += starts[s - 1];
        return (segments, starts);
    }

    private static (ColorSegment[], int[]) BuildRecolors(Level level, Tile[] tiles, int lastFloor, double pitch)
    {
        var raw = new List<(int Tile, double Start, int Order, ColorSegment Segment)>();
        var order = 0;
        foreach (var e in level.Events)
        {
            if (e is not RecolorTrackEvent r || r.Floor > lastFloor)
                continue;

            var start = TriggerMs(tiles, r.Floor, r.AngleOffset, pitch);
            var segment = new ColorSegment(start, r.DurationBeats * MsPerBeat(tiles[r.Floor].Bpm, pitch), r.Ease, r.Color);
            foreach (var tile in TilesInRange(r, r.StartTile, r.EndTile, r.GapLength, lastFloor))
                raw.Add((tile, start, order, segment));
            order++;
        }
        raw.Sort((x, y) => x.Tile != y.Tile ? x.Tile.CompareTo(y.Tile)
            : x.Start != y.Start ? x.Start.CompareTo(y.Start)
            : x.Order.CompareTo(y.Order));

        var segments = new ColorSegment[raw.Count];
        var starts = new int[tiles.Length + 1];
        for (var i = 0; i < raw.Count; i++)
        {
            segments[i] = raw[i].Segment;
            starts[raw[i].Tile + 1]++;
        }
        for (var s = 1; s < starts.Length; s++)
            starts[s] += starts[s - 1];
        return (segments, starts);
    }

    /// <summary>
    /// 등장/퇴장 시각. "몇 박"은 행성이 실제로 도는 각도(1박 = 180°)로 센다. 그래서 BPM 이 바뀌거나 Pause 가 있어도 맞는다.
    /// 등장은 타일 도착 BeatsAhead 박 전, 퇴장은 행성이 이 타일을 떠나고(다음 타일 도착) BeatsBehind 박 뒤에 시작한다.
    /// </summary>
    private static Animation[] BuildAnimations(
        Tile[] tiles, (TrackAppearAnimation Anim, double Beats)[] appear, (TrackDisappearAnimation Anim, double Beats)[] disappear, double pitch)
    {
        // 0번 타일부터 각 타일 도착까지 누적 회전각
        var angles = new double[tiles.Length];
        for (var i = 1; i < tiles.Length; i++)
            angles[i] = angles[i - 1] + tiles[i - 1].TravelAngle;

        double TimeAtAngle(double angle)
        {
            if (angle <= 0)
                return tiles[0].TimeMs + angle / 180.0 * MsPerBeat(tiles[0].Bpm, pitch);
            var last = tiles.Length - 1;
            if (angle >= angles[last])
                return tiles[last].TimeMs + (angle - angles[last]) / 180.0 * MsPerBeat(tiles[last].Bpm, pitch);

            var j = Array.BinarySearch(angles, angle);
            if (j < 0)
                j = ~j - 1;
            // 회전 없이 넘어가는 타일(미드스핀 직전)은 같은 각도가 여러 개라 뒤쪽으로 맞춘다
            while (j + 1 < tiles.Length && angles[j + 1] <= angle)
                j++;
            var t = tiles[j];
            return t.TravelAngle <= 0 ? t.TimeMs : t.TimeMs + (angle - angles[j]) / t.TravelAngle * t.TravelMs;
        }

        var result = new Animation[tiles.Length];
        const double animationDegrees = AnimationBeats * 180;
        for (var i = 0; i < tiles.Length; i++)
        {
            var appearAngle = angles[i] - appear[i].Beats * 180;
            var appearStart = TimeAtAngle(appearAngle);
            var appearEnd = Math.Max(appearStart, Math.Min(TimeAtAngle(appearAngle + animationDegrees), tiles[i].TimeMs));

            var leaveAngle = i + 1 < tiles.Length ? angles[i + 1] : angles[i];
            var disappearAngle = leaveAngle + disappear[i].Beats * 180;

            result[i] = new Animation(
                appear[i].Anim, appearStart, appearEnd,
                disappear[i].Anim, TimeAtAngle(disappearAngle), TimeAtAngle(disappearAngle + animationDegrees));
        }
        return result;
    }
}
