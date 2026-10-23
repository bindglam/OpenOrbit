using OpenOrbit.Core.Timing;

namespace OpenOrbit.Core;

/// <summary>
/// 한 판의 진행을 담당하는 클래스. <see cref="Start"/> 로 몇 번이든 다시 시작할 수 있고,
/// 다 쓰면 <see cref="Dispose"/> 로 입력 구독과 업데이트 스레드를 정리해야 한다.
/// <para>
/// 판정은 업데이트 스레드와 입력 스레드에서 동시에 일어날 수 있으므로 세션 접근과 상태 전환은 한 락 안에서 한다.
/// 이벤트도 그 락 안에서 일어나므로 순서가 보장되지만, 핸들러는 다른 스레드를 기다리면 안 된다.
/// </para>
/// </summary>
public sealed class GameEngine : IDisposable
{
    public event Action<HitResult>? Hit;
    public event Action? Cleared;
    public event Action? Failed;

    public GameSession Session { get; }
    public IGameClock Clock { get; }
    public IInputEventBus InputEventBus { get; }

    public int UpdateIntervalMs { get; set; } = 1;
    public double CalibrationMs { get; set; }
    public enum AutoPlay { None, Strict, Perfect }
    public AutoPlay Autoplay { get; set; } = AutoPlay.Perfect;
    public bool NoFail { get; set; } = false;

    public enum State { Ready, Playing, Failed, Cleared }
    private volatile State _state = State.Ready;
    public State CurrentState => _state;

    /// <summary>판정에 쓰는 곡 시각. 오프셋(보정값)만큼 당겨진다.</summary>
    private double SongTimeMs => Clock.NowMs - CalibrationMs;
    private double _frozenMs;
    public double CurrentTimeMs => _state is State.Playing or State.Cleared ? SongTimeMs : _frozenMs;

    private readonly Lock _lock = new();
    private volatile Thread? _updateThread;
    private bool _disposed;

    public GameEngine(GameSession session, IGameClock clock, IInputEventBus inputEventBus)
    {
        Session = session;
        Clock = clock;
        InputEventBus = inputEventBus;

        InputEventBus.Pressed += Press;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_state != State.Ready)
            Reset();

        lock (_lock)
        {
            _state = State.Playing;
            Clock.Start();
        }

        var thread = new Thread(RunUpdateLoop) { IsBackground = true, Name = "OpenOrbit GameEngine" };
        _updateThread = thread;
        thread.Start();
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = State.Ready;
            Clock.Stop();
            Session.Reset();
            _frozenMs = 0;
        }
        StopUpdateThread();
    }

    public void Fail()
    {
        lock (_lock)
        {
            if (_state == State.Playing)
                FailLocked();
        }
    }

    /// <summary>입력 구독을 끊고 업데이트 스레드를 멈춘다. 곡 시계는 건드리지 않는다 (다음 엔진이 같이 쓸 수 있다).</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        InputEventBus.Pressed -= Press;
        lock (_lock)
        {
            if (_state == State.Playing)
                _frozenMs = SongTimeMs;
            _state = State.Ready;
        }
        StopUpdateThread();
    }

    public void Press()
    {
        lock (_lock)
        {
            if (_state != State.Playing)
                return;

            // 입력 직전까지 밀린 미드스핀/미스를 먼저 정리해야 올바른 타일을 판정한다 (둘 다 같은 시각 기준)
            var now = SongTimeMs;
            if (!Judge(Session.Update(now)))
                return;

            if (Session.Press(now) is { } hit)
                Judge(hit);

            CheckCleared();
        }
    }

    public void Update()
    {
        lock (_lock)
        {
            while (_state == State.Playing && !Session.IsFinished)
            {
                var now = SongTimeMs;
                var next = Session.Chart.Tiles[Session.CurrentFloor + 1];
                if (!next.IsMidspin && now >= next.TimeMs)
                {
                    // 모든 타일을 완벽으로 처리하는 오토
                    if (Autoplay == AutoPlay.Perfect)
                    {
                        Judge(Session.Press(next.TimeMs)!.Value);
                        continue;
                    }
                    // 플레이어와 똑같은 타이밍을 적용받는 오토
                    else if (Autoplay == AutoPlay.Strict && !Judge(Session.Press(now)!.Value))
                        break;
                }

                var results = Session.Update(now);
                if (!Judge(results) || results.Length == 0)
                    break;
            }

            CheckCleared();
        }
    }

    private void RunUpdateLoop()
    {
        // Reset 직후 바로 Start 해서 새 스레드가 생겼으면 예전 스레드는 여기서 빠진다
        while (_state == State.Playing && _updateThread == Thread.CurrentThread)
        {
            Update();
            Thread.Sleep(UpdateIntervalMs);
        }
    }

    private void StopUpdateThread()
    {
        var thread = _updateThread;
        _updateThread = null;
        // 이벤트 핸들러 안(업데이트 스레드)에서 불렸으면 자기 자신은 기다릴 수 없다. 루프 조건에서 알아서 빠진다.
        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join();
    }

    /// <returns>판정 후에도 계속 진행 중이면 true.</returns>
    private bool Judge(ReadOnlySpan<HitResult> results)
    {
        // 핸들러가 세션을 다시 건드려도 안전하도록 복사 (보통 비어 있어서 할당 없음)
        foreach (var result in results.ToArray())
        {
            if (!Judge(result))
                return false;
        }
        return true;
    }

    /// <inheritdoc cref="Judge(ReadOnlySpan{HitResult})"/>
    private bool Judge(HitResult result)
    {
        Hit?.Invoke(result);

        if (_state == State.Playing && result.Margin == HitMargin.TooLate && !NoFail)
            FailLocked();
        return _state == State.Playing;
    }

    private void FailLocked()
    {
        _frozenMs = SongTimeMs;
        _state = State.Failed;
        Clock.Stop();

        Failed?.Invoke();
    }

    private void CheckCleared()
    {
        if (_state == State.Playing && Session.IsFinished)
        {
            _state = State.Cleared;

            Cleared?.Invoke();
        }
    }
}
