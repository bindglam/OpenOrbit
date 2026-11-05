# OpenOrbit.Java

## Commands

- Multi-project Gradle build (Java 25, wrapper pinned to 9.6 because run-paper 3.0.2 fails on 9.8). `./gradlew :core:publishNative` runs the native `dotnet publish` for the host RID; `./gradlew build` bundles `bin/publish/<rid>/openorbit.*` into the jars.
- Minecraft test server: `./gradlew :examples:minecraft-plugin:runServer` (Paper 26.1.2, run dir `examples/minecraft-plugin/run/`).

## Architecture

Gradle, outside the .NET solution.

- `core`: FFM (`java.lang.foreign`) bindings, package `io.github.bindglam.openorbit`. Loads the library from `-Dopenorbit.library.path`, then `natives/<rid>/` in the jar, then the OS search path.
  - `GameEngine` wraps the native `oo_engine_*` API: the C# `GameEngine` runs on its own .NET thread with a Stopwatch clock inside the library, and its `Hit`/`Cleared`/`Failed` events go into a queue that Java drains with `awaitEvents` (blocks in native up to a timeout). There are deliberately no upcalls from native into Java.
  - `GameSession` is the clock-less, thread-unsafe alternative for callers that drive time themselves.
- `examples/minecraft-plugin`: Paper plugin (`io.github.bindglam.openorbit.minecraft`). Renders the chart with per-player display entities, mounts the player on a camera entity, takes movement-key edges (`PlayerInputEvent`) and arm swings as hits, and serves each `.ogg` song as a generated resource pack over a built-in HTTP server. Judgment and the song clock live in the native `GameEngine`, not server ticks.
  - Each `Game` has an event thread that waits on `awaitEvents` (15 ms timeout, which doubles as the particle-trail interval) and sends sounds, action bars, titles and particles straight from there. The main thread only forwards input (`engine.press()`) and moves entities in `render()` (Bukkit entity calls must stay on the main thread).
  - The input offset is folded into the engine's calibration, so rendering adds it back (`displayTimeMs`) to stay in sync with the audio.
  - Twirl/SetSpeed markers are derived from `Tile.clockwise`/`Tile.bpm` changes against the previous tile, not from raw events.
