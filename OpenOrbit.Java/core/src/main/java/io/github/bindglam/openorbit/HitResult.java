package io.github.bindglam.openorbit;

import java.lang.foreign.MemorySegment;

import static java.lang.foreign.ValueLayout.JAVA_DOUBLE;
import static java.lang.foreign.ValueLayout.JAVA_INT;

/** @param errorMs 음수 = 빠름, 양수 = 느림. */
public record HitResult(int floor, HitMargin margin, double errorMs) {
    private static final long ERROR_MS = Native.offset(Native.HIT_RESULT, "errorMs");
    private static final long FLOOR = Native.offset(Native.HIT_RESULT, "floor");
    private static final long MARGIN = Native.offset(Native.HIT_RESULT, "margin");

    static HitResult read(MemorySegment array, long index) {
        var base = index * Native.HIT_RESULT.byteSize();
        return new HitResult(
                array.get(JAVA_INT, base + FLOOR),
                HitMargin.of(array.get(JAVA_INT, base + MARGIN)),
                array.get(JAVA_DOUBLE, base + ERROR_MS));
    }
}
