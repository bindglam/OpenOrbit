package io.github.bindglam.openorbit;

/** 판정. 순서(ordinal)가 OpenOrbit.Core 의 HitMargin 값과 같아야 한다. */
public enum HitMargin {
    TOO_EARLY,
    VERY_EARLY,
    EARLY_PERFECT,
    PERFECT,
    LATE_PERFECT,
    VERY_LATE,
    TOO_LATE,
    /** 미드스핀처럼 입력 없이 자동으로 지나간 타일. */
    AUTO;

    private static final HitMargin[] VALUES = values();

    static HitMargin of(int value) {
        return VALUES[value];
    }
}
