package io.github.bindglam.openorbit;

import java.io.IOException;
import java.io.InputStream;
import java.io.UncheckedIOException;
import java.lang.foreign.*;
import java.lang.invoke.MethodHandle;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.Locale;

import static java.lang.foreign.MemoryLayout.PathElement.groupElement;
import static java.lang.foreign.ValueLayout.*;

/**
 * openorbit 네이티브 라이브러리(OpenOrbit.Native, NativeAOT) 바인딩.
 * <p>
 * 라이브러리 위치: 시스템 프로퍼티 {@code openorbit.library.path} → jar 안의 {@code natives/<rid>/} → OS 라이브러리 검색 경로.
 * 구조체 레이아웃은 OpenOrbit.Native/NativeTypes.cs 와 일치해야 한다.
 */
final class Native {
    private Native() {}

    // ---------- 구조체 ----------

    static final StructLayout TILE = MemoryLayout.structLayout(
            JAVA_DOUBLE.withName("angle"),
            JAVA_DOUBLE.withName("positionX"),
            JAVA_DOUBLE.withName("positionY"),
            JAVA_DOUBLE.withName("timeMs"),
            JAVA_DOUBLE.withName("bpm"),
            JAVA_DOUBLE.withName("entryAngle"),
            JAVA_DOUBLE.withName("travelAngle"),
            JAVA_DOUBLE.withName("travelMs"),
            JAVA_INT.withName("floor"),
            JAVA_INT.withName("isMidspin"),
            JAVA_INT.withName("clockwise"),
            JAVA_INT.withName("planets"),
            JAVA_INT.withName("pivotPlanet"),
            MemoryLayout.paddingLayout(4));

    static final StructLayout SETTINGS = MemoryLayout.structLayout(
            JAVA_DOUBLE.withName("bpm"),
            JAVA_DOUBLE.withName("offsetMs"),
            JAVA_DOUBLE.withName("pitch"),
            JAVA_INT.withName("countdownTicks"),
            MemoryLayout.paddingLayout(4));

    static final StructLayout WINDOWS = MemoryLayout.structLayout(
            JAVA_DOUBLE.withName("perfectDeg"),
            JAVA_DOUBLE.withName("nearPerfectDeg"),
            JAVA_DOUBLE.withName("veryDeg"),
            JAVA_DOUBLE.withName("perfectMs"),
            JAVA_DOUBLE.withName("nearPerfectMs"),
            JAVA_DOUBLE.withName("veryMs"),
            JAVA_DOUBLE.withName("tooEarlyDeg"));

    static final StructLayout HIT_RESULT = MemoryLayout.structLayout(
            JAVA_DOUBLE.withName("errorMs"),
            JAVA_INT.withName("floor"),
            JAVA_INT.withName("margin"));

    static final StructLayout PLANET_STATE = MemoryLayout.structLayout(
            JAVA_DOUBLE.withName("pivotX"),
            JAVA_DOUBLE.withName("pivotY"),
            JAVA_DOUBLE.withName("orbitingX"),
            JAVA_DOUBLE.withName("orbitingY"),
            JAVA_DOUBLE.withName("orbitingAngle"),
            JAVA_INT.withName("planets"),
            JAVA_INT.withName("pivotPlanet"),
            JAVA_INT.withName("clockwise"),
            MemoryLayout.paddingLayout(4));

    static long offset(StructLayout layout, String field) {
        return layout.byteOffset(groupElement(field));
    }

    // ---------- 함수 ----------

    private static final SymbolLookup LOOKUP = loadLibrary();
    private static final Linker LINKER = Linker.nativeLinker();

    static final MethodHandle LAST_ERROR = fn("oo_last_error", JAVA_INT, ADDRESS, JAVA_INT);
    static final MethodHandle WINDOWS_PRESET = fn("oo_windows_preset", JAVA_INT, JAVA_INT, ADDRESS);
    static final MethodHandle PLANET_POSITION_AT = fn("oo_planet_position_at", JAVA_INT, ADDRESS, JAVA_INT, ADDRESS);

    static final MethodHandle CHART_LOAD = fn("oo_chart_load", JAVA_INT, ADDRESS, JAVA_INT, ADDRESS);
    static final MethodHandle CHART_DESTROY = fnVoid("oo_chart_destroy", JAVA_LONG);
    static final MethodHandle CHART_TILE_COUNT = fn("oo_chart_tile_count", JAVA_INT, JAVA_LONG);
    static final MethodHandle CHART_GET_TILES = fn("oo_chart_get_tiles", JAVA_INT, JAVA_LONG, JAVA_INT, JAVA_INT, ADDRESS);
    static final MethodHandle CHART_GET_SETTINGS = fn("oo_chart_get_settings", JAVA_INT, JAVA_LONG, ADDRESS);
    static final MethodHandle CHART_GET_SONG_FILENAME = fn("oo_chart_get_song_filename", JAVA_INT, JAVA_LONG, ADDRESS, JAVA_INT);
    static final MethodHandle CHART_MS_PER_DEGREE = fn("oo_chart_ms_per_degree", JAVA_DOUBLE, JAVA_LONG, JAVA_DOUBLE);

    static final MethodHandle SESSION_CREATE = fn("oo_session_create", JAVA_INT, JAVA_LONG, ADDRESS, ADDRESS);
    static final MethodHandle SESSION_DESTROY = fnVoid("oo_session_destroy", JAVA_LONG);
    static final MethodHandle SESSION_RESET = fn("oo_session_reset", JAVA_INT, JAVA_LONG);
    static final MethodHandle SESSION_PRESS = fn("oo_session_press", JAVA_INT, JAVA_LONG, JAVA_DOUBLE, ADDRESS);
    static final MethodHandle SESSION_UPDATE = fn("oo_session_update", JAVA_INT, JAVA_LONG, JAVA_DOUBLE, ADDRESS, JAVA_INT);
    static final MethodHandle SESSION_DRAIN = fn("oo_session_drain", JAVA_INT, JAVA_LONG, ADDRESS, JAVA_INT);
    static final MethodHandle SESSION_CURRENT_FLOOR = fn("oo_session_current_floor", JAVA_INT, JAVA_LONG);
    static final MethodHandle SESSION_IS_FINISHED = fn("oo_session_is_finished", JAVA_INT, JAVA_LONG);
    static final MethodHandle SESSION_RESULT_COUNT = fn("oo_session_result_count", JAVA_INT, JAVA_LONG);
    static final MethodHandle SESSION_GET_RESULTS = fn("oo_session_get_results", JAVA_INT, JAVA_LONG, JAVA_INT, JAVA_INT, ADDRESS);
    static final MethodHandle SESSION_GET_PLANET_STATE = fn("oo_session_get_planet_state", JAVA_INT, JAVA_LONG, JAVA_DOUBLE, ADDRESS);

    private static MethodHandle fn(String name, MemoryLayout result, MemoryLayout... args) {
        return LINKER.downcallHandle(find(name), FunctionDescriptor.of(result, args));
    }

    private static MethodHandle fnVoid(String name, MemoryLayout... args) {
        return LINKER.downcallHandle(find(name), FunctionDescriptor.ofVoid(args));
    }

    private static MemorySegment find(String name) {
        return LOOKUP.find(name).orElseThrow(() -> new UnsatisfiedLinkError("openorbit 에 " + name + " 함수가 없습니다."));
    }

    // ---------- 오류 ----------

    /** 상태 코드가 음수면 이 스레드의 마지막 오류 메시지로 예외를 던진다. 0 이상이면 그대로 돌려준다. */
    static int check(int status) {
        if (status < 0)
            throw new OpenOrbitException(status, lastError());
        return status;
    }

    static String lastError() {
        try (var arena = Arena.ofConfined()) {
            var length = (int) LAST_ERROR.invokeExact(MemorySegment.NULL, 0);
            if (length <= 0)
                return "";
            var buf = arena.allocate(length);
            var written = (int) LAST_ERROR.invokeExact(buf, length);
            return new String(buf.toArray(JAVA_BYTE), 0, Math.min(written, length), StandardCharsets.UTF_8);
        } catch (Throwable t) {
            throw rethrow(t);
        }
    }

    /** MethodHandle.invokeExact 가 던지는 Throwable 을 그대로 다시 던진다. */
    static RuntimeException rethrow(Throwable t) {
        if (t instanceof RuntimeException e) throw e;
        if (t instanceof Error e) throw e;
        throw new OpenOrbitException(OpenOrbitException.INTERNAL_ERROR, t.toString(), t);
    }

    // ---------- 로딩 ----------

    private static SymbolLookup loadLibrary() {
        var arena = Arena.global();

        var override = System.getProperty("openorbit.library.path");
        if (override != null)
            return SymbolLookup.libraryLookup(Path.of(override), arena);

        var fileName = libraryFileName();
        var resource = "/natives/" + runtimeId() + "/" + fileName;
        try (InputStream in = Native.class.getResourceAsStream(resource)) {
            if (in != null) {
                var dir = Files.createTempDirectory("openorbit");
                var file = dir.resolve(fileName);
                Files.copy(in, file, StandardCopyOption.REPLACE_EXISTING);
                file.toFile().deleteOnExit();
                dir.toFile().deleteOnExit();
                return SymbolLookup.libraryLookup(file, arena);
            }
        } catch (IOException e) {
            throw new UncheckedIOException("openorbit 네이티브 라이브러리를 풀지 못했습니다: " + resource, e);
        }

        return SymbolLookup.libraryLookup(fileName, arena);
    }

    /** .NET RID 와 같은 형식 (osx-arm64, linux-x64, win-x64 ...). */
    static String runtimeId() {
        var os = System.getProperty("os.name").toLowerCase(Locale.ROOT);
        var arch = System.getProperty("os.arch").toLowerCase(Locale.ROOT);

        String osPart;
        if (os.contains("mac") || os.contains("darwin")) osPart = "osx";
        else if (os.contains("win")) osPart = "win";
        else if (os.contains("linux")) osPart = "linux";
        else throw new UnsupportedOperationException("지원하지 않는 OS: " + os);

        String archPart = switch (arch) {
            case "aarch64", "arm64" -> "arm64";
            case "amd64", "x86_64" -> "x64";
            default -> throw new UnsupportedOperationException("지원하지 않는 아키텍처: " + arch);
        };
        return osPart + "-" + archPart;
    }

    /** NativeAOT 는 lib 접두사를 붙이지 않는다. */
    private static String libraryFileName() {
        var os = System.getProperty("os.name").toLowerCase(Locale.ROOT);
        if (os.contains("mac") || os.contains("darwin")) return "openorbit.dylib";
        if (os.contains("win")) return "openorbit.dll";
        return "openorbit.so";
    }
}
