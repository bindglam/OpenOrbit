package io.github.bindglam.openorbit;

/** 2D 벡터. 1 = 타일 한 칸, +Y 가 위쪽. */
public record Vec2(double x, double y) {
    public static final Vec2 ZERO = new Vec2(0, 0);
}
