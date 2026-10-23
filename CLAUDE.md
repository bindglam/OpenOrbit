# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

OpenOrbit is a rhythm game engine that plays A Dance of Fire and Ice (ADOFAI, 얼불춤) `.adofai` charts. The goal is to behave like the real game; when in doubt, match ADOFAI. It is under heavy development, and breaking API changes are acceptable.

## Commands

```sh
dotnet build OpenOrbit.slnx          # build everything (Core + Godot example)
dotnet build OpenOrbit.Core          # core library only
```

- Run the example game: open `OpenOrbit.Examples.Godot/project.godot` in Godot 4.7 (.NET build). The binary is at `/Applications/Godot.app/Contents/MacOS/Godot`.
- Run log: `~/Library/Application Support/Godot/app_userdata/OpenOrbit.Examples.Godot/logs/`. Godot thread-guard errors show up here.
- Saved settings (offset, last chart): `user://settings.cfg` in that same directory.
- Default test chart: `OpenOrbit.Examples.Godot/charts/Kplecraft - 16383/16383.adofai`.
- There is no test project.
- To verify game behavior headlessly, temporarily add a scene and script under `OpenOrbit.Examples.Godot/_Verify/` that instantiates `res://scenes/game.tscn` and drives it. Run it with `Godot --path OpenOrbit.Examples.Godot --headless [--audio-driver CoreAudio] res://_Verify/<scene>.tscn`, then delete `_Verify/`.

## Architecture

There are two projects (`OpenOrbit.slnx`), both on .NET 10:

- **`OpenOrbit.Core`**:
  - Engine-agnostic: no dependencies, and `IsAotCompatible` is set, so avoid reflection.
- **`OpenOrbit.Examples.Godot`**:
  - A playable Godot 4.7 frontend that references Core.
  - Namespace `OpenOrbit.Examples.Godot`.

### Core pipeline

`LevelParser` (`.adofai` JSON → `Level`) → `ChartCompiler.Compile` (→ `Chart` of `Tile`s) → `GameSession` (pure judgment state) → `GameEngine` (threaded game loop).

- **Level parsing:**
  - Handles both `angleData` and legacy `pathData`.
  - Only events that affect gameplay are typed (`SetSpeed`, `Twirl`, `Pause`, `Hold`). Everything else is kept as `UnknownEvent` with its raw JSON.
- **Chart compilation:** precomputes each tile's position (1 unit = one tile, +Y up), arrival `TimeMs`, BPM, rotation direction, entry angle and travel angle.
- **Midspins** (`Angle == 999`):
  - The tile before a midspin is a dead end that the planet only touches.
  - The midspin tile itself is placed one cell *back* (at tile k-1's position), and the incoming direction is reversed.
  - The renderer relies on this geometry, so don't "fix" it without checking real ADOFAI screenshots.
- **Judgment is angle-based** (`JudgmentWindows`):
  - The effective window is `max(degrees, ms / msPerDegree)`, where `Chart.MsPerDegree(bpm)` includes pitch.
  - The ms floor keeps windows hittable at very high BPM.
  - Game modes: `Lenient`, `Normal` and `Strict` presets. The Godot game uses `Strict`.
- **`GameSession`:**
  - `Press(time)` judges an input.
  - `Update(time)` auto-passes midspins (`HitMargin.Auto`) and emits `TooLate` (miss) for tiles whose window has passed.
  - It is pure logic; time is injected.
- **`GameEngine`:**
  - Runs `Update` on its own background thread (every `UpdateIntervalMs`).
  - Input arrives via `IInputEventBus.Pressed`, usually on the host's main thread.
  - All session access and state transitions happen under one lock. `Hit`, `Failed` and `Cleared` are raised inside that lock, possibly on the engine thread, so handlers must not block on other threads and must marshal UI work.
  - An engine subscribes to the input bus in its constructor, so a discarded engine **must be `Dispose()`d**. Otherwise it keeps judging input and stops the shared clock.
- **Host integration:** implement `IGameClock` (song clock, should follow audio position) and `IInputEventBus`.

### Godot example

- **`Game`:**
  - Orchestrates loading, state transitions, input and the HUD.
  - Implements `IInputEventBus` itself.
  - `SongClock` is the `IGameClock`: it runs on a system timer that is nudged toward the audio playback position each frame. `Stop()` may be called from the engine thread, so Godot node calls are deferred to the main thread.
- **`TrackView`:**
  - Renders tiles with one `RenderingServer` canvas item per tile.
  - Draw commands are recorded once and re-recorded only for tiles whose passed/unpassed state changes.
  - Earlier tiles draw on top (draw index `n - i`).
  - Tiles are built from `arm.png`/`joint.png` (two passes: outline, then fill); the dead-end tile before a midspin uses `midspin.png`.
- **`HitsoundMixer`:** synthesizes hitsounds sample-accurately at chart times into an `AudioStreamGenerator`, independent of input, so high-BPM streams don't smear.
- **`HitErrorMeter`:** ADOFAI-style timing meter. Errors are mapped piecewise so effective (ms-floored) window boundaries line up with fixed color bands.
- **Coordinates:** `Coords.ToScreen` converts Core units to pixels (`UnitPx = 100`, Y flipped).

## Conventions

- Code comments and docs are in Korean; keep that style.
- The README is in English.
