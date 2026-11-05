# OpenOrbit.Java

## Commands

- Multi-project Gradle build (Java 25, wrapper pinned to 9.6 because run-paper 3.0.2 fails on 9.8). `./gradlew :core:publishNative` runs the native `dotnet publish` for the host RID; `./gradlew build` bundles `bin/publish/<rid>/openorbit.*` into the jars.
- Minecraft test server: `./gradlew :examples:minecraft-plugin:runServer` (Paper 26.1.2, run dir `examples/minecraft-plugin/run/`).

## Architecture

Gradle, outside the .NET solution.

- `core`: FFM (`java.lang.foreign`) bindings, package `io.github.bindglam.openorbit`. Loads the library from `-Dopenorbit.library.path`, then `natives/<rid>/` in the jar, then the OS search path.
- `examples/minecraft-plugin`: Paper plugin (`io.github.bindglam.openorbit.minecraft`). Renders the chart with per-player display entities, mounts the player on a camera entity, takes movement-key edges (`PlayerInputEvent`) and arm swings as hits, and serves each `.ogg` song as a generated resource pack over a built-in HTTP server. The song clock is `System.nanoTime`, not server ticks.
  - Each `Game` runs judgment (misses, midspins, autoplay) and particle trails on its own thread every 1 ms under `Game.lock`; the main thread only handles input events and moves entities in `render()` (Bukkit entity calls must stay on the main thread). Sounds, titles and particles are sent straight from the game thread.
  - Twirl/SetSpeed markers are derived from `Tile.clockwise`/`Tile.bpm` changes against the previous tile, not from raw events.
