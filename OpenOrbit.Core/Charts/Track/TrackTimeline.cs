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
/// 트랙 장식(ColorTrack, RecolorTrack, MoveTrack, AnimateTrack, PositionTrack 의 모양)을 미리 정리해 두고
/// 타일 하나의 특정 시각 모습을 계산한다. 엔진과 무관한 순수 계산이며 만든 뒤에는 바뀌지 않으므로 아무 스레드에서나 불러도 된다.
/// <para>
/// 타일마다 채널이 여섯 개(위치 X/Y, 회전, 크기 X/Y, 투명도) 있고, 채널은 한 번에 움직임 하나만 갖는다.
/// 새 움직임이 시작되면 앞 움직임은 그 자리에서 끝값으로 완료되고, 새 움직임은 그 끝값에서 출발한다.
/// 등장/퇴장 애니메이션과 MoveTrack 이 모두 같은 채널을 움직인다. 색은 따로 색 채널 하나로 다룬다.
/// </para>
/// </summary>
public sealed class TrackTimeline
{
    private enum Channel { X, Y, Rotation, ScaleX, ScaleY, Opacity }
    private const int ChannelCount = 6;

    /// <summary>Start 부터 Length 동안 From → To 로 움직인다. 끝나면 다음 움직임 전까지 To 에 머문다.</summary>
    private readonly record struct Move(double Start, double Length, double From, double To, Ease Ease)
    {
        public double End => Start + Length;
    }

    /// <summary>타일의 트랙 색과 Stripes 의 기준 타일.</summary>
    private readonly record struct Paint(TrackColor Color, int StripeOrigin);

    /// <summary>Start 부터 Paint 로 바뀐다. Blend 동안은 From 색에서 섞인다 (0 이면 바로 바뀜).</summary>
    private readonly record struct Repaint(double Start, double Blend, Ease Ease, Paint Paint, Rgba From)
    {
        public double End => Start + Blend;
    }

    private readonly int _tileCount;
    private readonly double _pitch;

    // 채널 c (= 타일 × 6 + 속성) 의 처음 값은 _rest[c], 움직임은 _moves[_firstMove[c] .. _firstMove[c + 1]) (시작 시각 순)
    private readonly double[] _rest;
    private readonly Move[] _moves;
    private readonly int[] _firstMove;

    // 타일 i 의 처음 색은 _paints[i], 색 바꿈은 _repaints[_firstRepaint[i] .. _firstRepaint[i + 1])
    private readonly Paint[] _paints;
    private readonly Repaint[] _repaints;
    private readonly int[] _firstRepaint;

    public bool StickToFloors { get; }

    private TrackTimeline(int tileCount, double pitch, double[] rest, Move[] moves, int[] firstMove,
        Paint[] paints, Repaint[] repaints, int[] firstRepaint, bool stickToFloors)
    {
        _tileCount = tileCount;
        _pitch = pitch;
        _rest = rest;
        _moves = moves;
        _firstMove = firstMove;
        _paints = paints;
        _repaints = repaints;
        _firstRepaint = firstRepaint;
        StickToFloors = stickToFloors;
    }

    /// <summary>floor 번 타일의 songTimeMs 시점 모습.</summary>
    public TileVisual Evaluate(int floor, double songTimeMs)
    {
        CheckFloor(floor);
        var t = songTimeMs;
        var (color, paint) = ColorOf(floor, t);
        return new TileVisual(
            new Vec2(ValueOf(floor, Channel.X, t), ValueOf(floor, Channel.Y, t)),
            ValueOf(floor, Channel.Rotation, t),
            new Vec2(ValueOf(floor, Channel.ScaleX, t), ValueOf(floor, Channel.ScaleY, t)),
            Math.Clamp(ValueOf(floor, Channel.Opacity, t), 0, 1),
            color,
            paint.Color.Style,
            paint.Color.GlowIntensity);
    }

    /// <summary>
    /// songTimeMs 부터 이 시각 전까지는 <see cref="Evaluate"/> 결과가 변하지 않는다.
    /// 지금 움직이는 중이면 songTimeMs 를, 앞으로 바뀔 일이 없으면 +∞ 를 돌려준다.
    /// 렌더러가 그동안 계산을 건너뛰는 데 쓴다 (시각이 뒤로 가면 다시 물어봐야 한다).
    /// </summary>
    public double StableUntil(int floor, double songTimeMs)
    {
        CheckFloor(floor);
        var t = songTimeMs;
        var next = double.PositiveInfinity;

        for (var c = floor * ChannelCount; c < (floor + 1) * ChannelCount; c++)
        {
            int begin = _firstMove[c], end = _firstMove[c + 1];
            var k = Latest(_moves, begin, end, t, static m => m.Start);
            if (k >= begin && t < _moves[k].End)
                return t;
            if (k + 1 < end)
                next = Math.Min(next, _moves[k + 1].Start);
        }

        int first = _firstRepaint[floor], last = _firstRepaint[floor + 1];
        var r = Latest(_repaints, first, last, t, static p => p.Start);
        var paint = r < first ? _paints[floor] : _repaints[r].Paint;
        if (paint.Color.IsAnimated || (r >= first && t < _repaints[r].End))
            return t;
        if (r + 1 < last)
            next = Math.Min(next, _repaints[r + 1].Start);
        return next;
    }

    private void CheckFloor(int floor)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(floor);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(floor, _tileCount);
    }

    private double ValueOf(int floor, Channel channel, double t)
    {
        var c = floor * ChannelCount + (int)channel;
        var begin = _firstMove[c];
        var k = Latest(_moves, begin, _firstMove[c + 1], t, static m => m.Start);
        if (k < begin)
            return _rest[c];
        var m = _moves[k];
        var progress = m.Length > 0 ? Math.Clamp((t - m.Start) / m.Length, 0, 1) : 1;
        return m.From + (m.To - m.From) * Easing.Evaluate(m.Ease, progress);
    }

    private (Rgba Color, Paint Paint) ColorOf(int floor, double t)
    {
        var first = _firstRepaint[floor];
        var k = Latest(_repaints, first, _firstRepaint[floor + 1], t, static p => p.Start);
        var paint = k < first ? _paints[floor] : _repaints[k].Paint;
        var color = paint.Color.Evaluate(floor, paint.StripeOrigin, ColorClock(t, _pitch));
        if (k >= first && t < _repaints[k].End)
        {
            var r = _repaints[k];
            color = Rgba.Lerp(r.From, color, Easing.Evaluate(r.Ease, (t - r.Start) / r.Blend));
        }
        return (color, paint);
    }

    /// <summary>색 주기를 재는 시계(초). 피치를 반영하기 전 곡 시간이라 피치가 바뀌어도 주기가 박자에 맞는다.</summary>
    private static double ColorClock(double songTimeMs, double pitch) => songTimeMs / 1000 * pitch / 100;

    /// <summary>[begin, end) 에서 시작 시각이 t 이하인 마지막 원소의 번호. 없으면 begin - 1.</summary>
    private static int Latest<T>(T[] items, int begin, int end, double t, Func<T, double> start)
    {
        int lo = begin, hi = end;
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

    // ================= 만들기 =================

    /// <summary>
    /// 컴파일된 타일과 레벨 이벤트로 타임라인을 만든다.
    /// shapes 는 PositionTrack 이 정한 타일별 처음 모양 (회전, 크기, 투명도).
    /// </summary>
    internal static TrackTimeline Build(Level level, Tile[] tiles, (double Rotation, double Scale, double Opacity)[] shapes)
    {
        var context = new BuildContext(level, tiles);
        var rest = new double[tiles.Length * ChannelCount];
        for (var i = 0; i < tiles.Length; i++)
        {
            rest[i * ChannelCount + (int)Channel.Rotation] = shapes[i].Rotation;
            rest[i * ChannelCount + (int)Channel.ScaleX] = shapes[i].Scale;
            rest[i * ChannelCount + (int)Channel.ScaleY] = shapes[i].Scale;
            rest[i * ChannelCount + (int)Channel.Opacity] = shapes[i].Opacity;
        }

        var floors = FloorSettings.Scan(context);
        var changes = new List<Change>();
        var sequence = 0;
        for (var floor = 0; floor < tiles.Length; floor++)
        {
            var s = floors[floor];
            if (AppearRecipes.TryGetValue(s.Appear, out var appear))
                Schedule(context, floor, appear, s, rest, changes, sequence++);
            if (floor < context.LastFloor && DisappearRecipes.TryGetValue(s.Disappear, out var disappear))
                Schedule(context, floor, disappear, s, rest, changes, sequence++);
        }
        ScheduleMoveTracks(context, changes, ref sequence);

        var (moves, firstMove) = Chain(changes, rest);
        var paints = floors.Select(f => f.Paint).ToArray();
        var (repaints, firstRepaint) = ChainRepaints(context, paints);
        return new TrackTimeline(tiles.Length, context.Pitch, rest, moves, firstMove,
            paints, repaints, firstRepaint, level.Settings.StickToFloors);
    }

    /// <summary>만드는 동안 함께 쓰는 값들.</summary>
    private sealed class BuildContext(Level level, Tile[] tiles)
    {
        public Level Level { get; } = level;
        public Tile[] Tiles { get; } = tiles;
        public int LastFloor { get; } = tiles.Length - 1;
        public double Pitch { get; } = level.Settings.Pitch;
        public double UnitsPerTile { get; } = Chart.UnitsPerTileOf(level.Settings.TileShape);

        public double BeatMs(double bpm) => 60000.0 / (bpm * Pitch / 100.0);

        /// <summary>floor 번 타일에 도착하고 angleOffset(도, 180° = 한 박) 뒤의 시각.</summary>
        public double EventTime(int floor, double angleOffset) =>
            Tiles[floor].TimeMs + angleOffset / 180.0 * BeatMs(Tiles[floor].Bpm);

        /// <summary>startTile ~ endTile 범위 (양 끝 포함, 순서가 뒤집혀 있으면 바로잡고 타일 밖은 잘라낸다).</summary>
        public (int First, int Last) Span(LevelEvent e, TileReference start, TileReference end)
        {
            var a = Math.Clamp(start.Resolve(e.Floor, LastFloor), 0, LastFloor);
            var b = Math.Clamp(end.Resolve(e.Floor, LastFloor), 0, LastFloor);
            return (Math.Min(a, b), Math.Max(a, b));
        }

        /// <summary>범위 안에서 gapLength 개씩 건너뛴 타일들.</summary>
        public IEnumerable<int> Targets(LevelEvent e, TileReference start, TileReference end, int gapLength)
        {
            var (first, last) = Span(e, start, end);
            for (var i = first; i <= last; i += Math.Max(gapLength, 0) + 1)
                yield return i;
        }
    }

    // ---------- 타일별 설정 ----------

    /// <summary>
    /// 한 타일에 적용되는 설정: 트랙 색, 등장/퇴장 애니메이션과 그 시작 시각.
    /// </summary>
    /// <param name="AppearLeadMs">타일에 도착하기 이만큼 전에 등장한다.</param>
    /// <param name="DisappearDelayMs">다음 타일에 도착하고 이만큼 뒤에 퇴장한다.</param>
    private readonly record struct FloorSettings(
        Paint Paint,
        TrackAppearAnimation Appear,
        double AppearLeadMs,
        TrackDisappearAnimation Disappear,
        double DisappearDelayMs)
    {
        /// <summary>
        /// 타일을 처음부터 훑으며 ColorTrack 과 AnimateTrack 을 쌓는다.
        /// <list type="bullet">
        /// <item>ColorTrack 은 그 타일부터 계속 적용되고, justThisTile 이면 그 타일에만 적용된다.
        /// 색 종류를 바꾸면 그 타일이 Stripes 의 기준이 된다.</item>
        /// <item>AnimateTrack 은 바꾼 애니메이션만 바꾼다. 애니메이션을 주면 박 수도 함께 정하고, 박 수를 안 주면 등장 3박, 퇴장 4박.</item>
        /// <item>"몇 박"은 가장 최근 AnimateTrack 이 걸린 타일의 BPM 으로 잰다. 다만 그 AnimateTrack 이 등장 애니메이션을 주지 않았으면
        /// 그 앞 AnimateTrack(없으면 settings)의 BPM 으로 잰다.</item>
        /// </list>
        /// </summary>
        public static FloorSettings[] Scan(BuildContext context)
        {
            var settings = context.Level.Settings;
            var tiles = context.Tiles;
            var events = context.Level.Events.ToLookup(e => e.Floor);
            var result = new FloorSettings[tiles.Length];

            var paint = new Paint(settings.TrackColor, 0);
            var appear = settings.TrackAnimation;
            var disappear = settings.TrackDisappearAnimation;
            double beatsAhead = settings.BeatsAhead, beatsBehind = settings.BeatsBehind;
            double animateBpm = settings.Bpm, olderAnimateBpm = settings.Bpm;
            var latestSetAppear = true;

            for (var floor = 0; floor < tiles.Length; floor++)
            {
                var here = paint;
                foreach (var e in events[floor])
                {
                    if (e is ColorTrackEvent color)
                    {
                        here = new Paint(color.Color.ApplyTo(here.Color), color.Color.Type is null ? here.StripeOrigin : floor);
                        if (!color.JustThisTile)
                            paint = here;
                    }
                    else if (e is AnimateTrackEvent animate)
                    {
                        latestSetAppear = animate.Appear is not null;
                        if (animate.Appear is { } a)
                            (appear, beatsAhead) = (a, animate.BeatsAhead ?? 3);
                        if (animate.Disappear is { } d)
                            (disappear, beatsBehind) = (d, animate.BeatsBehind ?? 4);
                        (olderAnimateBpm, animateBpm) = (animateBpm, tiles[floor].Bpm);
                    }
                }
                var beatMs = context.BeatMs(latestSetAppear ? animateBpm : olderAnimateBpm);
                result[floor] = new FloorSettings(here, appear, beatsAhead * beatMs, disappear, beatsBehind * beatMs);
            }
            return result;
        }
    }

    // ---------- 등장 / 퇴장 애니메이션 ----------

    /// <summary>값을 정하는 데 필요한 타일 정보.</summary>
    /// <param name="Spread">Assemble/Scatter 가 흩어지는 거리 (타일 단위).</param>
    private readonly record struct Subject(int Floor, double Spread, double UnitsPerTile, Vec2 FromPrevious, Vec2 ToNext, bool HasPrevious);

    /// <summary>
    /// 채널 하나의 움직임. Target 은 끝값, StartAt 은 출발값(null 이면 그 순간 값),
    /// Additive 면 끝값은 출발값에 더하는 양, Length 는 애니메이션 길이에 곱하는 비율.
    /// </summary>
    private sealed record Motion(Channel Channel, Func<Subject, double> Target, Ease Ease,
        Func<Subject, double?>? StartAt = null, bool Additive = false, double Length = 1);

    /// <summary>
    /// 애니메이션 한 종류: 시작 전 자세(채널의 처음 값을 바꾼다)와 움직임들.
    /// Lead 는 등장 시작을 앞당기는 배수, LeadIsLength 면 애니메이션 길이가 앞당긴 시간과 같다 (아니면 반 박, 최대 0.5초).
    /// </summary>
    private sealed record Recipe((Channel Channel, Func<Subject, double> Value)[] Pose, Motion[] Motions,
        double Spread = 0, int Lead = 1, bool LeadIsLength = false, bool IsAppear = true);

    private static double Hash(Subject s, int salt) => Noise(s.Floor, salt);

    private static readonly Func<Subject, double> Zero = static _ => 0;
    private static readonly Func<Subject, double> One = static _ => 1;
    private static Motion Home(Channel c, Ease ease) => new(c, Zero, ease);
    private static Motion[] HomeXY(Ease ease) => [Home(Channel.X, ease), Home(Channel.Y, ease)];

    private static (Channel, Func<Subject, double>)[] Hidden(params (Channel, Func<Subject, double>)[] extra) =>
        [(Channel.ScaleX, Zero), (Channel.ScaleY, Zero), .. extra];

    /// <summary>
    /// 등장: 타일에 도착하기 앞서 원래 자리, 크기, 회전으로 돌아온다. 모든 종류가 위치를 원래 자리로 되돌린다.
    /// 크기 이징을 따로 정하지 않는 종류는 OutQuad 로 본다.
    /// </summary>
    private static readonly Dictionary<TrackAppearAnimation, Recipe> AppearRecipes = new()
    {
        [TrackAppearAnimation.Assemble] = Scattered(4),
        [TrackAppearAnimation.Assemble_Far] = Scattered(8),
        // 앞 타일 자리에서 뻗어 나오며 커진다. 회전은 바로 0
        [TrackAppearAnimation.Extend] = new(Hidden(),
        [
            new(Channel.X, Zero, Ease.OutSine, static s => s.HasPrevious ? s.FromPrevious.X : null),
            new(Channel.Y, Zero, Ease.OutSine, static s => s.HasPrevious ? s.FromPrevious.Y : null),
            new(Channel.Rotation, Zero, Ease.Linear, StartAt: static _ => 0, Length: 0),
            new(Channel.ScaleX, One, Ease.OutSine),
            new(Channel.ScaleY, One, Ease.OutSine),
        ]),
        [TrackAppearAnimation.Grow] = new(Hidden(),
            [.. HomeXY(Ease.OutSine), new(Channel.ScaleX, One, Ease.OutQuad), new(Channel.ScaleY, One, Ease.OutQuad)]),
        [TrackAppearAnimation.Grow_Spin] = new(Hidden((Channel.Rotation, static _ => -180)),
        [
            .. HomeXY(Ease.OutSine),
            new(Channel.ScaleX, One, Ease.OutQuad), new(Channel.ScaleY, One, Ease.OutQuad),
            Home(Channel.Rotation, Ease.OutSine),
        ]),
        [TrackAppearAnimation.Fade] = new([(Channel.Opacity, Zero)],
            [.. HomeXY(Ease.OutSine), new(Channel.Opacity, One, Ease.Linear)]),
        // 위(아래) 8 유닛에서 떨어지며(올라오며) 나타난다. 두 배 일찍 시작하고 앞당긴 시간 동안 움직인다
        [TrackAppearAnimation.Drop] = Falling(8),
        [TrackAppearAnimation.Rise] = Falling(-8),
    };

    private static Recipe Scattered(double units) => new(
        [
            (Channel.X, static s => Hash(s, 1) * s.Spread),
            (Channel.Y, static s => Hash(s, 2) * s.Spread),
            (Channel.Rotation, static s => Hash(s, 3) * 75),
        ],
        [.. HomeXY(Ease.OutSine), Home(Channel.Rotation, Ease.OutSine)],
        Spread: units);

    private static Recipe Falling(double units) => new(
        Hidden((Channel.Y, s => units / s.UnitsPerTile)),
        [.. HomeXY(Ease.Linear), new(Channel.ScaleX, One, Ease.OutQuad, Length: 1.0 / 8), new(Channel.ScaleY, One, Ease.OutQuad, Length: 1.0 / 8)],
        Lead: 2, LeadIsLength: true);

    /// <summary>
    /// 퇴장: 다음 타일에 도착하고 얼마 뒤 사라진다. 마지막 타일은 사라지지 않는다.
    /// Scatter 는 흩어지기만 하고 크기와 투명도는 그대로라 계속 보인다.
    /// </summary>
    private static readonly Dictionary<TrackDisappearAnimation, Recipe> DisappearRecipes = new()
    {
        [TrackDisappearAnimation.Scatter] = Scattering(4),
        [TrackDisappearAnimation.Scatter_Far] = Scattering(8),
        // 다음 타일 자리로 빨려 들어가며 작아진다
        [TrackDisappearAnimation.Retract] = new([],
        [
            new(Channel.ScaleX, Zero, Ease.OutSine), new(Channel.ScaleY, Zero, Ease.OutSine),
            new(Channel.X, static s => s.ToNext.X, Ease.OutSine), new(Channel.Y, static s => s.ToNext.Y, Ease.OutSine),
        ], IsAppear: false),
        [TrackDisappearAnimation.Shrink] = new([],
            [new(Channel.ScaleX, Zero, Ease.OutQuad), new(Channel.ScaleY, Zero, Ease.OutQuad)], IsAppear: false),
        [TrackDisappearAnimation.Shrink_Spin] = new([],
        [
            new(Channel.ScaleX, Zero, Ease.OutQuad), new(Channel.ScaleY, Zero, Ease.OutQuad),
            new(Channel.Rotation, static _ => -180, Ease.OutSine),
        ], IsAppear: false),
        [TrackDisappearAnimation.Fade] = new([], [new(Channel.Opacity, Zero, Ease.Linear)], IsAppear: false),
    };

    private static Recipe Scattering(double units) => new([],
    [
        new(Channel.X, static s => Hash(s, 4) * s.Spread, Ease.OutSine, Additive: true),
        new(Channel.Y, static s => Hash(s, 5) * s.Spread, Ease.OutSine, Additive: true),
        new(Channel.Rotation, static s => Hash(s, 6) * 75, Ease.OutSine),
    ], Spread: units, IsAppear: false);

    /// <summary>등장/퇴장 애니메이션 하나를 채널 변화로 바꾼다.</summary>
    private static void Schedule(BuildContext context, int floor, Recipe recipe, FloorSettings settings,
        double[] rest, List<Change> changes, int sequence)
    {
        var tiles = context.Tiles;
        var tile = tiles[floor];
        // 보통 길이: 이 타일의 반 박, 최대 0.5초
        var length = Math.Min(0.5 * context.BeatMs(tile.Bpm), 500);
        double start;
        if (recipe.IsAppear)
        {
            // 카운트다운이 시작되기 전에는 등장하지 않는다
            var countdownStart = tiles[0].TimeMs - (context.Level.Settings.CountdownTicks - 1) * context.BeatMs(context.Level.Settings.Bpm);
            start = Math.Max(tile.TimeMs - settings.AppearLeadMs * recipe.Lead, countdownStart);
            if (recipe.LeadIsLength)
                length = settings.AppearLeadMs;
        }
        else
        {
            start = tiles[floor + 1].TimeMs + settings.DisappearDelayMs;
        }

        var subject = new Subject(
            floor,
            recipe.Spread / context.UnitsPerTile,
            context.UnitsPerTile,
            floor > 0 ? tiles[floor - 1].Position - tile.Position : default,
            floor < context.LastFloor ? tiles[floor + 1].Position - tile.Position : default,
            floor > 0);

        foreach (var (channel, value) in recipe.Pose)
            rest[floor * ChannelCount + (int)channel] = value(subject);
        foreach (var m in recipe.Motions)
        {
            changes.Add(new Change(floor * ChannelCount + (int)m.Channel, start, sequence, length * m.Length,
                m.StartAt?.Invoke(subject), m.Target(subject), m.Additive, m.Ease));
        }
    }

    /// <summary>
    /// MoveTrack: 이벤트 시각에 범위 안 타일들의 채널을 목표값으로 움직인다.
    /// 목표값은 원래 상태 기준이라 PositionTrack 모양을 덮어쓰고, 이벤트에 없는 값은 건드리지 않는다.
    /// </summary>
    private static void ScheduleMoveTracks(BuildContext context, List<Change> changes, ref int sequence)
    {
        foreach (var e in context.Level.Events)
        {
            if (e is not MoveTrackEvent m || m.Floor > context.LastFloor)
                continue;
            var start = context.EventTime(m.Floor, m.AngleOffset);
            var length = m.DurationBeats * context.BeatMs(context.Tiles[m.Floor].Bpm);
            ReadOnlySpan<double?> targets = [m.PositionX, m.PositionY, m.Rotation, m.ScaleX, m.ScaleY, m.Opacity];
            var order = sequence++;
            foreach (var tile in context.Targets(m, m.StartTile, m.EndTile, m.GapLength))
            {
                for (var c = 0; c < ChannelCount; c++)
                {
                    if (targets[c] is { } to)
                        changes.Add(new Change(tile * ChannelCount + c, start, order, length, null, to, false, m.Ease));
                }
            }
        }
    }

    /// <summary>
    /// 채널 하나의 변화. Order 는 같은 시각에 시작하는 변화끼리의 순서 (먼저 만든 것이 먼저).
    /// From 이 null 이면 그 순간 값에서 출발하고, Additive 면 To 를 출발값에 더한다.
    /// </summary>
    private readonly record struct Change(int Channel, double Start, int Order, double Length, double? From, double To, bool Additive, Ease Ease);

    /// <summary>채널별로 변화를 시간순으로 늘어놓고, 각 변화의 출발값을 앞 변화의 끝값으로 채운다.</summary>
    private static (Move[] Moves, int[] First) Chain(List<Change> changes, double[] rest)
    {
        var ordered = changes
            .OrderBy(c => c.Channel)
            .ThenBy(c => c.Start)
            .ThenBy(c => c.Order)
            .ToArray();

        var moves = new Move[ordered.Length];
        var first = new int[rest.Length + 1];
        var value = 0.0;
        for (var i = 0; i < ordered.Length; i++)
        {
            var c = ordered[i];
            if (i == 0 || ordered[i - 1].Channel != c.Channel)
                value = rest[c.Channel];
            var from = c.From ?? value;
            var to = c.Additive ? from + c.To : c.To;
            moves[i] = new Move(c.Start, Math.Max(c.Length, 0), from, to, c.Ease);
            value = to;
            first[c.Channel + 1]++;
        }
        for (var i = 1; i < first.Length; i++)
            first[i] += first[i - 1];
        return (moves, first);
    }

    /// <summary>
    /// RecolorTrack: 이벤트 시각에 범위 안 타일들의 색을 바꾼다. 이벤트에 없는 값은 그 순간 타일의 값을 그대로 쓴다.
    /// 앞의 색 바꿈은 그 자리에서 끝나고, 그 순간 색에서 새 색으로 섞인다. 저절로 바뀌는 색(Glow 등)은 섞지 않고 바로 바뀐다.
    /// 색 종류를 바꾸면 범위의 첫 타일이 Stripes 의 기준이 된다.
    /// </summary>
    private static (Repaint[] Repaints, int[] First) ChainRepaints(BuildContext context, Paint[] paints)
    {
        var requests = new List<(int Tile, double Start, int Order, RecolorTrackEvent Event, int SpanStart, double Length)>();
        var order = 0;
        foreach (var e in context.Level.Events)
        {
            if (e is not RecolorTrackEvent r || r.Floor > context.LastFloor)
                continue;
            var start = context.EventTime(r.Floor, r.AngleOffset);
            var length = r.DurationBeats * context.BeatMs(context.Tiles[r.Floor].Bpm);
            var spanStart = context.Span(r, r.StartTile, r.EndTile).First;
            foreach (var tile in context.Targets(r, r.StartTile, r.EndTile, r.GapLength))
                requests.Add((tile, start, order, r, spanStart, length));
            order++;
        }
        var ordered = requests.OrderBy(q => q.Tile).ThenBy(q => q.Start).ThenBy(q => q.Order).ToArray();

        var repaints = new Repaint[ordered.Length];
        var first = new int[paints.Length + 1];
        var paint = default(Paint);
        for (var i = 0; i < ordered.Length; i++)
        {
            var (tile, start, _, r, spanStart, length) = ordered[i];
            if (i == 0 || ordered[i - 1].Tile != tile)
                paint = paints[tile];
            var from = paint.Color.Evaluate(tile, paint.StripeOrigin, ColorClock(start, context.Pitch));
            paint = new Paint(r.Color.ApplyTo(paint.Color), r.Color.Type is null ? paint.StripeOrigin : spanStart);
            var blend = paint.Color.IsAnimated ? 0 : Math.Max(length, 0);
            repaints[i] = new Repaint(start, blend, r.Ease, paint, from);
            first[tile + 1]++;
        }
        for (var i = 1; i < first.Length; i++)
            first[i] += first[i - 1];
        return (repaints, first);
    }

    /// <summary>타일과 salt 로 정해지는 -1~1 값. 흩어지는 방향을 정하는 데 쓴다 (실행할 때마다 같다).</summary>
    private static double Noise(int floor, int salt)
    {
        var h = (uint)floor * 2654435761u ^ (uint)salt * 0x9E3779B9u;
        h ^= h >> 16;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        return h / (double)uint.MaxValue * 2 - 1;
    }
}
