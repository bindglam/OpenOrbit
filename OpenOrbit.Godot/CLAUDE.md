# Godot example

## Commands

- Run the example game: open `OpenOrbit.Godot/project.godot` in Godot 4.7 (.NET build). The binary is at `/Applications/Godot.app/Contents/MacOS/Godot`. `project.godot` and `OpenOrbit.Godot.csproj` are gitignored, so a fresh worktree has neither.
- The library code lives in `addons/OpenOrbit.Godot/`; the playable game is `addons/OpenOrbit.Godot/examples/test_game/game.tscn`.
- Run log: `~/Library/Application Support/Godot/app_userdata/OpenOrbit.Godot/logs/`. Godot thread-guard errors show up here.
- Saved settings (offset, last chart): `user://settings.cfg` in that same directory.
- Default test chart: `addons/OpenOrbit.Godot/examples/test_game/charts/Kplecraft - 16383/16383.adofai`.
- To verify game behavior, temporarily add a scene and script under `_Verify/` that instantiates `res://addons/OpenOrbit.Godot/examples/test_game/game.tscn` and drives it (charts can be loaded by absolute path through `Game.TryLoadChart`). Run it with `Godot --path OpenOrbit.Godot --headless [--audio-driver CoreAudio] res://_Verify/<scene>.tscn`, then delete `_Verify/`.
  - Headless mode does not render. To check visuals, run without `--headless` (add `--resolution 960x540`) and save `GetViewport().GetTexture().GetImage()` to a PNG.

## Architecture

A playable Godot 4.7 frontend that references Core. The Godot game uses the `Strict` judgment preset.

- **`Game`:**
  - Orchestrates loading, state transitions, input and the HUD.
  - Implements `IInputEventBus` itself.
  - `SongClock` is the `IGameClock`: it runs on a system timer that is nudged toward the audio playback position each frame. `Stop()` may be called from the engine thread, so Godot node calls are deferred to the main thread.
  - Feeds `TrackView.SongTimeMs` every frame. With `stickToFloors`, planets are shifted by the pivot tile's MoveTrack offset.
  - The camera comes from `Chart.Camera`: `Game` passes a smoothed player position (exponential follow), and `TilesPerScreenHeight` (a guess) converts ADOFAI zoom into `Camera2D.Zoom`.
- **`TrackView`:**
  - One `RenderingServer` canvas item per drawn tile, with a child item for icons. Geometry is recorded once, in white.
  - Every frame it evaluates `Chart.Track` and pushes only what changed: transform (offset, rotation, scale), `modulate` (opacity, inherited by icons) and `self_modulate` (track color, not inherited). Tiles are skipped until `TrackTimeline.StableUntil`, so static tiles cost nothing.
  - Passed tiles are darkened by multiplying the color (`DimPassedTiles`, not an ADOFAI feature).
  - Earlier tiles draw on top (draw index `n - i`).
  - Tiles are built from `arm.png`/`joint.png` (two passes: outline, then fill); the dead-end tile before a midspin uses `midspin.png`. Arm directions use the original positions, so arms don't follow neighbours moved by MoveTrack.
  - A midspin is drawn as part of the tile at the same position and follows that tile's visual; MoveTrack/RecolorTrack aimed only at the midspin floor are not shown.
- **`HitsoundMixer`:** synthesizes hitsounds sample-accurately at chart times into an `AudioStreamGenerator`, independent of input, so high-BPM streams don't smear.
- **`HitErrorMeter`:** ADOFAI-style timing meter. Errors are mapped piecewise so effective (ms-floored) window boundaries line up with fixed color bands.
