# ☄️ OpenOrbit

> [!IMPORTANT]
> This library is under active and heavy development. Breaking changes may occur at any time.

An open-source rhythm game engine compatible with [A Dance of Fire and Ice](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/) (ADOFAI) charts, written in C#.

## ⭐️ Features
- Tiles
  - [x] Midspin
- Events
  - [x] `SetSpeed`
    - [x] BPM
    - [x] multiplier
  - [x] `Twirl`
  - [x] `Pause`
    - [ ] Planet rotation
  - [ ] `Hold`
- Judgement
  - [x] `Lenient`
  - [x] `Normal`
  - [x] `Strict`
  - [x] ~~`Super Strict`~~
- [x] Threaded game engine

## 📥 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Godot 4.7 (.NET / mono build)](https://godotengine.org/) for the example game

## 🚀 Getting started

### Building the core library

```sh
dotnet build OpenOrbit.Core
```

### Running the example game

1. Open `OpenOrbit.Examples.Godot/project.godot` in the Godot .NET editor.
2. Build the C# solution and run the project.
3. Press `O` to open an `.adofai` chart. The song file referenced by the chart is loaded from the same folder.

#### Controls

| Key | Action |
|---|---|
| `O` | Open a chart |
| `Space` / `Enter` | Start |
| Any key / mouse click | Hit (while playing) |
| `R` | Restart |
| `Esc` | Stop and reset |
| `←` / `→` | Adjust offset by 10 ms (`Shift` for 1 ms) |
| `A` | Toggle autoplay |
| `N` | Toggle no-fail |

## 🧑🏻‍💻 Using the core library

```csharp
using OpenOrbit.Core;
using OpenOrbit.Core.Charts;
using OpenOrbit.Core.Charts.Levels;
using OpenOrbit.Core.Timing;

var level = LevelParser.ParseFile("level.adofai");
var chart = ChartCompiler.Compile(level);
var session = new GameSession(chart, JudgmentWindows.Normal);

// Provide your own song clock and input source
var engine = new GameEngine(session, myClock, myInput)
{
    Autoplay = GameEngine.AutoPlay.None,
    CalibrationMs = 0,
};

engine.Hit += result => Console.WriteLine($"Floor {result.Floor}: {result.Margin} ({result.ErrorMs:+0.0;-0.0} ms)");
engine.Cleared += () => Console.WriteLine("Cleared!");
engine.Failed += () => Console.WriteLine("Failed");

engine.Start();
```

To integrate with your own engine or framework, implement two interfaces:

- **`IGameClock`**: the song playback clock (`NowMs`, `Start`, `Stop`). It should follow the actual audio position for accurate sync.
- **`IInputEventBus`**: raises `Pressed` / `Released` when the player hits.

Events such as `Hit` are raised on the engine's update thread, so marshal them to your main thread when touching UI. For rendering, `GameSession.GetPlanetState(timeMs)` returns the pivot and orbiting planet positions at a given song time.

## 📜 License

[MIT](LICENSE) © 2026 Woobeen Jeon
