package io.github.bindglam.openorbit;

/** 네이티브 호출이 실패했을 때. {@link #status()} 는 OpenOrbit.Native 의 OoStatus 값. */
public final class OpenOrbitException extends RuntimeException {
    public static final int INVALID_HANDLE = -1;
    public static final int INVALID_ARGUMENT = -2;
    public static final int PARSE_ERROR = -3;
    public static final int IO_ERROR = -4;
    public static final int INTERNAL_ERROR = -5;

    private final int status;

    public OpenOrbitException(int status, String message) {
        this(status, message, null);
    }

    public OpenOrbitException(int status, String message, Throwable cause) {
        super(message, cause);
        this.status = status;
    }

    public int status() {
        return status;
    }
}
