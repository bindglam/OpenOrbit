package io.github.bindglam.openorbit;

import java.lang.foreign.MemorySegment;

import static java.lang.foreign.ValueLayout.JAVA_DOUBLE;
import static java.lang.foreign.ValueLayout.JAVA_INT;

/**
 * 컴파일된 타일 한 칸. "travel*" 값은 이 타일에서 다음 타일로 넘어가는 구간 기준.
 *
 * @param angle       원본 angleData 값 (0번 타일은 0, 미드스핀은 999).
 * @param timeMs      행성이 이 타일에 도착해야 하는 곡 기준 시각(ms).
 * @param bpm         이 타일을 떠날 때 적용되는 BPM (피치 미적용).
 * @param entryAngle  회전하는 행성의 출발 각도(도).
 * @param travelAngle 다음 타일까지 회전하는 각도(도). 마지막 타일은 0.
 * @param planets     이 타일을 떠날 때의 행성 개수.
 * @param pivotPlanet 이 타일에서 중심이 되는 행성 번호 (0 = 불, 1 = 얼음, 2.. = 3공, 4공 ...).
 */
public record Tile(
        int floor,
        double angle,
        boolean isMidspin,
        Vec2 position,
        double timeMs,
        double bpm,
        boolean clockwise,
        double entryAngle,
        double travelAngle,
        double travelMs,
        int planets,
        int pivotPlanet) {

    private static final long ANGLE = Native.offset(Native.TILE, "angle");
    private static final long POSITION_X = Native.offset(Native.TILE, "positionX");
    private static final long POSITION_Y = Native.offset(Native.TILE, "positionY");
    private static final long TIME_MS = Native.offset(Native.TILE, "timeMs");
    private static final long BPM = Native.offset(Native.TILE, "bpm");
    private static final long ENTRY_ANGLE = Native.offset(Native.TILE, "entryAngle");
    private static final long TRAVEL_ANGLE = Native.offset(Native.TILE, "travelAngle");
    private static final long TRAVEL_MS = Native.offset(Native.TILE, "travelMs");
    private static final long FLOOR = Native.offset(Native.TILE, "floor");
    private static final long IS_MIDSPIN = Native.offset(Native.TILE, "isMidspin");
    private static final long CLOCKWISE = Native.offset(Native.TILE, "clockwise");
    private static final long PLANETS = Native.offset(Native.TILE, "planets");
    private static final long PIVOT_PLANET = Native.offset(Native.TILE, "pivotPlanet");

    public double travelBeats() {
        return travelAngle / 180.0;
    }

    static Tile read(MemorySegment array, long index) {
        var s = array.asSlice(index * Native.TILE.byteSize(), Native.TILE.byteSize());
        return new Tile(
                s.get(JAVA_INT, FLOOR),
                s.get(JAVA_DOUBLE, ANGLE),
                s.get(JAVA_INT, IS_MIDSPIN) != 0,
                new Vec2(s.get(JAVA_DOUBLE, POSITION_X), s.get(JAVA_DOUBLE, POSITION_Y)),
                s.get(JAVA_DOUBLE, TIME_MS),
                s.get(JAVA_DOUBLE, BPM),
                s.get(JAVA_INT, CLOCKWISE) != 0,
                s.get(JAVA_DOUBLE, ENTRY_ANGLE),
                s.get(JAVA_DOUBLE, TRAVEL_ANGLE),
                s.get(JAVA_DOUBLE, TRAVEL_MS),
                s.get(JAVA_INT, PLANETS),
                s.get(JAVA_INT, PIVOT_PLANET));
    }
}
