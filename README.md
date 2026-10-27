# ☄️ OpenOrbit

> [!IMPORTANT]
> This library is under active and heavy development. Breaking changes may occur at any time.

An open-source rhythm game engine compatible with [A Dance of Fire and Ice](https://store.steampowered.com/app/977950/A_Dance_of_Fire_and_Ice/) (ADOFAI) charts, written in C#.

## ⭐️ Features
- Tiles
  - [x] Normal
  - [x] Midspin
- Events
  - [x] `SetSpeed`
    - [x] BPM
    - [x] multiplier
  - [x] `Twirl`
  - [x] `Pause`
    - [ ] Planet rotation
  - [ ] `Hold`
  - [x] `Multi Planet`
- Judgement
  - [x] `Lenient`
  - [x] `Normal`
  - [x] `Strict`
  - [x] ~~`Super Strict`~~
- Platforms:
  - [x] C#
  - [x] Java (via NativeAOT + FFM)
  - [x] Godot Engine
  - [ ] Unity
- [x] Threaded game engine

## 📥 Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/)
- [Godot 4.7 (.NET / mono build)](https://godotengine.org/) for the example game

## 🚀 Getting started

### Building the core library

```sh
dotnet build OpenOrbit.Core
```

### Using from Java

`OpenOrbit.Native` exports a C API from the core with NativeAOT, and `OpenOrbit.Java/core` binds it with the FFM API (Java 25+).

```sh
cd OpenOrbit.Java
./gradlew :core:publishNative   # NativeAOT build for this OS/arch → ../OpenOrbit.Native/bin/publish/<rid>/
./gradlew build                 # core jar (and the example plugin) with natives/<rid>/ bundled
```

NativeAOT can't cross-compile between operating systems, so run `publishNative` on each target OS and collect the `bin/publish/<rid>/` folders before building the jar.
Run with `--enable-native-access=ALL-UNNAMED` (or your module name) to silence the restricted-method warning.

```java
try (var chart = Chart.load(Path.of("level.adofai"));
     var session = new GameSession(chart, JudgmentWindows.STRICT)) {
    session.update(songTimeMs, result -> { /* auto-passed midspins, misses */ });
    HitResult hit = session.press(songTimeMs);
    PlanetState planets = session.planetState(songTimeMs);
}
```

### Minecraft plugin example

`OpenOrbit.Java/examples/minecraft-plugin` is a Paper 26.1 plugin that plays ADOFAI charts in Minecraft.

1. Build it with `./gradlew :examples:minecraft-plugin:build` → `build/libs/OpenOrbit-<version>.jar`, or run a test server with `./gradlew :examples:minecraft-plugin:runServer`.
2. Put chart folders (the `.adofai` file and its `.ogg` song) in `plugins/OpenOrbit/charts/`.
3. `/adofai play <chart>` to play, `/adofai auto <chart>` to watch autoplay, `/adofai stop`, `/adofai list`, `/adofai offset [ms]`.

Any movement key (WASD, Space, Shift, Ctrl) or left click is a hit. Songs are sent to the client as a generated resource pack served by a built-in HTTP server (`resource-pack.public-url` in `config.yml`); only `.ogg` songs can be played.
The vanilla client sends input at 20 Hz, so timing is coarser than the real game; `lenient` judgment is the default.

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
