using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using OpenOrbit.Core;
using OpenOrbit.Core.Timing;

namespace OpenOrbit.Examples.Godot;

public partial class Game : Node2D, IInputEventBus
{
    [Export(PropertyHint.File, "*.adofai")]
    public string ChartPath { get; set; } = "res://charts/Kplecraft - 16383/16383.adofai";

    /// <summary>꺼져 있으면 실제 게임처럼 미스 한 번에 실패.</summary>
    [Export] public bool NoFail { get; set; }
    [Export] public bool Autoplay { get; set; }

    private const string SettingsPath = "user://settings.cfg";
    private const float CameraZoom = 0.75f;

    private LoadedChart _loaded = null!;
    private bool _hasChart;
    private string? _message; // 차트 로딩 실패 등 대기 화면에 잠깐 띄울 메시지
    private double _calibrationMs;
    private readonly Dictionary<HitMargin, int> _counts = [];

    private GameEngine? _engine;
    private SongClock _clock = null!;
    
    private AudioStreamPlayer _music = null!;
    private HitsoundMixer _hitsounds = null!;
    private TrackView _track = null!;
    private PlanetView _planets = null!;
    private Node2D _popups = null!;
    private Camera2D _camera = null!;
    private Hud _hud = null!;
    private FileDialog _openDialog = null!;
    
    public event Action? Pressed;
    public event Action? Released;

    public override void _Ready()
    {
        RenderingServer.SetDefaultClearColor(Palette.Background);

        _music = GetNode<AudioStreamPlayer>("MusicPlayer");
        
        _hitsounds = new HitsoundMixer(GetNode<AudioStreamPlayer>("HitsoundPlayer"), _music);
        
        AddChild(_track = new TrackView());
        _planets = GetNode<PlanetView>("PlanetView");
        AddChild(_popups = new Node2D());
        AddChild(_camera = new Camera2D { Zoom = Vector2.One * CameraZoom });
        AddChild(_hud = new Hud());
        AddChild(_openDialog = CreateOpenDialog());
        _clock = new SongClock(_music);

        var lastChart = LoadSettings();

        // 마지막으로 연 채보가 있으면 그걸, 없거나 실패하면 기본 채보
        if (!(lastChart is not null && TryLoadChart(lastChart)))
            TryLoadChart(ChartPath);
    }

    // ---- 채보 열기 ----
    private FileDialog CreateOpenDialog()
    {
        var dialog = new FileDialog
        {
            Title = "채보 열기",
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = ["*.adofai ; 얼불춤 레벨"],
            UseNativeDialog = true,
        };
        dialog.FileSelected += path =>
        {
            if (TryLoadChart(path))
                SaveSettings();
        };
        return dialog;
    }

    private void OpenChartDialog()
    {
        // 지금 연 채보가 디스크에 있는 파일이면 그 폴더에서 시작
        if (_hasChart && !ChartPath.StartsWith("res://"))
            _openDialog.CurrentDir = ChartPath.GetBaseDir();
        _openDialog.PopupCentered(new Vector2I(900, 600));
    }

    /// <summary>
    /// 채보를 불러와 대기 상태로 만든다. 실패하면 메시지만 띄우고 이전 채보(있다면)를 그대로 둔다.
    /// 경로는 res:// 또는 파일 시스템 절대 경로. 곡 파일은 채보의 songFilename 을 같은 폴더에서 찾는다.
    /// </summary>
    private bool TryLoadChart(string path)
    {
        LoadedChart loaded;
        try
        {
            loaded = ChartLoader.Load(path);
        }
        catch (Exception e)
        {
            GD.PushError(e.ToString());
            _message = $"채보를 불러오지 못했습니다\n{e.Message}";
            return false;
        }
        
        _loaded = loaded;
        _hasChart = true;
        _message = loaded.Song is null ? $"곡 파일을 찾지 못해 음악 없이 진행합니다\n({loaded.Chart.Level.Settings.SongFilename})" : null;
        ChartPath = path;

        _music.Stream = loaded.Song;
        _music.PitchScale = (float)(loaded.Chart.Level.Settings.Pitch / 100.0);
        _track.Setup(loaded.Chart);
        Reset();
        return true;
    }

    // ---- 상태 전환 ----

    private void Reset()
    {
        _engine?.Reset();
        _hitsounds.Stop();

        _engine = new GameEngine(new GameSession(_loaded.Chart, JudgmentWindows.Normal), _clock, this);
        _engine.Hit += Hit;
        
        _counts.Clear();
        _hud.Meter.Setup(_engine.Session.Windows);
        foreach (var popup in _popups.GetChildren())
            popup.QueueFree();
        
        _track.CurrentFloor = 0;
        _camera.Position = Coords.ToScreen(_loaded.Chart.Tiles[0].Position);
    }

    public void StartPlaying()
    {
        if (_engine == null || !_hasChart)
            return;
        _message = null;

        _engine!.Autoplay = GameEngine.AutoPlay.Strict;
        _engine!.NoFail = NoFail;
        _engine!.Start();
        _hitsounds.Start(_loaded.Chart); // 곡 재생과 같은 프레임에 시작해야 박자가 맞는다
    }

    // ---- 매 프레임 ----
    public override void _Process(double delta)
    {
        if (_engine?.CurrentState is GameEngine.State.Failed)
            _hitsounds.Stop();
        _hitsounds.Fill();

        if (_engine == null || !_hasChart)
        {
            _hud.Center = _message ?? "";
            _hud.Hint = "O 채보 열기";
            return;
        }

        if (_engine!.CurrentState is GameEngine.State.Playing or GameEngine.State.Cleared)
        {
            _clock.Sync();
        }

        _planets.Set(_engine.Session.GetPlanetState(_engine.CurrentTimeMs), _engine.Session.CurrentFloor);

        var target = Coords.ToScreen(_loaded.Chart.Tiles[_engine.Session.CurrentFloor].Position);
        _camera.Position = _camera.Position.Lerp(target, (float)(1 - Math.Exp(-delta * 5)));

        UpdateHud(_engine.CurrentTimeMs);
    }

    private void Hit(HitResult result)
    {
        _counts[result.Margin] = _counts.GetValueOrDefault(result.Margin) + 1;
        _track.CurrentFloor = _engine!.Session.CurrentFloor;

        if (result.Margin != HitMargin.Auto)
        {
            var tiles = _loaded.Chart.Tiles;
            _hud.Meter.Add(result.Margin, result.ErrorMs / _loaded.Chart.MsPerDegree(tiles[result.Floor - 1].Bpm));

            var position = Coords.ToScreen(_loaded.Chart.Tiles[result.Floor].Position);
            _popups.CallDeferred("add_child",
                JudgmentPopup.Create(Palette.Text(result.Margin), Palette.ForMargin(result.Margin), position));
        }
    }

    // ---- 입력 ----

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Echo: false } key:
                HandleKey(key);
                break;
            case InputEventMouseButton { Pressed: true } when _engine?.CurrentState == GameEngine.State.Playing && !Autoplay:
                Pressed?.Invoke();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void HandleKey(InputEventKey key)
    {
        if (key.Keycode == Key.O && _engine?.CurrentState != GameEngine.State.Playing)
        {
            OpenChartDialog();
            return;
        }
        if (!_hasChart)
            return;

        switch (key.Keycode)
        {
            case Key.Escape:
                Reset();
                return;
            case Key.R when _engine?.CurrentState != GameEngine.State.Ready:
                StartPlaying();
                return;
        }

        if (_engine?.CurrentState == GameEngine.State.Playing)
        {
            if (!Autoplay)
                Pressed?.Invoke();
            return;
        }

        switch (key.Keycode)
        {
            case Key.Space or Key.Enter:
                StartPlaying();
                break;
            case Key.Left or Key.Right:
                var step = key.ShiftPressed ? 1 : 10;
                _calibrationMs += key.Keycode == Key.Right ? step : -step;
                SaveSettings();
                break;
            case Key.A:
                Autoplay = !Autoplay;
                break;
            case Key.N:
                NoFail = !NoFail;
                break;
        }
    }

    // ---- HUD ----

    private void UpdateHud(double time)
    {
        var tiles = _loaded.Chart.Tiles;
        var modes = string.Join("  ", new[] { Autoplay ? "AUTO" : null, NoFail ? "NO-FAIL" : null }.OfType<string>());
        _hud.Info = $"{_loaded.Name}\n타일 {_engine?.Session.CurrentFloor} / {tiles.Count - 1}\n오프셋 {_calibrationMs:+0;-0;0}ms\n{modes}";

        _hud.Stats = $"정확도 {Accuracy():0.00}%\n" +
                     $"Perfect {Count(HitMargin.Perfect)}\n" +
                     $"E/L Perfect {Count(HitMargin.EarlyPerfect)} / {Count(HitMargin.LatePerfect)}\n" +
                     $"Early / Late {Count(HitMargin.VeryEarly)} / {Count(HitMargin.VeryLate)}\n" +
                     $"Too Early {Count(HitMargin.TooEarly)}\n" +
                     $"Miss {Count(HitMargin.TooLate)}";

        _hud.Center = _engine?.CurrentState != GameEngine.State.Playing && _message is not null ? _message : _engine?.CurrentState switch
        {
            GameEngine.State.Ready => "Space 로 시작",
            GameEngine.State.Playing => Countdown(time),
            GameEngine.State.Failed => "실패!\nR 로 다시 시작",
            GameEngine.State.Cleared => $"클리어!\n정확도 {Accuracy():0.00}%\nR 로 다시 시작",
            _ => "",
        };

        _hud.Hint = _engine?.CurrentState == GameEngine.State.Playing
            ? "아무 키나 클릭으로 입력  ·  R 재시작  ·  Esc 처음으로"
            : "O 채보 열기  ·  ←/→ 오프셋 ±10ms (Shift ±1ms)  ·  A 자동 플레이  ·  N 노페일  ·  Esc 처음으로";
    }

    /// <summary>첫 입력 전 박자 카운트다운 ("3, 2, 1, Go!").</summary>
    private string Countdown(double time)
    {
        var tiles = _loaded.Chart.Tiles;
        if (tiles.Count < 2 || time >= tiles[1].TimeMs)
            return "";

        var beatMs = _loaded.Chart.MsPerDegree(tiles[0].Bpm) * 180;
        var beatsLeft = (int)Math.Ceiling((tiles[1].TimeMs - time) / beatMs);
        if (beatsLeft > _loaded.Chart.Level.Settings.CountdownTicks)
            return "";
        return beatsLeft > 1 ? (beatsLeft - 1).ToString() : "Go!";
    }

    private int Count(HitMargin margin) => _counts.GetValueOrDefault(margin);

    /// <summary>Perfect 100%, E/L Perfect 75%, Early/Late 40%, Too Early·Miss 0%. 미드스핀 자동 통과는 제외.</summary>
    private double Accuracy()
    {
        var judged = _counts.Where(c => c.Key != HitMargin.Auto).Sum(c => c.Value);
        if (judged == 0)
            return 100;

        var score = Count(HitMargin.Perfect) * 1.0
                    + (Count(HitMargin.EarlyPerfect) + Count(HitMargin.LatePerfect)) * 0.75
                    + (Count(HitMargin.VeryEarly) + Count(HitMargin.VeryLate)) * 0.4;
        return score / judged * 100;
    }

    // ---- 설정 ----

    /// <summary>보정값을 읽고, 마지막으로 연 채보 경로(없으면 null)를 돌려준다.</summary>
    private string? LoadSettings()
    {
        var config = new ConfigFile();
        if (config.Load(SettingsPath) != Error.Ok)
            return null;

        _calibrationMs = config.GetValue("game", "calibration_ms", 0.0).AsDouble();
        var lastChart = config.GetValue("game", "last_chart", "").AsString();
        return lastChart == "" ? null : lastChart;
    }

    private void SaveSettings()
    {
        var config = new ConfigFile();
        config.SetValue("game", "calibration_ms", _calibrationMs);
        if (_hasChart)
            config.SetValue("game", "last_chart", ChartPath);
        config.Save(SettingsPath);
    }
}
