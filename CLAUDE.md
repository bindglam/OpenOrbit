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
  - Typed events: gameplay (`SetSpeed`, `Twirl`, `Pause`, `Hold`, `MultiPlanet`) and track (`ColorTrack`, `RecolorTrack`, `MoveTrack`, `AnimateTrack`, `PositionTrack`), plus `MoveCamera`. Everything else is kept as `UnknownEvent` with its raw JSON.
  - Some real levels still fail to parse (duplicate keys, raw control characters inside strings); `System.Text.Json` rejects both.
- **Chart compilation:** precomputes each tile's position (1 unit = one tile, +Y up), arrival `TimeMs`, BPM, rotation direction, entry angle and travel angle.
  - `PositionTrack` (unless `editorOnly`) moves tile positions here, so the planet path follows it; angles and timing are unchanged.
  - Events with `"active": false` are dropped by the parser.
- **ADOFAI reference:** the track and camera behaviour below was checked against the decompiled game (`Assembly-CSharp.dll`, Mono build; classes `ffxMoveFloorPlus`, `ffxRecolorFloorPlus`, `ffxFloorAppearPlus`, `ffxFloorDisappearPlus`, `ffxCameraPlus`, `scrFloor`, `scrCamera`, `scnGame`). Reimplement behaviour from it; don't copy its code. A key missing from an event's JSON means "unchanged" (ADOFAI's `disabled` fields), so those fields are nullable.
- **Track timeline** (`Chart.Track`, `TrackTimeline.Evaluate(floor, songTimeMs)` → `TileVisual`): offset, rotation, scale, opacity and color of a tile at a time, for renderers.
  - `ColorTrack`/`AnimateTrack`/`PositionTrack` shape are per-floor settings (apply from their floor onward; `justThisTile` only that floor). `RecolorTrack`/`MoveTrack` are timed: they fire at their floor's `TimeMs` plus `angleOffset` and target a tile range, stepping `gapLength + 1`.
  - Each property (X, Y, rotation, scale X/Y, opacity) has one tween at a time. A new tween first completes the previous one (DOTween `Kill(complete: true)`) and starts from its end value. MoveTrack, appear and disappear all write these tweens. MoveTrack targets are absolute (scale/rotation/opacity replace the PositionTrack shape).
  - Appear starts `beatsAhead` beats before the tile is reached (twice that for Drop/Rise) and disappear `beatsBehind` beats after the next tile is reached, both measured at the BPM of the AnimateTrack that set them. They last half a beat, capped at 0.5 s. Scatter/Assemble randomness is a per-tile hash (ADOFAI uses `UnityEngine.Random`), and Scatter tiles stay visible, as in ADOFAI.
  - Colors use ADOFAI's formulas; animated types (Glow, Blink, Switch, Rainbow, Volume) switch instantly and only Single/Stripes blend over `duration`. Stripes alternate from the tile that last changed the color type.
  - Not verified: DOTween's default ease for `DOScale` without `SetEase` (assumed `OutQuad`), `Volume` color (falls back to the primary color), and Flash eases (linear).
- **Camera timeline** (`Chart.Camera`, `CameraTimeline.Evaluate(songTimeMs, player)` → `CameraState`): settings `relativeTo`/`position`/`rotation`/`zoom` plus timed `MoveCamera` (zoom stored as 1 = 100%; 100% shows 10 ADOFAI units = 6.67 tiles vertically).
  - Models ADOFAI's camera parent: a player offset while following, a world position when fixed. `Player` follows the `player` position the caller passes in (ADOFAI moves it linearly to the landed planet over 2 beats). `Tile` anchors to the event's floor, `Global` to the origin, `LastPosition` adds to the current position and rotation, `LastPositionNoRotation` sets rotation absolutely.
  - X, Y, rotation and zoom tween independently with the same complete-then-restart rule as tracks. Setting `relativeTo` turns a missing position axis into 0, unless it repeats the previous `relativeTo` (same tile for `Tile`). `legacyCamRelativeTo` adds positions without `relativeTo` to the current one.
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
