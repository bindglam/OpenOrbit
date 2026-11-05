using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenOrbit.Core;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Native;

/// <summary>
/// GameEngine 진입점. 엔진은 자기 스레드에서 판정을 돌리고, 결과(Hit/Cleared/Failed)는 콜백 대신 큐에 쌓아 둔다.
/// 호출자는 <c>oo_engine_wait_events</c> 로 꺼내 간다. 런타임 경계를 거꾸로 넘는 호출(upcall)은 없다.
/// <para>
/// 세션 함수와 달리 엔진 함수는 여러 스레드에서 동시에 불러도 된다. 단, destroy 는 다른 호출이 모두 끝난 뒤에 한다.
/// </para>
/// </summary>
public static unsafe partial class Exports
{
    /// <summary>엔진을 만든다. 시계는 엔진 안의 단조 시계를 쓰고, 오토플레이는 꺼진 상태로 시작한다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_create")]
    public static int EngineCreate(nint chartHandle, OoWindows* windows, nint* outEngine)
    {
        if (!TryGet(chartHandle, out Chart? chart))
            return OoStatus.InvalidHandle;
        if (windows == null || outEngine == null)
            return Fail(OoStatus.InvalidArgument, "windows 또는 outEngine 이 null 입니다.");

        *outEngine = Alloc(new NativeEngine(new GameSession(chart, windows->ToWindows())));
        return OoStatus.Ok;
    }

    /// <summary>업데이트 스레드를 멈추고 해제한다. 다른 스레드가 이 엔진을 쓰는 중이면 안 된다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_destroy")]
    public static void EngineDestroy(nint handle)
    {
        if (handle == 0)
            return;
        if (GCHandle.FromIntPtr(handle).Target is NativeEngine engine)
            engine.Engine.Dispose();
        Free(handle);
    }

    /// <summary>0 = None, 1 = Strict, 2 = Perfect.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_set_autoplay")]
    public static int EngineSetAutoplay(nint handle, int mode)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;
        if (mode is < 0 or > 2)
            return Fail(OoStatus.InvalidArgument, $"알 수 없는 오토플레이 모드: {mode}");

        e.Engine.Autoplay = (GameEngine.AutoPlay)mode;
        return OoStatus.Ok;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_engine_set_no_fail")]
    public static int EngineSetNoFail(nint handle, int noFail)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        e.Engine.NoFail = noFail != 0;
        return OoStatus.Ok;
    }

    /// <summary>GameEngine.CalibrationMs. 판정과 행성 위치에 쓰는 곡 시각을 이만큼 당긴다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_set_calibration_ms")]
    public static int EngineSetCalibrationMs(nint handle, double ms)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        e.Engine.CalibrationMs = ms;
        return OoStatus.Ok;
    }

    /// <summary>곡 시계를 0 부터 다시 돌리고 업데이트 스레드를 시작한다. 진행 중이었으면 처음부터 다시 한다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_start")]
    public static int EngineStart(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        try
        {
            e.ClearEvents();
            e.Engine.Start();
            return OoStatus.Ok;
        }
        catch (Exception ex) { return FailWith(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_engine_reset")]
    public static int EngineReset(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        e.Engine.Reset();
        e.ClearEvents();
        return OoStatus.Ok;
    }

    /// <summary>진행 중이면 실패 처리한다 (Failed 이벤트가 쌓인다).</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_fail")]
    public static int EngineFail(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        e.Engine.Fail();
        return OoStatus.Ok;
    }

    /// <summary>지금 시각으로 입력 하나를 판정한다. 결과는 이벤트로 나온다.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_press")]
    public static int EnginePress(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;

        e.Engine.Press();
        return OoStatus.Ok;
    }

    /// <summary>0 = Ready, 1 = Playing, 2 = Failed, 3 = Cleared.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_state")]
    public static int EngineState(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;
        return (int)e.Engine.CurrentState;
    }

    /// <summary>GameEngine.CurrentTimeMs. 실패하면 멈춘 시각을 준다. 핸들이 잘못되면 NaN.</summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_current_time_ms")]
    public static double EngineCurrentTimeMs(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return double.NaN;
        return e.Engine.CurrentTimeMs;
    }

    [UnmanagedCallersOnly(EntryPoint = "oo_engine_current_floor")]
    public static int EngineCurrentFloor(nint handle)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;
        return e.Engine.Session.CurrentFloor;
    }

    /// <summary>
    /// 렌더링용 행성 상태. 업데이트 스레드와 동시에 불러도 된다
    /// (GetPlanetState 는 CurrentFloor 를 한 번만 읽으므로 한 타일 기준으로 일관된 값이 나온다).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_get_planet_state")]
    public static int EngineGetPlanetState(nint handle, double songTimeMs, OoPlanetState* outState)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;
        if (outState == null)
            return Fail(OoStatus.InvalidArgument, "outState 가 null 입니다.");

        *outState = OoPlanetState.From(e.Engine.Session.GetPlanetState(songTimeMs));
        return OoStatus.Ok;
    }

    /// <summary>
    /// 쌓인 이벤트를 dst 에 최대 cap 개 꺼낸다. 비어 있으면 timeoutMs 까지 기다린다 (0 이면 바로 돌아온다).
    /// 반환값은 꺼낸 개수.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "oo_engine_wait_events")]
    public static int EngineWaitEvents(nint handle, OoEngineEvent* dst, int cap, int timeoutMs)
    {
        if (!TryGet(handle, out NativeEngine? e))
            return OoStatus.InvalidHandle;
        if ((dst == null && cap > 0) || cap < 0 || timeoutMs < 0)
            return Fail(OoStatus.InvalidArgument, "dst, cap, timeoutMs 중 잘못된 값이 있습니다.");

        return e.WaitEvents(dst, cap, timeoutMs);
    }
}

/// <summary>엔진과, 엔진 이벤트를 호출자가 꺼내 갈 때까지 담아 두는 큐.</summary>
internal sealed class NativeEngine
{
    public GameEngine Engine { get; }

    private readonly Queue<OoEngineEvent> _events = new();

    public NativeEngine(GameSession session)
    {
        Engine = new GameEngine(session, new MonotonicClock(), new NoInputEventBus())
        {
            Autoplay = GameEngine.AutoPlay.None,
        };
        // 엔진 락 안에서 불린다. 큐 락은 짧게만 잡으므로 교착은 없다.
        Engine.Hit += result => Push(OoEngineEvent.FromHit(result));
        Engine.Cleared += () => Push(OoEngineEvent.Of(OoEngineEventKind.Cleared));
        Engine.Failed += () => Push(OoEngineEvent.Of(OoEngineEventKind.Failed));
    }

    public void ClearEvents()
    {
        lock (_events)
            _events.Clear();
    }

    public unsafe int WaitEvents(OoEngineEvent* dst, int cap, int timeoutMs)
    {
        lock (_events)
        {
            if (_events.Count == 0 && timeoutMs > 0)
            {
                var deadline = Environment.TickCount64 + timeoutMs;
                while (_events.Count == 0)
                {
                    var remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0 || !Monitor.Wait(_events, (int)remaining))
                        break;
                }
            }

            var n = Math.Min(_events.Count, cap);
            for (var i = 0; i < n; i++)
                dst[i] = _events.Dequeue();
            return n;
        }
    }

    private void Push(OoEngineEvent e)
    {
        lock (_events)
        {
            _events.Enqueue(e);
            Monitor.PulseAll(_events);
        }
    }
}

/// <summary>Stopwatch 기반 곡 시계. Start 하면 0 부터 흐르고, Stop 하면 그 시각에 멈춘다.</summary>
internal sealed class MonotonicClock : IGameClock
{
    private long _startTicks;
    private double _stoppedMs;
    private volatile bool _running;

    public bool Running => _running;

    public double NowMs => _running ? Stopwatch.GetElapsedTime(_startTicks).TotalMilliseconds : _stoppedMs;

    public void Start()
    {
        _startTicks = Stopwatch.GetTimestamp();
        _running = true;
    }

    public void Stop()
    {
        if (!_running)
            return;
        _stoppedMs = NowMs;
        _running = false;
    }
}

/// <summary>입력은 oo_engine_press 로 직접 넣으므로 구독할 것이 없다.</summary>
internal sealed class NoInputEventBus : IInputEventBus
{
    public event Action Pressed { add { } remove { } }
    public event Action Released { add { } remove { } }
}
