using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using OpenOrbit.Core;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Native;

/// <summary>
/// C ABI 진입점. 객체는 GCHandle 을 정수 핸들로 바꿔 넘기고, 다 쓰면 *_destroy 로 해제해야 한다.
/// <para>
/// 예외가 네이티브 경계를 넘으면 프로세스가 죽으므로 모든 함수는 예외를 잡아 상태 코드로 바꾼다.
/// 세션은 스레드 안전하지 않다. 한 세션은 한 스레드에서만(또는 호출자가 동기화해서) 써야 한다.
/// </para>
/// </summary>
public static unsafe class Exports
{
    [ThreadStatic] private static string? _lastError;

    // ---------- 공통 ----------

    /// <summary>
    /// 이 스레드에서 마지막으로 실패한 호출의 메시지를 UTF-8 로 buf 에 쓴다 (NUL 미포함, cap 을 넘으면 잘림).
    /// 반환값은 전체 메시지의 바이트 수이므로 cap = 0 으로 길이를 먼저 알아낼 수 있다.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_last_error")]
    public static int LastError(byte* buf, int cap) => WriteUtf8(_lastError ?? "", buf, cap);

    /// <summary>판정 프리셋. 0 = Lenient, 1 = Normal, 2 = Strict, 3 = SuperStrict.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_windows_preset")]
    public static int WindowsPreset(int preset, OoWindows* outWindows)
    {
        if (outWindows == null)
            return Fail(OoStatus.InvalidArgument, "outWindows 가 null 입니다.");

        JudgmentWindows? windows = preset switch
        {
            0 => JudgmentWindows.Lenient,
            1 => JudgmentWindows.Normal,
            2 => JudgmentWindows.Strict,
            3 => JudgmentWindows.SuperStrict,
            _ => null,
        };
        if (windows is null)
            return Fail(OoStatus.InvalidArgument, $"알 수 없는 판정 프리셋: {preset}");

        *outWindows = OoWindows.From(windows);
        return OoStatus.Ok;
    }

    /// <summary>PlanetState.PositionAt. outXy 에 double 2개(x, y)를 쓴다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_planet_position_at")]
    public static int PlanetPositionAt(OoPlanetState* state, int k, double* outXy)
    {
        if (state == null || outXy == null)
            return Fail(OoStatus.InvalidArgument, "state 또는 outXy 가 null 입니다.");

        var p = state->ToState().PositionAt(k);
        outXy[0] = p.X;
        outXy[1] = p.Y;
        return OoStatus.Ok;
    }

    // ---------- 차트 ----------

    /// <summary>.adofai JSON(UTF-8, len 바이트)을 파싱하고 컴파일한다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_load")]
    public static int ChartLoad(byte* json, int len, nint* outChart)
    {
        if (json == null || len < 0 || outChart == null)
            return Fail(OoStatus.InvalidArgument, "json, len, outChart 중 잘못된 값이 있습니다.");

        try
        {
            var level = LevelParser.Parse(new ReadOnlySpan<byte>(json, len));
            *outChart = Alloc(ChartCompiler.Compile(level));
            return OoStatus.Ok;
        }
        catch (Exception e) { return FailWith(e); }
    }

    /// <summary>.adofai 파일(경로는 NUL 로 끝나는 UTF-8)을 읽어 파싱하고 컴파일한다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_load_file")]
    public static int ChartLoadFile(byte* path, nint* outChart)
    {
        if (path == null || outChart == null)
            return Fail(OoStatus.InvalidArgument, "path 또는 outChart 가 null 입니다.");

        try
        {
            var level = LevelParser.ParseFile(Marshal.PtrToStringUTF8((nint)path)!);
            *outChart = Alloc(ChartCompiler.Compile(level));
            return OoStatus.Ok;
        }
        catch (Exception e) { return FailWith(e); }
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_chart_destroy")]
    public static void ChartDestroy(nint chart) => Free(chart);

    /// <summary>타일 개수. 실패하면 음수.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_tile_count")]
    public static int ChartTileCount(nint handle)
    {
        if (!TryGet(handle, out Chart? chart))
            return OoStatus.InvalidHandle;
        return chart.Tiles.Count;
    }

    /// <summary>start 번째 타일부터 최대 count 개를 dst 에 복사한다. 반환값은 복사한 개수.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_get_tiles")]
    public static int ChartGetTiles(nint handle, int start, int count, OoTile* dst)
    {
        if (!TryGet(handle, out Chart? chart))
            return OoStatus.InvalidHandle;
        if (dst == null || start < 0 || count < 0)
            return Fail(OoStatus.InvalidArgument, "dst, start, count 중 잘못된 값이 있습니다.");

        var tiles = chart.Tiles;
        var n = Math.Clamp(tiles.Count - start, 0, count);
        for (var i = 0; i < n; i++)
            dst[i] = OoTile.From(tiles[start + i]);
        return n;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_chart_get_settings")]
    public static int ChartGetSettings(nint handle, OoSettings* outSettings)
    {
        if (!TryGet(handle, out Chart? chart))
            return OoStatus.InvalidHandle;
        if (outSettings == null)
            return Fail(OoStatus.InvalidArgument, "outSettings 가 null 입니다.");

        *outSettings = OoSettings.From(chart.Level.Settings);
        return OoStatus.Ok;
    }

    /// <summary>곡 파일 이름. 쓰는 방식과 반환값은 oo_last_error 와 같다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_get_song_filename")]
    public static int ChartGetSongFilename(nint handle, byte* buf, int cap)
    {
        if (!TryGet(handle, out Chart? chart))
            return OoStatus.InvalidHandle;
        return WriteUtf8(chart.Level.Settings.SongFilename, buf, cap);
    }

    /// <summary>Chart.MsPerDegree. 핸들이 잘못되면 NaN.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_chart_ms_per_degree")]
    public static double ChartMsPerDegree(nint handle, double bpm)
    {
        if (!TryGet(handle, out Chart? chart))
            return double.NaN;
        return chart.MsPerDegree(bpm);
    }

    // ---------- 세션 ----------

    /// <summary>
    /// 세션을 만든다. 세션은 차트를 따로 참조하므로 차트 핸들을 먼저 해제해도 된다.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_create")]
    public static int SessionCreate(nint chartHandle, OoWindows* windows, nint* outSession)
    {
        if (!TryGet(chartHandle, out Chart? chart))
            return OoStatus.InvalidHandle;
        if (windows == null || outSession == null)
            return Fail(OoStatus.InvalidArgument, "windows 또는 outSession 이 null 입니다.");

        *outSession = Alloc(new NativeSession(new GameSession(chart, windows->ToWindows())));
        return OoStatus.Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_session_destroy")]
    public static void SessionDestroy(nint session) => Free(session);

    [UnmanagedCallersOnly(EntryPoint = "oo_session_reset")]
    public static int SessionReset(nint handle)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;

        s.Session.Reset();
        s.Pending.Clear();
        s.PendingStart = 0;
        return OoStatus.Ok;
    }

    /// <summary>GameSession.Press. 판정이 나오면 outResult 에 쓰고 1, 끝난 판이라 판정이 없으면 0.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_press")]
    public static int SessionPress(nint handle, double songTimeMs, OoHitResult* outResult)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        if (outResult == null)
            return Fail(OoStatus.InvalidArgument, "outResult 가 null 입니다.");

        if (s.Session.Press(songTimeMs) is not { } result)
            return 0;
        *outResult = OoHitResult.From(result);
        return 1;
    }

    /// <summary>
    /// GameSession.Update. 새로 나온 판정(미드스핀 자동 통과, 미스)을 dst 에 최대 cap 개 쓰고 그 개수를 돌려준다.
    /// 다 못 쓴 판정은 남겨 두었다가 다음 oo_session_update / oo_session_drain 에서 이어서 준다.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_update")]
    public static int SessionUpdate(nint handle, double songTimeMs, OoHitResult* dst, int cap)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        if ((dst == null && cap > 0) || cap < 0)
            return Fail(OoStatus.InvalidArgument, "dst 또는 cap 이 잘못되었습니다.");

        foreach (var result in s.Session.Update(songTimeMs))
            s.Pending.Add(result);
        return s.Drain(dst, cap);
    }

    /// <summary>남아 있는 판정을 dst 에 최대 cap 개 쓴다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_drain")]
    public static int SessionDrain(nint handle, OoHitResult* dst, int cap)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        if ((dst == null && cap > 0) || cap < 0)
            return Fail(OoStatus.InvalidArgument, "dst 또는 cap 이 잘못되었습니다.");

        return s.Drain(dst, cap);
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_session_current_floor")]
    public static int SessionCurrentFloor(nint handle)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        return s.Session.CurrentFloor;
    }

    /// <summary>끝났으면 1, 아니면 0.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_is_finished")]
    public static int SessionIsFinished(nint handle)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        return s.Session.IsFinished ? 1 : 0;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_session_result_count")]
    public static int SessionResultCount(nint handle)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        return s.Session.Results.Count;
    }

    /// <summary>지금까지의 모든 판정 중 start 번째부터 최대 count 개를 복사한다. 반환값은 복사한 개수.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_session_get_results")]
    public static int SessionGetResults(nint handle, int start, int count, OoHitResult* dst)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        if (dst == null || start < 0 || count < 0)
            return Fail(OoStatus.InvalidArgument, "dst, start, count 중 잘못된 값이 있습니다.");

        var results = s.Session.Results;
        var n = Math.Clamp(results.Count - start, 0, count);
        for (var i = 0; i < n; i++)
            dst[i] = OoHitResult.From(results[start + i]);
        return n;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_session_get_planet_state")]
    public static int SessionGetPlanetState(nint handle, double songTimeMs, OoPlanetState* outState)
    {
        if (!TryGet(handle, out NativeSession? s))
            return OoStatus.InvalidHandle;
        if (outState == null)
            return Fail(OoStatus.InvalidArgument, "outState 가 null 입니다.");

        *outState = OoPlanetState.From(s.Session.GetPlanetState(songTimeMs));
        return OoStatus.Ok;
    }

    // ---------- 내부 ----------

    private static nint Alloc(object target) => GCHandle.ToIntPtr(GCHandle.Alloc(target));

    private static void Free(nint handle)
    {
        if (handle != 0)
            GCHandle.FromIntPtr(handle).Free();
    }

    /// <summary>0 이거나 다른 종류의 핸들이면 false. 해제된 핸들이나 아무 값이나 넘기면 막을 수 없다.</summary>
    private static bool TryGet<T>(nint handle, [NotNullWhen(true)] out T? target) where T : class
    {
        target = handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as T;
        if (target is null)
            _lastError = $"잘못된 {typeof(T).Name} 핸들입니다.";
        return target is not null;
    }

    private static int Fail(int status, string message)
    {
        _lastError = message;
        return status;
    }

    private static int FailWith(Exception e) => e switch
    {
        LevelParseException => Fail(OoStatus.ParseError, e.Message),
        IOException or UnauthorizedAccessException => Fail(OoStatus.IoError, e.Message),
        _ => Fail(OoStatus.InternalError, e.ToString()),
    };

    private static int WriteUtf8(string text, byte* buf, int cap)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        if (buf != null && cap > 0)
            bytes.AsSpan(0, Math.Min(cap, bytes.Length)).CopyTo(new Span<byte>(buf, cap));
        return bytes.Length;
    }
}

/// <summary>세션과, Update 로 나왔지만 아직 호출자에게 넘기지 못한 판정.</summary>
internal sealed class NativeSession(GameSession session)
{
    public GameSession Session { get; } = session;
    public List<HitResult> Pending { get; } = [];
    public int PendingStart { get; set; }

    public unsafe int Drain(OoHitResult* dst, int cap)
    {
        var n = Math.Min(Pending.Count - PendingStart, cap);
        for (var i = 0; i < n; i++)
            dst[i] = OoHitResult.From(Pending[PendingStart + i]);
        PendingStart += n;

        if (PendingStart == Pending.Count)
        {
            Pending.Clear();
            PendingStart = 0;
        }
        return n;
    }
}
