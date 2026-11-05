# Godot example

## Commands

- Run the example game: open `OpenOrbit.Examples.Godot/project.godot` in Godot 4.7 (.NET build). The binary is at `/Applications/Godot.app/Contents/MacOS/Godot`.
- Run log: `~/Library/Application Support/Godot/app_userdata/OpenOrbit.Examples.Godot/logs/`. Godot thread-guard errors show up here.
- Saved settings (offset, last chart): `user://settings.cfg` in that same directory.
- Default test chart: `OpenOrbit.Examples.Godot/charts/Kplecraft - 16383/16383.adofai`.
- To verify game behavior headlessly, temporarily add a scene and script under `OpenOrbit.Examples.Godot/_Verify/` that instantiates `res://scenes/game.tscn` and drives it. Run it with `Godot --path OpenOrbit.Examples.Godot --headless [--audio-driver CoreAudio] res://_Verify/<scene>.tscn`, then delete `_Verify/`.

## Architecture

A playable Godot 4.7 frontend that references Core. The Godot game uses the `Strict` judgment preset.

- **`Game`:**
  - Orchestrates loading, state transitions, input and the HUD.
  - Implements `IInputEventBus` itself.
  - `SongClock` is the `IGameClock`: it runs on a system timer that is nudged toward the audio playback position each frame. `Stop()` may be called from the engine thread, so Godot node calls are deferred to the main thread.
- **`TrackView`:**
  - Draw commands are recorded once and re-recorded only for tiles whose passed/unpassed state changes.
  - Earlier tiles draw on top (draw index `n - i`).
  - Tiles are built from `arm.png`/`joint.png` (two passes: outline, then fill); the dead-end tile before a midspin uses `midspin.png`.
- **`HitsoundMixer`:** synthesizes hitsounds sample-accurately at chart times into an `AudioStreamGenerator`, independent of input, so high-BPM streams don't smear.
- **`HitErrorMeter`:** ADOFAI-style timing meter. Errors are mapped piecewise so effective (ms-floored) window boundaries line up with fixed color bands.
