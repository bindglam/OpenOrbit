package io.github.bindglam.openorbit;

import java.lang.invoke.MethodHandle;

/**
 * 네이티브 핸들과 해제 함수. Cleaner 동작으로도 쓰이므로 소유 객체를 참조하면 안 된다.
 * 해제된 핸들을 네이티브에 넘기면 프로세스가 죽을 수 있으므로 {@link #get()} 이 먼저 막는다.
 */
final class NativeHandle implements Runnable {
    private final MethodHandle destroy;
    private volatile long value;

    NativeHandle(long value, MethodHandle destroy) {
        this.value = value;
        this.destroy = destroy;
    }

    long get() {
        var v = value;
        if (v == 0)
            throw new IllegalStateException("이미 닫힌 객체입니다.");
        return v;
    }

    @Override
    public synchronized void run() {
        var v = value;
        if (v == 0)
            return;
        value = 0;
        try {
            destroy.invokeExact(v);
        } catch (Throwable t) {
            throw Native.rethrow(t);
        }
    }
}
