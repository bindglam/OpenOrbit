package io.github.bindglam.openorbit;

import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.ref.Cleaner;
import java.util.function.Consumer;

import static java.lang.foreign.ValueLayout.JAVA_DOUBLE;
import static java.lang.foreign.ValueLayout.JAVA_INT;
import static java.lang.foreign.ValueLayout.JAVA_LONG;

/**
 * 한 판의 진행을 담당하는 엔진 (OpenOrbit.Core 의 GameEngine).
 * <p>
 * 판정(미스, 미드스핀 자동 통과, 오토플레이)은 네이티브 쪽 업데이트 스레드가 곡 시계에 맞춰 돌린다.
 * 곡 시계도 네이티브 안의 단조 시계이며 {@link #start()} 시점이 0 ms 다.
 * 결과는 콜백 대신 큐에 쌓이고, 호출자가 {@link #awaitEvents} 로 꺼내 간다.
 * <p>
 * 모든 메서드는 여러 스레드에서 불러도 된다. 단 {@link #awaitEvents} 는 한 번에 한 스레드만 부르고,
 * {@link #close()} 는 다른 호출이 모두 끝난 뒤에 한다.
 */
public final class GameEngine implements AutoCloseable {
    public enum State { READY, PLAYING, FAILED, CLEARED }

    public enum Autoplay {
        /** 오토플레이 없음. */
        NONE,
        /** 플레이어와 똑같은 판정 범위를 적용받는 오토. */
        STRICT,
        /** 모든 타일을 완벽으로 처리하는 오토. */
        PERFECT
    }

    /** awaitEvents 한 번에 네이티브에서 받아 오는 이벤트 개수. 넘치면 기다리지 않고 이어서 받는다. */
    private static final int EVENT_BUFFER_SIZE = 64;

    private static final long EVENT_ERROR_MS = Native.offset(Native.ENGINE_EVENT, "errorMs");
    private static final long EVENT_FLOOR = Native.offset(Native.ENGINE_EVENT, "floor");
    private static final long EVENT_MARGIN = Native.offset(Native.ENGINE_EVENT, "margin");
    private static final long EVENT_KIND = Native.offset(Native.ENGINE_EVENT, "kind");
    private static final EngineEvent.Cleared CLEARED = new EngineEvent.Cleared();
    private static final EngineEvent.Failed FAILED = new EngineEvent.Failed();

    private final Chart chart;
    private final JudgmentWindows windows;
    private final NativeHandle handle;
    private final Cleaner.Cleanable cleanable;

    /** awaitEvents 전용 버퍼. 그 외 호출은 스레드가 섞이므로 호출마다 따로 할당한다. */
    private final MemorySegment eventBuffer = Arena.ofAuto().allocate(Native.ENGINE_EVENT, EVENT_BUFFER_SIZE);

    public GameEngine(Chart chart, JudgmentWindows windows) {
        this.chart = chart;
        this.windows = windows;

        long raw;
        try (var temp = Arena.ofConfined()) {
            var w = temp.allocate(Native.WINDOWS);
            windows.write(w);
            var out = temp.allocate(JAVA_LONG);
            Native.check((int) Native.ENGINE_CREATE.invokeExact(chart.handle(), w, out));
            raw = out.get(JAVA_LONG, 0);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }

        handle = new NativeHandle(raw, Native.ENGINE_DESTROY);
        cleanable = Chart.CLEANER.register(this, handle);
    }

    public Chart chart() {
        return chart;
    }

    public JudgmentWindows windows() {
        return windows;
    }

    public void setAutoplay(Autoplay autoplay) {
        try {
            Native.check((int) Native.ENGINE_SET_AUTOPLAY.invokeExact(handle.get(), autoplay.ordinal()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 켜면 미스가 나도 실패하지 않는다. */
    public void setNoFail(boolean noFail) {
        try {
            Native.check((int) Native.ENGINE_SET_NO_FAIL.invokeExact(handle.get(), noFail ? 1 : 0));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 판정과 행성 위치에 쓰는 곡 시각을 이만큼(ms) 당긴다. 오디오 지연이나 입력 오프셋 보정용. */
    public void setCalibrationMs(double ms) {
        try {
            Native.check((int) Native.ENGINE_SET_CALIBRATION_MS.invokeExact(handle.get(), ms));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 곡 시계를 0 부터 돌리고 판정을 시작한다. 진행 중이었으면 처음부터 다시 한다. */
    public void start() {
        try {
            Native.check((int) Native.ENGINE_START.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 처음 상태(READY)로 되돌린다. 쌓인 이벤트도 버린다. */
    public void reset() {
        try {
            Native.check((int) Native.ENGINE_RESET.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 진행 중이면 실패 처리한다. */
    public void fail() {
        try {
            Native.check((int) Native.ENGINE_FAIL.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 지금 시각으로 입력 하나를 판정한다. 결과는 이벤트로 나온다. */
    public void press() {
        try {
            Native.check((int) Native.ENGINE_PRESS.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    public State state() {
        try {
            return State.values()[Native.check((int) Native.ENGINE_STATE.invokeExact(handle.get()))];
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 판정 기준 곡 시각(ms, 보정 포함). 실패하면 그 시각에 멈춘다. */
    public double currentTimeMs() {
        try {
            var ms = (double) Native.ENGINE_CURRENT_TIME_MS.invokeExact(handle.get());
            if (Double.isNaN(ms))
                throw new OpenOrbitException(OpenOrbitException.INVALID_HANDLE, Native.lastError());
            return ms;
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 중심 행성이 서 있는 타일. */
    public int currentFloor() {
        try {
            return Native.check((int) Native.ENGINE_CURRENT_FLOOR.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 렌더링용: songTimeMs 기준 중심 행성 위치와 회전 행성의 각도. 업데이트 스레드와 동시에 불러도 된다. */
    public PlanetState planetState(double songTimeMs) {
        try (var temp = Arena.ofConfined()) {
            var out = temp.allocate(Native.PLANET_STATE);
            Native.check((int) Native.ENGINE_GET_PLANET_STATE.invokeExact(handle.get(), songTimeMs, out));
            return PlanetState.read(out);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 지금 시각 기준 행성 상태. */
    public PlanetState planetState() {
        return planetState(currentTimeMs());
    }

    /**
     * 쌓인 이벤트를 순서대로 넘겨준다. 하나도 없으면 최대 timeoutMillis 동안 기다린다 (0 이면 바로 돌아온다).
     * 한 번에 한 스레드만 불러야 한다.
     *
     * @return 넘겨준 이벤트 개수.
     */
    public synchronized int awaitEvents(int timeoutMillis, Consumer<EngineEvent> onEvent) {
        try {
            var h = handle.get();
            var total = 0;
            var n = Native.check((int) Native.ENGINE_WAIT_EVENTS.invokeExact(h, eventBuffer, EVENT_BUFFER_SIZE, timeoutMillis));
            while (n > 0) {
                for (var i = 0; i < n; i++)
                    onEvent.accept(readEvent(i));
                total += n;
                if (n < EVENT_BUFFER_SIZE)
                    break;
                n = Native.check((int) Native.ENGINE_WAIT_EVENTS.invokeExact(h, eventBuffer, EVENT_BUFFER_SIZE, 0));
            }
            return total;
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    private EngineEvent readEvent(long index) {
        var base = index * Native.ENGINE_EVENT.byteSize();
        var kind = eventBuffer.get(JAVA_INT, base + EVENT_KIND);
        return switch (kind) {
            case 0 -> new EngineEvent.Hit(new HitResult(
                    eventBuffer.get(JAVA_INT, base + EVENT_FLOOR),
                    HitMargin.of(eventBuffer.get(JAVA_INT, base + EVENT_MARGIN)),
                    eventBuffer.get(JAVA_DOUBLE, base + EVENT_ERROR_MS)));
            case 1 -> CLEARED;
            case 2 -> FAILED;
            default -> throw new IllegalStateException("알 수 없는 엔진 이벤트: " + kind);
        };
    }

    /** 업데이트 스레드를 멈추고 네이티브 엔진을 해제한다. 차트는 닫지 않는다. */
    @Override
    public void close() {
        cleanable.clean();
    }
}
