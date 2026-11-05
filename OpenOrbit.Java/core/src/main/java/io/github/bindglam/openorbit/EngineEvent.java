package io.github.bindglam.openorbit;

/** {@link GameEngine} 이 내보내는 이벤트. 엔진 안에서 일어난 순서대로 나온다. */
public sealed interface EngineEvent {
    /** 판정 하나. 미드스핀 자동 통과(AUTO), 미스(TOO_LATE), 너무 빠름(TOO_EARLY) 포함. */
    record Hit(HitResult result) implements EngineEvent {}

    /** 마지막 타일까지 통과했다. */
    record Cleared() implements EngineEvent {}

    /** 미스로 실패했다 (NoFail 이 꺼져 있을 때만). */
    record Failed() implements EngineEvent {}
}
