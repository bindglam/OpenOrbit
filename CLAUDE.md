# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

OpenOrbit is a rhythm game engine that plays A Dance of Fire and Ice (ADOFAI, 얼불춤) `.adofai` charts. The goal is to behave like the real game; when in doubt, match ADOFAI. It is under heavy development, and breaking API changes are acceptable.

## Commands

```sh
dotnet publish OpenOrbit.Native -c Release -r osx-arm64 -o OpenOrbit.Native/bin/publish/osx-arm64   # native lib for Java
```

- There is no test project.
- Java and Godot commands live in `OpenOrbit.Java/CLAUDE.md` and `OpenOrbit.Godot/CLAUDE.md`.

## Architecture

The .NET projects (`OpenOrbit.slnx`) are all on .NET 10:

- **`OpenOrbit.Core`**:
  - Engine-agnostic: no dependencies, and `IsAotCompatible` is set, so avoid reflection.
- **`OpenOrbit.Godot`**: a playable Godot 4.7 frontend that references Core.
- **`OpenOrbit.Native`**:
  - NativeAOT shared library (`openorbit.dylib/.so/.dll`) exporting a C ABI (`oo_*`) over `GameSession` and `GameEngine`.
  - Never call back into the host: the engine gets a built-in `MonotonicClock` and a no-op input bus, input comes in through `oo_engine_press`, and engine events are queued for `oo_engine_wait_events` instead of invoked across the boundary.
  - Objects are `GCHandle` handles; failures return negative `OoStatus` with a thread-local `oo_last_error`.
  - Struct layouts in `NativeTypes.cs` must match `Native.java` in `OpenOrbit.Java`.
- **`OpenOrbit.Java`** (Gradle, outside the solution): FFM bindings over `OpenOrbit.Native` plus a Minecraft plugin example.

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
  - Game modes: `Lenient`, `Normal` and `Strict` presets.
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

## Conventions

- Code comments and docs are in Korean; keep that style.
- The README is in English.
