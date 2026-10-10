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
    public bool Autoplay { get; set; } = false;
    public bool NoFail { get; set; } = false;
    
    public enum State { Ready, Playing, Failed, Cleared }
    public State CurrentState { get; private set; }
    
    /// <summary>판정에 쓰는 곡 시각. 오프셋(보정값)만큼 당겨진다.</summary>
    private double SongTimeMs => Clock.NowMs - CalibrationMs;
    private double _frozenMs;
    public double CurrentTimeMs => CurrentState is State.Playing or State.Cleared ? SongTimeMs : _frozenMs;

    private Thread _updateThread;

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
        
        _updateThread.Start();
    }

    public void Reset()
    {
        CurrentState = State.Ready;
        
        InitUpdateThread();
        
        Clock.Stop();
        Session.Reset();
        
        _frozenMs = 0;
    }

    public void Fail()
    {
        CurrentState = State.Failed;
        
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
            // 자동 플레이는 Update 보다 먼저 눌러야 한다. 고BPM 구간은 판정 범위가 한 프레임보다 짧아서
            // Update 가 먼저 돌면 프레임 사이에 지나간 타일을 미스로 처리해 버린다.
            var next = Session.Chart.Tiles[Session.CurrentFloor + 1];
            if (Autoplay && !next.IsMidspin && CurrentTimeMs >= next.TimeMs)
            {
                HandleHit(Session.Press(next.TimeMs)!.Value);
                continue;
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