package io.github.bindglam.openorbit;

import java.io.IOException;
import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.ref.Cleaner;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;

import static java.lang.foreign.ValueLayout.*;

/**
 * 타이밍/위치까지 계산해 둔 채보. 타일과 설정은 불러올 때 모두 Java 로 복사해 두므로 읽기는 네이티브 호출 없이 된다.
 * <p>
 * 다 쓰면 {@link #close()} 한다. 이 차트로 만든 {@link GameSession} 은 닫은 뒤에도 계속 쓸 수 있다.
 */
public final class Chart implements AutoCloseable {
    static final Cleaner CLEANER = Cleaner.create();

    private final NativeHandle handle;
    private final Cleaner.Cleanable cleanable;
    private final List<Tile> tiles;
    private final LevelSettings settings;

    private Chart(long rawHandle) {
        handle = new NativeHandle(rawHandle, Native.CHART_DESTROY);
        cleanable = CLEANER.register(this, handle);
        try {
            tiles = readTiles(rawHandle);
            settings = readSettings(rawHandle);
        } catch (RuntimeException e) {
            close();
            throw e;
        }
    }

    /** .adofai 파일을 읽어 파싱하고 컴파일한다. */
    public static Chart load(Path path) throws IOException {
        return parse(Files.readAllBytes(path));
    }

    /** .adofai JSON 문자열을 파싱하고 컴파일한다. */
    public static Chart parse(String json) {
        return parse(json.getBytes(StandardCharsets.UTF_8));
    }

    /** .adofai JSON(UTF-8, BOM 허용)을 파싱하고 컴파일한다. */
    public static Chart parse(byte[] utf8Json) {
        try (var arena = Arena.ofConfined()) {
            var json = arena.allocateFrom(JAVA_BYTE, utf8Json);
            var out = arena.allocate(JAVA_LONG);
            Native.check((int) Native.CHART_LOAD.invokeExact(json, utf8Json.length, out));
            return new Chart(out.get(JAVA_LONG, 0));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    public List<Tile> tiles() {
        return tiles;
    }

    public Tile tile(int floor) {
        return tiles.get(floor);
    }

    public LevelSettings settings() {
        return settings;
    }

    /** 피치 반영 후 1도 회전에 걸리는 ms. */
    public double msPerDegree(double bpm) {
        try {
            return (double) Native.CHART_MS_PER_DEGREE.invokeExact(handle(), bpm);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    long handle() {
        return handle.get();
    }

    @Override
    public void close() {
        cleanable.clean();
    }

    private static List<Tile> readTiles(long h) {
        try (var arena = Arena.ofConfined()) {
            var count = Native.check((int) Native.CHART_TILE_COUNT.invokeExact(h));
            var buf = arena.allocate(Native.TILE, Math.max(count, 1));
            var copied = Native.check((int) Native.CHART_GET_TILES.invokeExact(h, 0, count, buf));

            var result = new Tile[copied];
            for (var i = 0; i < copied; i++)
                result[i] = Tile.read(buf, i);
            return List.of(result);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    private static LevelSettings readSettings(long h) {
        try (var arena = Arena.ofConfined()) {
            var s = arena.allocate(Native.SETTINGS);
            Native.check((int) Native.CHART_GET_SETTINGS.invokeExact(h, s));

            var length = Native.check((int) Native.CHART_GET_SONG_FILENAME.invokeExact(h, MemorySegment.NULL, 0));
            var name = "";
            if (length > 0) {
                var buf = arena.allocate(length);
                Native.check((int) Native.CHART_GET_SONG_FILENAME.invokeExact(h, buf, length));
                name = new String(buf.toArray(JAVA_BYTE), StandardCharsets.UTF_8);
            }

            return new LevelSettings(
                    s.get(JAVA_DOUBLE, Native.offset(Native.SETTINGS, "bpm")),
                    s.get(JAVA_DOUBLE, Native.offset(Native.SETTINGS, "offsetMs")),
                    s.get(JAVA_DOUBLE, Native.offset(Native.SETTINGS, "pitch")),
                    s.get(JAVA_INT, Native.offset(Native.SETTINGS, "countdownTicks")),
                    name);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }
}
