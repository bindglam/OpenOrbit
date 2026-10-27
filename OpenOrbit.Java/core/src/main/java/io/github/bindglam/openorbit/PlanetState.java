package io.github.bindglam.openorbit;

import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;

import static java.lang.foreign.ValueLayout.JAVA_DOUBLE;
import static java.lang.foreign.ValueLayout.JAVA_INT;

/**
 * 렌더링용 행성 상태.
 *
 * @param planets     행성 개수.
 * @param pivotPlanet 중심 행성 번호 (0 = 불, 1 = 얼음, 2.. = 3공, 4공 ...).
 */
public record PlanetState(
        Vec2 pivotPosition,
        Vec2 orbitingPosition,
        double orbitingAngle,
        int planets,
        int pivotPlanet,
        boolean clockwise) {

    private static final long PIVOT_X = Native.offset(Native.PLANET_STATE, "pivotX");
    private static final long PIVOT_Y = Native.offset(Native.PLANET_STATE, "pivotY");
    private static final long ORBITING_X = Native.offset(Native.PLANET_STATE, "orbitingX");
    private static final long ORBITING_Y = Native.offset(Native.PLANET_STATE, "orbitingY");
    private static final long ORBITING_ANGLE = Native.offset(Native.PLANET_STATE, "orbitingAngle");
    private static final long PLANETS = Native.offset(Native.PLANET_STATE, "planets");
    private static final long PIVOT_PLANET = Native.offset(Native.PLANET_STATE, "pivotPlanet");
    private static final long CLOCKWISE = Native.offset(Native.PLANET_STATE, "clockwise");

    /** 순서 k 번째 행성의 위치. 0 = 중심, 1 = 회전 중, 2.. = 그다음에 회전할 행성. */
    public Vec2 positionAt(int k) {
        if (k == 0) return pivotPosition;
        if (k == 1) return orbitingPosition;

        try (var arena = Arena.ofConfined()) {
            var s = arena.allocate(Native.PLANET_STATE);
            write(s);
            var xy = arena.allocate(JAVA_DOUBLE, 2);
            Native.check((int) Native.PLANET_POSITION_AT.invokeExact(s, k, xy));
            return new Vec2(xy.getAtIndex(JAVA_DOUBLE, 0), xy.getAtIndex(JAVA_DOUBLE, 1));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 순서 k 번째 자리에 있는 행성 번호. */
    public int planetAt(int k) {
        return (pivotPlanet + k) % planets;
    }

    static PlanetState read(MemorySegment s) {
        return new PlanetState(
                new Vec2(s.get(JAVA_DOUBLE, PIVOT_X), s.get(JAVA_DOUBLE, PIVOT_Y)),
                new Vec2(s.get(JAVA_DOUBLE, ORBITING_X), s.get(JAVA_DOUBLE, ORBITING_Y)),
                s.get(JAVA_DOUBLE, ORBITING_ANGLE),
                s.get(JAVA_INT, PLANETS),
                s.get(JAVA_INT, PIVOT_PLANET),
                s.get(JAVA_INT, CLOCKWISE) != 0);
    }

    private void write(MemorySegment s) {
        s.set(JAVA_DOUBLE, PIVOT_X, pivotPosition.x());
        s.set(JAVA_DOUBLE, PIVOT_Y, pivotPosition.y());
        s.set(JAVA_DOUBLE, ORBITING_X, orbitingPosition.x());
        s.set(JAVA_DOUBLE, ORBITING_Y, orbitingPosition.y());
        s.set(JAVA_DOUBLE, ORBITING_ANGLE, orbitingAngle);
        s.set(JAVA_INT, PLANETS, planets);
        s.set(JAVA_INT, PIVOT_PLANET, pivotPlanet);
        s.set(JAVA_INT, CLOCKWISE, clockwise ? 1 : 0);
    }
}
