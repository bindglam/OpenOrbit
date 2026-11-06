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
- **Units:** Core positions are in tiles. `settings.tileShape` sets how many ADOFAI units one tile is (`Chart.UnitsPerTile`: Long 1.5, Short 1.0). Sizes ADOFAI gives in units (track animation ranges, tile meshes, camera height, planet size) are converted with it.
- **Chart compilation:** precomputes each tile's position (1 unit = one tile, +Y up), arrival `TimeMs`, BPM, rotation direction, entry angle and travel angle.
  - `PositionTrack` (unless `editorOnly`) moves tile positions here, so the planet path follows it; angles and timing are unchanged.
  - Events with `"active": false` are dropped by the parser.
- **Matching ADOFAI:** the track and camera behaviour below matches what the real game does. The code is our own implementation: write it from the observable behaviour, never by copying or translating the game's code, and don't name the game's internal classes or fields in code or docs. A key missing from an event's JSON means "unchanged", so those fields are nullable.
- **Track timeline** (`Chart.Track`, `TrackTimeline.Evaluate(floor, songTimeMs)` → `TileVisual`): offset, rotation, scale, opacity and color of a tile at a time, for renderers.
  - `ColorTrack`/`AnimateTrack`/`PositionTrack` shape are per-floor settings (apply from their floor onward; `justThisTile` only that floor). `RecolorTrack`/`MoveTrack` are timed: they fire at their floor's `TimeMs` plus `angleOffset` and target a tile range, stepping `gapLength + 1`.
  - Each property (X, Y, rotation, scale X/Y, opacity) has one tween at a time. A new tween first jumps the previous one to its end value and starts from there. MoveTrack, appear and disappear all write these tweens. MoveTrack targets are absolute (scale/rotation/opacity replace the PositionTrack shape).
  - Appear starts `beatsAhead` beats before the tile is reached (twice that for Drop/Rise) and disappear `beatsBehind` beats after the next tile is reached, both measured at the BPM of the AnimateTrack that set them. They last half a beat, capped at 0.5 s. Scatter/Assemble randomness is a per-tile hash (ADOFAI's is random each run, so only the range matches), and Scatter tiles stay visible, as in ADOFAI.
  - Colors use ADOFAI's formulas; animated types (Glow, Blink, Switch, Rainbow, Volume) switch instantly and only Single/Stripes blend over `duration`. Stripes alternate from the tile that last changed the color type.
  - Not verified: the scale ease of Grow, Grow_Spin, Drop/Rise, Shrink and Shrink_Spin (assumed `OutQuad`), `Volume` color (falls back to the primary color), and Flash eases (linear).
- **Camera timeline** (`Chart.Camera`, `CameraTimeline.Evaluate(songTimeMs, player)` → `CameraState`): settings `relativeTo`/`position`/`rotation`/`zoom` plus timed `MoveCamera` (zoom stored as 1 = 100%; 100% shows 10 ADOFAI units vertically, i.e. 6.67 tiles with Long tiles and 10 with Short).
  - Models ADOFAI's camera parent: a player offset while following, a world position when fixed. `Player` follows the `player` position the caller passes in (ADOFAI moves it linearly to the landed planet over 2 beats). `Tile` anchors to the event's floor, `Global` to the origin, `LastPosition` adds to the current position and rotation, `LastPositionNoRotation` sets rotation absolutely.
  - X, Y, rotation and zoom tween independently with the same complete-then-restart rule as tracks. Setting `relativeTo` turns a missing position axis into 0, unless it repeats the previous `relativeTo` (same tile for `Tile`). `legacyCamRelativeTo` adds positions without `relativeTo` to the current one.
- **Tile shapes** (`FloorMesh`, `FloorGeometry`): our own implementation, not a port of ADOFAI's mesh code, but tiles must look the same as in ADOFAI. Change the construction freely; check the look against screenshots.
  - `FloorMesh.Shapes(chart)` gives each floor's two arm directions from angleData (not positions, so PositionTrack doesn't bend arms): the exit is the next tile's angle, the entry is the reverse of the previous exit (floor 0 enters from 180°). Before a midspin the exit equals the entry (a dead end); the last floor is straight.
  - A tile is a signed distance function, not a polygon:
    - a corner tile is (arm 0 ∪ arm 1) ∩ a rounded wedge. Each arm is a strip of half-width W, cut at its end, reaching back to the other arm's end line. The wedge lies between the two outer sides, and its corner is rounded by `CornerRoundness(spread)` × W (1 below 5°, down to 0 from 120°);
    - a straight tile is the union of the two arms;
    - a dead end is a pentagon: an arm of 2/3 length starting length/12 behind the center, plus a pointed tip W deep.
  - Arms are at least as long as they are wide.
  - Shadows (0.11 units) are outside the tile but within both arm end lines, so neighbouring tiles join without a seam. There are none between overlapping arms (sharp turns); the dead-end tip shadow is the tip scaled out by 0.11.
  - `FloorGeometry.Shade` is the CPU reference: 1 at the outline down to 0 one half-width inside, then 1 → 2 across the shadow. `FloorMesh.Build` returns only a quad covering the tile and its shadow; renderers evaluate the distance per pixel.
  - Size (`FloorMesh.Dimensions`): arm length and half-width are 0.75 × 0.4125 units for Long tiles and 0.5 × 0.413 for Short. Neon, NeonLight and Basic add 0.03 to the length and 0.0375 to the width; Minimal adds only the width. `TileDimensions` (length/width %) multiplies them from its floor onward without changing tile spacing.
  - Gems is a 1-unit square, unrotated, showing the gem image.
  - The style comes from `TileVisual.Style` (settings, ColorTrack, timed RecolorTrack), so a tile's shape can change mid-song. `FloorGeometry` values are equal for tiles that can share a mesh.
  - Not handled: custom track textures (`trackTexture`), the Neon multi-tap edge and the hit glow (`trackGlowIntensity`).
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
