package io.github.bindglam.openorbit;

import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;

import static java.lang.foreign.ValueLayout.JAVA_DOUBLE;

/**
 * 각도 기준 판정 범위(도). 각 범위는 *Ms 이상의 시간을 보장한다 (실제 범위 = max(각도, ms 를 그 BPM 의 각도로 환산한 값)).
 *
 * @param tooEarlyDeg 이보다 일찍 누르면 입력을 무시한다 (TOO_EARLY, 타일 진행 없음).
 */
public record JudgmentWindows(
        double perfectDeg,
        double nearPerfectDeg,
        double veryDeg,
        double perfectMs,
        double nearPerfectMs,
        double veryMs,
        double tooEarlyDeg) {

    // 프리셋보다 먼저 초기화되어야 한다
    private static final String[] FIELDS = {
            "perfectDeg", "nearPerfectDeg", "veryDeg", "perfectMs", "nearPerfectMs", "veryMs", "tooEarlyDeg"};

    public static final JudgmentWindows LENIENT = preset(0);
    public static final JudgmentWindows NORMAL = preset(1);
    public static final JudgmentWindows STRICT = preset(2);
    public static final JudgmentWindows SUPER_STRICT = preset(3);

    private static JudgmentWindows preset(int preset) {
        try (var arena = Arena.ofConfined()) {
            var s = arena.allocate(Native.WINDOWS);
            Native.check((int) Native.WINDOWS_PRESET.invokeExact(preset, s));
            var v = new double[FIELDS.length];
            for (var i = 0; i < v.length; i++)
                v[i] = s.get(JAVA_DOUBLE, Native.offset(Native.WINDOWS, FIELDS[i]));
            return new JudgmentWindows(v[0], v[1], v[2], v[3], v[4], v[5], v[6]);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }

    void write(MemorySegment s) {
        double[] v = {perfectDeg, nearPerfectDeg, veryDeg, perfectMs, nearPerfectMs, veryMs, tooEarlyDeg};
        for (var i = 0; i < v.length; i++)
            s.set(JAVA_DOUBLE, Native.offset(Native.WINDOWS, FIELDS[i]), v[i]);
    }
}
