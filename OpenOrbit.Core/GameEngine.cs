using OpenOrbit.Core.Timing;

namespace OpenOrbit.Core;

/// <summary>
/// 한 판의 진행을 담당하는 클래스. 재사용 불가
/// </summary>
public sealed class GameEngine
{
    public event Action<HitResult> Hit; 
    public event Action Cleared;
    public event Action Failed;
    
    public GameSession Session { get; }
    public IGameClock Clock { get; }
    public IInputEventBus InputEventBus { get; }
    
    public double CalibrationMs { get; set; }
    public enum AutoPlay { None, Strict, Perfect }
    public AutoPlay Autoplay { get; set; } = AutoPlay.Perfect;
    public bool NoFail { get; set; } = false;
    
    public enum State { Ready, Playing, Failed, Cleared }
    public State CurrentState { get; private set; }
    
    /// <summary>판정에 쓰는 곡 시각. 오프셋(보정값)만큼 당겨진다.</summary>
    private double SongTimeMs => Clock.NowMs - CalibrationMs;
    private double _frozenMs;
    public double CurrentTimeMs => CurrentState is State.Playing or State.Cleared ? SongTimeMs : _frozenMs;

    private Thread? _updateThread;

    public GameEngine(GameSession session, IGameClock clock, IInputEventBus inputEventBus)
    {
        Session = session;
        Clock = clock;
        InputEventBus = inputEventBus;
        
        CurrentState = State.Ready;

        InputEventBus.Pressed += Press;
        
        InitUpdateThread();
    }

    private void InitUpdateThread()
    {
        if(_updateThread?.ThreadState == ThreadState.Running)
            _updateThread?.Join();
        _updateThread = new Thread(() =>
        {
            while (CurrentState is State.Playing or State.Cleared)
            {
                Update();
                Thread.Sleep(1);
            }
        })
        {
            IsBackground = true
        };
    }

    public void Start()
    {
        if(CurrentState != State.Ready)
            Reset();
        CurrentState = State.Playing;
        
        Clock.Start();
        
        if(_updateThread == null)
            InitUpdateThread();
        _updateThread!.Start();
    }

    public void Reset()
    {
        CurrentState = State.Ready;
        
        if(_updateThread?.ThreadState == ThreadState.Running)
            _updateThread?.Join();
        _updateThread = null;
        
        Clock.Stop();
        Session.Reset();
        
        _frozenMs = 0;
    }

    public void Fail()
    {
        CurrentState = State.Failed;
        
        if(_updateThread?.ThreadState == ThreadState.Running)
            _updateThread?.Join();
        _updateThread = null;
        
        _frozenMs = SongTimeMs;
        
        Clock.Stop();

        Failed?.Invoke();
    }
    
    private void HandleHit(HitResult result)
    {
        Hit?.Invoke(result);
        
        if (result.Margin == HitMargin.TooLate && !NoFail)
            Fail();
    }
    
    public void Press()
    {
        // 입력 직전까지 밀린 미드스핀/미스를 먼저 정리해야 올바른 타일을 판정한다
        foreach (var result in Session.Update(SongTimeMs))
            HandleHit(result);
        if (CurrentState != State.Playing)
            return;

        if (Session.Press(SongTimeMs) is { } hit)
            HandleHit(hit);
        
        if (Session.IsFinished)
        {
            CurrentState = State.Cleared;
            
            Cleared?.Invoke();
        }
    }
    
    public void Update()
    {
        while (CurrentState == State.Playing && !Session.IsFinished)
        {
            var next = Session.Chart.Tiles[Session.CurrentFloor + 1];
            if (!next.IsMidspin && CurrentTimeMs >= next.TimeMs)
            {
                // 모든 타일을 완벽으로 처리하는 오토
                if (Autoplay == AutoPlay.Perfect)
                {
                    HandleHit(Session.Press(next.TimeMs)!.Value);
                    continue;
                }
                // 플레이어와 똑같은 타이밍을 적용받는 오토
                else if(Autoplay == AutoPlay.Strict)
                {
                    HandleHit(Session.Press(CurrentTimeMs)!.Value);
                }
            }

            var results = Session.Update(CurrentTimeMs);
            foreach (var result in results)
                HandleHit(result);
            if (results.Length == 0)
                break;
        }

        if (CurrentState == State.Playing && Session.IsFinished)
        {
            CurrentState = State.Cleared;
            
            Cleared?.Invoke();
        }
    }
}