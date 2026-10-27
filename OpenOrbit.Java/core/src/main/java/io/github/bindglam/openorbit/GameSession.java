package io.github.bindglam.openorbit;

import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.ref.Cleaner;
import java.util.ArrayList;
import java.util.List;
import java.util.function.Consumer;

import static java.lang.foreign.ValueLayout.JAVA_LONG;

/**
 * 한 판의 진행 상태 (OpenOrbit.Core 의 GameSession). 시간은 호출자가 넣어 주고, 이 클래스는 순수 로직만 담당한다.
 * <p>
 * 스레드 안전하지 않다. 한 스레드에서만 쓰거나 호출자가 동기화해야 한다.
 * 다 쓰면 {@link #close()} 한다.
 */
public final class GameSession implements AutoCloseable {
    /** update 한 번에 네이티브에서 받아 오는 판정 개수. 넘치면 drain 으로 이어서 받는다. */
    private static final int RESULT_BUFFER_SIZE = 64;

    private final Chart chart;
    private final JudgmentWindows windows;
    private final NativeHandle handle;
    private final Cleaner.Cleanable cleanable;

    // 호출마다 할당하지 않도록 출력 버퍼를 세션이 들고 있는다
    private final Arena arena = Arena.ofAuto();
    private final MemorySegment resultBuffer = arena.allocate(Native.HIT_RESULT, RESULT_BUFFER_SIZE);
    private final MemorySegment planetBuffer = arena.allocate(Native.PLANET_STATE);

    public GameSession(Chart chart, JudgmentWindows windows) {
        this.chart = chart;
        this.windows = windows;

        long raw;
        try (var temp = Arena.ofConfined()) {
            var w = temp.allocate(Native.WINDOWS);
            windows.write(w);
            var out = temp.allocate(JAVA_LONG);
            Native.check((int) Native.SESSION_CREATE.invokeExact(chart.handle(), w, out));
            raw = out.get(JAVA_LONG, 0);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }

        handle = new NativeHandle(raw, Native.SESSION_DESTROY);
        cleanable = Chart.CLEANER.register(this, handle);
    }

    public Chart chart() {
        return chart;
    }

    public JudgmentWindows windows() {
        return windows;
    }

    /** 중심 행성이 서 있는 타일. */
    public int currentFloor() {
        try {
            return Native.check((int) Native.SESSION_CURRENT_FLOOR.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    public boolean isFinished() {
        try {
            return Native.check((int) Native.SESSION_IS_FINISHED.invokeExact(handle.get())) != 0;
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    public void reset() {
        try {
            Native.check((int) Native.SESSION_RESET.invokeExact(handle.get()));
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /**
     * 키 입력 처리. 판정 대상이 아니면(너무 빠름) TOO_EARLY 결과만 돌려주고 진행하지 않는다.
     *
     * @return 판정. 이미 끝난 판이면 null.
     */
    public HitResult press(double songTimeMs) {
        try {
            var produced = Native.check((int) Native.SESSION_PRESS.invokeExact(handle.get(), songTimeMs, resultBuffer));
            return produced == 0 ? null : HitResult.read(resultBuffer, 0);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /**
     * 매 프레임 호출. 미드스핀 자동 통과(AUTO), 판정 범위를 지나친 타일의 미스(TOO_LATE)를 처리하고
     * 새로 나온 판정을 순서대로 넘겨준다.
     */
    public void update(double songTimeMs, Consumer<HitResult> onResult) {
        try {
            var h = handle.get();
            var n = Native.check((int) Native.SESSION_UPDATE.invokeExact(h, songTimeMs, resultBuffer, RESULT_BUFFER_SIZE));
            while (n > 0) {
                for (var i = 0; i < n; i++)
                    onResult.accept(HitResult.read(resultBuffer, i));
                if (n < RESULT_BUFFER_SIZE)
                    break;
                n = Native.check((int) Native.SESSION_DRAIN.invokeExact(h, resultBuffer, RESULT_BUFFER_SIZE));
            }
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** {@link #update(double, Consumer)} 의 결과를 리스트로 받는다. 대부분 프레임은 빈 리스트. */
    public List<HitResult> update(double songTimeMs) {
        var results = new ArrayList<HitResult>(0);
        update(songTimeMs, results::add);
        return results;
    }

    /** 지금까지의 모든 판정. */
    public List<HitResult> results() {
        try (var temp = Arena.ofConfined()) {
            var h = handle.get();
            var count = Native.check((int) Native.SESSION_RESULT_COUNT.invokeExact(h));
            var buf = temp.allocate(Native.HIT_RESULT, Math.max(count, 1));
            var copied = Native.check((int) Native.SESSION_GET_RESULTS.invokeExact(h, 0, count, buf));

            var result = new HitResult[copied];
            for (var i = 0; i < copied; i++)
                result[i] = HitResult.read(buf, i);
            return List.of(result);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    /** 렌더링용: 현재 시각 기준 중심 행성 위치와 회전 행성의 각도. */
    public PlanetState planetState(double songTimeMs) {
        try {
            Native.check((int) Native.SESSION_GET_PLANET_STATE.invokeExact(handle.get(), songTimeMs, planetBuffer));
            return PlanetState.read(planetBuffer);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    @Override
    public void close() {
        cleanable.clean();
    }
}
