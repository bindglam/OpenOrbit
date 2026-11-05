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
  - The camera comes from `Chart.Camera`. Like ADOFAI's `scrCamera`, the followed player position moves linearly to the landed planet over 2 beats each time a new floor is reached; `TilesPerScreenHeight` (10 / 1.5) converts ADOFAI zoom into `Camera2D.Zoom`.
- **`TrackView`:**
  - One `RenderingServer` canvas item per drawn tile, with a child item for icons. Geometry is recorded once, in white.
  - Every frame it evaluates `Chart.Track` and pushes only what changed: transform (offset, rotation, scale), `modulate` (opacity, inherited by icons) and `self_modulate` (track color, not inherited). Tiles are skipped until `TrackTimeline.StableUntil`, so static tiles cost nothing.
  - Passed tiles are darkened by multiplying the color (`DimPassedTiles`, not an ADOFAI feature).
  - Earlier tiles draw on top (draw index `n - i`), as in ADOFAI. Every floor is its own item; a midspin floor simply overlaps the tile it shares a position with.
  - Tiles are `FloorMesh` quads. One `ArrayMesh` per `FloorGeometry` and surface variant is shared by all tiles of that shape in a chart and rotated per tile by the mesh command's transform.
  - The shader (`addons/OpenOrbit.Godot/floor.gdshader`, loaded by `res://` path because `.uid` files are gitignored) repeats `FloorGeometry`'s distance function per pixel; keep the two in sync. Shape values live in a float data texture (`shape_data`, two texels per shape, numbered per chart). The quad's UV holds only small integers (the texel column, plus the variant and row), because Godot 2D mesh UVs don't carry large values exactly; mesh `CUSTOM0` data doesn't reach canvas shaders either.
  - Edges are not antialiased: blending would show gaps where tiles join.
  - Style changes re-record only that tile's mesh command and material. There is one material per track style with the values from `scrFloor.SetTrackStyle`:
    - Standard: noise and the surface pattern, border × 1.23, black shadow at 45%.
    - Basic and Minimal: no shadow.
    - Neon and NeonLight: a white shadow at 35%, tinted by the tile color, which reads as a glow.
    - Gems: draws `GemTexture` on the square (`sprite`).
    - `settings.trackShadowColor` overrides every style's shadow.
  - Shading matches the previous look: border ramp by depth × 1.23 (Standard and Gems) plus noise³, capped at 1; surface pattern in local units, scale 10, moved and turned per tile variant; shadow alpha `(1 − d/0.11)^1.3 · color.a`.
  - The textures are `BorderTexture` (Standard), `Basic/Neon/NeonLight/MinimalBorderTexture`, `SurfaceTexture`, `NoiseTexture` and `GemTexture`. Without them each style gets a similar built-in border gradient, there is no pattern, and gems are plain squares.
  - The surface offset differs per tile. Godot instance uniforms are capped at 4096 items, so instead each shape gets 16 meshes with a variant number in UV.x.
  - Performance: about 2–3 ms per frame on real charts (Hello (BPM) 2026 about 2.8 ms, against 1.9 ms for the earlier polygon meshes). Each tile is its own draw call, since meshes don't batch.
- **Example textures:** `examples/test_game/floor_*.png` are ADOFAI textures:
  - `floor_border.png` is `rs13`, and `floor_border_basic/neon/neonlight/minimal.png` are the matching `*_edge` ramps;
  - `floor_surface.png` is `tile_texture_2020_cracks_tiled_2048`, `floor_noise.png` is `perlin` and `floor_gem.png` is `gem_floorMesh`.
  - They are referenced from `RDConstants` in `resources.assets`. They are gitignored like `white.png` (only their `.import` files are tracked) and must not be committed.
- The camera height and planet size follow `Chart.UnitsPerTile`, so Short-tile charts show 10 tiles at 100% zoom and the planets are 1.5× larger relative to tiles (`PlanetView.SizeScale`).
- **`HitsoundMixer`:** synthesizes hitsounds sample-accurately at chart times into an `AudioStreamGenerator`, independent of input, so high-BPM streams don't smear.
- **`HitErrorMeter`:** ADOFAI-style timing meter. Errors are mapped piecewise so effective (ms-floored) window boundaries line up with fixed color bands.
